#if TOOLS
using System;
using Godot;
using GodotMCP.Handlers;
using System.Linq;
using GDArray = Godot.Collections.Array;

namespace GodotMCP;

[Tool]
public partial class RuntimeBridgeDebuggerPlugin : EditorDebuggerPlugin
{
    private readonly System.Collections.Generic.Dictionary<int, EditorDebuggerSession> _sessions = new();
    private readonly System.Collections.Generic.HashSet<int> _startedSessionIds = new();

    public override bool _HasCapture(string capture)
    {
        if (string.IsNullOrWhiteSpace(capture))
            return false;

        return capture == RuntimeBridgeProtocol.MessageHeader ||
               capture.StartsWith($"{RuntimeBridgeProtocol.MessageHeader}:", StringComparison.Ordinal);
    }

    public override void _SetupSession(int sessionId)
    {
        var session = GetSession(sessionId);
        _sessions[sessionId] = session;
        RuntimeBridgeService.Instance.AttachDebugger(this);
        session.Started += () =>
        {
            _sessions[sessionId] = session;
            _startedSessionIds.Add(sessionId);
            RuntimeBridgeService.Instance.MarkSessionSeen(sessionId);
            EditorHandler.RecordLog($"Runtime debugger session {sessionId} started.", "info", nameof(RuntimeBridgeDebuggerPlugin));
        };
        session.Stopped += () =>
        {
            _startedSessionIds.Remove(sessionId);
            _sessions.Remove(sessionId);
            EditorHandler.RecordLog($"Runtime debugger session {sessionId} stopped.", "info", nameof(RuntimeBridgeDebuggerPlugin));
        };
        EditorHandler.RecordLog($"Runtime debugger session {sessionId} attached.", "info", nameof(RuntimeBridgeDebuggerPlugin));
    }

    public override bool _Capture(string message, GDArray data, int sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || !IsInstanceValid(session))
        {
            session = GetSession(sessionId);
            _sessions[sessionId] = session;
        }

        _startedSessionIds.Add(sessionId);
        RuntimeBridgeService.Instance.MarkSessionSeen(sessionId);
        RuntimeBridgeService.Instance.HandleCapture(sessionId, RuntimeBridgeProtocol.TrimHeader(message), data);
        return true;
    }

    public int CleanupInactiveSessions()
    {
        var inactiveSessionIds = _sessions
            .Where(pair => !IsTrackedSessionUsable(pair.Key, pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var sessionId in inactiveSessionIds)
        {
            _startedSessionIds.Remove(sessionId);
            _sessions.Remove(sessionId);
        }

        return inactiveSessionIds.Length;
    }

    public int? GetActiveSessionId()
    {
        CleanupInactiveSessions();

        foreach (var pair in _sessions.OrderByDescending(pair => pair.Key))
        {
            if (_startedSessionIds.Contains(pair.Key) || pair.Value.IsActive())
                return pair.Key;
        }

        return null;
    }

    public bool SendMessageToSession(int sessionId, string action, GDArray? payload = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || !IsTrackedSessionUsable(sessionId, session))
        {
            _startedSessionIds.Remove(sessionId);
            _sessions.Remove(sessionId);
            return false;
        }

        session.SendMessage(RuntimeBridgeProtocol.BuildMessage(action), payload ?? new GDArray());
        return true;
    }

    private bool IsTrackedSessionUsable(int sessionId, EditorDebuggerSession session)
    {
        if (!IsInstanceValid(session))
            return false;

        if (!_startedSessionIds.Contains(sessionId))
            return true;

        return session.IsActive();
    }
}
#endif
