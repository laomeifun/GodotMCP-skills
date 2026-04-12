#if TOOLS
using Godot;
using Godot.Collections;
using GodotMCP.Handlers;
using System;
using System.Threading;
using System.Threading.Tasks;
using GDArray = Godot.Collections.Array;

namespace GodotMCP;

public sealed class RuntimeBridgeService
{
    private sealed class PendingRuntimeRequest
    {
        public TaskCompletionSource<Dictionary> Completion { get; }
        public CancellationTokenSource TimeoutSource { get; }

        public PendingRuntimeRequest(TaskCompletionSource<Dictionary> completion, CancellationTokenSource timeoutSource)
        {
            Completion = completion;
            TimeoutSource = timeoutSource;
        }
    }

    public static RuntimeBridgeService Instance { get; } = new();

    private readonly System.Collections.Generic.Dictionary<string, PendingRuntimeRequest> _pendingRequests = new();
    private readonly GDArray _recentLogs = new();
    private const int MaxLogEntries = 250;
    private const int HeartbeatIntervalMs = 5000;
    private const int HeartbeatTimeoutMs = 15000;

    private RuntimeBridgeDebuggerPlugin? _debuggerPlugin;
    private Dictionary _lastStatus = new();
    private int? _activeSessionId;
    private bool _runtimeConnected;
    private ulong _lastReadyMs;
    private ulong _lastPongMs;
    private ulong _lastHeartbeatSentMs;
    private string _lastDisconnectReason = string.Empty;
    private ulong _lastDisconnectMs;

    private RuntimeBridgeService()
    {
    }

    public void AttachDebugger(RuntimeBridgeDebuggerPlugin debuggerPlugin)
    {
        _debuggerPlugin = debuggerPlugin;
    }

    public void Shutdown()
    {
        RejectAllPendingRequests("Runtime bridge is shutting down.");
        _debuggerPlugin = null;
        _lastStatus = new Dictionary();
        _activeSessionId = null;
        _runtimeConnected = false;
        _lastReadyMs = 0;
        _lastPongMs = 0;
        _lastHeartbeatSentMs = 0;
        _lastDisconnectReason = string.Empty;
        _lastDisconnectMs = 0;
    }

    public void MarkSessionSeen(int sessionId)
    {
        _activeSessionId = sessionId;
    }

    public void Update()
    {
        if (_debuggerPlugin == null)
            return;

        _debuggerPlugin.CleanupInactiveSessions();
        var sessionId = _debuggerPlugin.GetActiveSessionId();

        if (!sessionId.HasValue)
        {
            if (_runtimeConnected)
                MarkDisconnected("Runtime debugger session ended.");
            _activeSessionId = null;
            return;
        }

        _activeSessionId = sessionId;

        var now = Time.GetTicksMsec();
        if (now - _lastHeartbeatSentMs >= HeartbeatIntervalMs)
        {
            _lastHeartbeatSentMs = now;
            _debuggerPlugin.SendMessageToSession(sessionId.Value, RuntimeBridgeProtocol.MessagePing, new GDArray());
        }

        if (_runtimeConnected && _lastPongMs > 0 && now - _lastPongMs > HeartbeatTimeoutMs)
            MarkDisconnected("Runtime heartbeat timed out.");
    }

    public Dictionary GetStatus()
    {
        var status = new Dictionary
        {
            { "is_game_running", EditorInterface.Singleton.IsPlayingScene() },
            { "runtime_connected", _runtimeConnected },
            { "active_session_id", _activeSessionId ?? -1 },
            { "last_ready_ms", (long)_lastReadyMs },
            { "last_pong_ms", (long)_lastPongMs },
            { "pending_requests", _pendingRequests.Count },
            { "recent_log_count", _recentLogs.Count },
            { "session_state", GetSessionState() },
            { "last_disconnect_reason", _lastDisconnectReason },
            { "last_disconnect_ms", (long)_lastDisconnectMs },
        };

        var unavailableReason = GetRuntimeUnavailableReason();
        if (!string.IsNullOrWhiteSpace(unavailableReason))
            status["unavailable_reason"] = unavailableReason;

        foreach (var key in _lastStatus.Keys)
            status[key] = _lastStatus[key];

        return status;
    }

    public string GetRuntimeUnavailableReason()
    {
        if (_debuggerPlugin == null)
            return "Runtime debugger plugin is not available. The Godot MCP editor plugin may still be reloading; retry in a moment, and reload the project if this keeps happening.";

        if (!EditorInterface.Singleton.IsPlayingScene())
            return "No game is currently running. Start a scene first with scene_play.";

        if (!string.IsNullOrWhiteSpace(_lastDisconnectReason) && !_activeSessionId.HasValue)
            return $"{_lastDisconnectReason} Run the scene again with scene_play to start a new runtime session.";

        if (_activeSessionId.HasValue && !_runtimeConnected)
            return $"Runtime debugger session {_activeSessionId.Value} is attached, but the runtime bridge has not finished connecting yet. Wait a moment and retry.";

        if (_lastReadyMs > 0 && !_runtimeConnected)
            return "The previous runtime bridge session has ended. Run the scene again with scene_play to start a new runtime session.";

        if (!_activeSessionId.HasValue)
            return "No active runtime debugger session is available. Start the game from the editor first. If the running project is throwing startup exceptions, fix those first so the runtime bridge can attach.";

        return string.Empty;
    }

    public GDArray GetRecentLogs(int count, string? level = null)
    {
        var result = new GDArray();
        var startIndex = Math.Max(0, _recentLogs.Count - count);
        for (int i = startIndex; i < _recentLogs.Count; i++)
        {
            var entry = _recentLogs[i].AsGodotDictionary();
            if (!string.IsNullOrWhiteSpace(level) &&
                entry.TryGetValue("level", out var entryLevel) &&
                !string.Equals(entryLevel.AsString(), level, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }

    public async Task<Dictionary> RequestAsync(string action, Dictionary payload, int timeoutMs)
    {
        if (_debuggerPlugin == null)
            throw new InvalidOperationException(GetRuntimeUnavailableReason());

        var sessionId = _debuggerPlugin.GetActiveSessionId();
        if (!sessionId.HasValue)
            throw new InvalidOperationException(GetRuntimeUnavailableReason());

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<Dictionary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(timeoutMs);
        timeoutSource.Token.Register(() =>
        {
            if (_pendingRequests.Remove(requestId, out var pendingRequest))
            {
                pendingRequest.Completion.TrySetException(new TimeoutException($"Runtime request '{action}' timed out after {timeoutMs}ms."));
                pendingRequest.TimeoutSource.Dispose();
            }
        });

        _pendingRequests[requestId] = new PendingRuntimeRequest(completion, timeoutSource);

        var messagePayload = new GDArray { requestId, payload };
        if (!_debuggerPlugin.SendMessageToSession(sessionId.Value, action, messagePayload))
        {
            _pendingRequests.Remove(requestId);
            timeoutSource.Dispose();
            throw new InvalidOperationException("Failed to send runtime request to the active debugger session.");
        }

        return await completion.Task;
    }

    public async Task<Dictionary> WaitUntilReadyAsync(int timeoutMs, int pollIntervalMs)
    {
        var startedAt = Time.GetTicksMsec();
        var timeoutAt = startedAt + (ulong)Math.Max(1, timeoutMs);

        while (Time.GetTicksMsec() <= timeoutAt)
        {
            if (_debuggerPlugin == null)
                throw new InvalidOperationException(GetRuntimeUnavailableReason());

            var status = GetStatus();
            if (_runtimeConnected && _activeSessionId.HasValue)
            {
                return new Dictionary
                {
                    { "ready", true },
                    { "waited_ms", (long)(Time.GetTicksMsec() - startedAt) },
                    { "status", status },
                };
            }

            if (!EditorInterface.Singleton.IsPlayingScene())
                throw new InvalidOperationException(GetRuntimeUnavailableReason());

            await Task.Delay(Math.Max(10, pollIntervalMs));
        }

        throw new TimeoutException($"{GetRuntimeUnavailableReason()} Waited {timeoutMs}ms for runtime bridge readiness.");
    }

    public void HandleCapture(int sessionId, string action, GDArray data)
    {
        _activeSessionId = sessionId;
        var now = Time.GetTicksMsec();

        switch (action)
        {
            case RuntimeBridgeProtocol.MessageReady:
                _runtimeConnected = true;
                _lastReadyMs = now;
                _lastPongMs = now;
                _lastDisconnectReason = string.Empty;
                _lastDisconnectMs = 0;
                MergeStatus(ExtractPayload(data));
                AddLog("Runtime bridge connected.", "info", "runtime");
                break;
            case RuntimeBridgeProtocol.MessagePong:
                _runtimeConnected = true;
                _lastPongMs = now;
                _lastDisconnectReason = string.Empty;
                _lastDisconnectMs = 0;
                MergeStatus(ExtractPayload(data));
                break;
            case RuntimeBridgeProtocol.MessageResponse:
                HandleResponse(ExtractPayload(data));
                break;
            case RuntimeBridgeProtocol.MessageLog:
                AddLogFromPayload(ExtractPayload(data));
                break;
            case RuntimeBridgeProtocol.MessageSceneChanged:
                MergeStatus(ExtractPayload(data));
                AddLog("Runtime scene changed.", "info", "runtime");
                break;
            default:
                AddLog($"Received unknown runtime bridge message: {action}", "warning", "runtime");
                break;
        }
    }

    private void HandleResponse(Dictionary payload)
    {
        var requestId = payload.TryGetValue("request_id", out var requestIdVariant)
            ? requestIdVariant.AsString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(requestId) || !_pendingRequests.Remove(requestId, out var pendingRequest))
            return;

        pendingRequest.TimeoutSource.Dispose();

        var success = payload.TryGetValue("success", out var successVariant) && successVariant.AsBool();
        if (success)
        {
            var responseData = payload.TryGetValue("data", out var dataVariant)
                ? dataVariant.AsGodotDictionary()
                : new Dictionary();
            pendingRequest.Completion.TrySetResult(responseData);
            return;
        }

        var errorMessage = payload.TryGetValue("error", out var errorVariant)
            ? errorVariant.AsString()
            : "Unknown runtime bridge error.";
        pendingRequest.Completion.TrySetException(new InvalidOperationException(errorMessage));
    }

    private void MergeStatus(Dictionary payload)
    {
        if (payload.Count == 0)
            return;

        _lastStatus = payload;
    }

    private static Dictionary ExtractPayload(GDArray data)
    {
        if (data.Count > 0 && data[0].VariantType == Variant.Type.Dictionary)
            return data[0].AsGodotDictionary();

        return new Dictionary();
    }

    private void AddLogFromPayload(Dictionary payload)
    {
        var message = payload.TryGetValue("message", out var messageVariant)
            ? messageVariant.AsString()
            : "Runtime bridge event";
        var level = payload.TryGetValue("level", out var levelVariant)
            ? levelVariant.AsString()
            : "info";
        var source = payload.TryGetValue("source", out var sourceVariant)
            ? sourceVariant.AsString()
            : "runtime";

        var entry = new Dictionary
        {
            { "message", message },
            { "level", level },
            { "source", source },
            { "timestamp_ms", payload.TryGetValue("timestamp_ms", out var timestampVariant) ? timestampVariant : (long)Time.GetTicksMsec() },
        };

        AddLog(entry);
    }

    private void AddLog(string message, string level, string source)
    {
        AddLog(new Dictionary
        {
            { "message", message },
            { "level", level },
            { "source", source },
            { "timestamp_ms", (long)Time.GetTicksMsec() },
        });
    }

    private void AddLog(Dictionary entry)
    {
        _recentLogs.Add(entry);
        while (_recentLogs.Count > MaxLogEntries)
            _recentLogs.RemoveAt(0);

        var message = entry.TryGetValue("message", out var messageVariant) ? messageVariant.AsString() : "Runtime bridge event";
        var level = entry.TryGetValue("level", out var levelVariant) ? levelVariant.AsString() : "info";
        var source = entry.TryGetValue("source", out var sourceVariant) ? sourceVariant.AsString() : "runtime";
        EditorHandler.RecordLog(message, level, source);
    }

    private void MarkDisconnected(string reason)
    {
        _runtimeConnected = false;
        _activeSessionId = null;
        _lastStatus = new Dictionary();
        _lastDisconnectReason = reason;
        _lastDisconnectMs = Time.GetTicksMsec();
        RejectAllPendingRequests(reason);
        AddLog(reason, "warning", "runtime");
    }

    private string GetSessionState()
    {
        if (_debuggerPlugin == null)
            return "plugin_unavailable";

        if (_runtimeConnected && _activeSessionId.HasValue)
            return "connected";

        if (!EditorInterface.Singleton.IsPlayingScene())
            return "game_not_running";

        if (!string.IsNullOrWhiteSpace(_lastDisconnectReason) && !_activeSessionId.HasValue)
            return "session_ended";

        if (_activeSessionId.HasValue)
            return "waiting_for_runtime_ready";

        return "waiting_for_debugger_session";
    }

    private void RejectAllPendingRequests(string reason)
    {
        foreach (var pendingRequest in _pendingRequests.Values)
        {
            pendingRequest.TimeoutSource.Dispose();
            pendingRequest.Completion.TrySetException(new InvalidOperationException(reason));
        }

        _pendingRequests.Clear();
    }
}
#endif
