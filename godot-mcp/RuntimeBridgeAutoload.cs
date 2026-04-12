using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GDArray = Godot.Collections.Array;

namespace GodotMCP;

public partial class RuntimeBridgeAutoload : Node
{
    private readonly GDArray _recentLogs = new();
    private readonly System.Collections.Generic.Dictionary<string, GDArray> _storedMacros = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxLogEntries = 250;
    private double _scenePollAccumulator;
    private string _lastSceneSignature = string.Empty;
    private bool _editorDebuggerConfirmed;
    private bool _readyNotificationSent;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);

#if TOOLS
        EngineDebugger.RegisterMessageCapture(RuntimeBridgeProtocol.MessageHeader, Callable.From((string message, GDArray data) =>
        {
            _ = HandleDebuggerMessageAsync(message, data);
            return true;
        }));
#endif

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        PushLog("Runtime bridge autoload ready.", "info", "runtime");
        _lastSceneSignature = GetSceneSignature();
    }

    public override void _ExitTree()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

#if TOOLS
        EngineDebugger.UnregisterMessageCapture(RuntimeBridgeProtocol.MessageHeader);
#endif
    }

    public override void _Process(double delta)
    {
        _scenePollAccumulator += delta;
        if (_scenePollAccumulator < 0.5)
            return;

        _scenePollAccumulator = 0;
        var signature = GetSceneSignature();
        if (signature == _lastSceneSignature)
            return;

        _lastSceneSignature = signature;
        PushLog("Runtime scene signature changed.", "info", "runtime");
        SendNotification(RuntimeBridgeProtocol.MessageSceneChanged, BuildStatus());
    }

    private async Task HandleDebuggerMessageAsync(string action, GDArray data)
    {
        try
        {
            ConfirmEditorDebuggerConnection();

            switch (action)
            {
                case RuntimeBridgeProtocol.MessagePing:
                    SendNotification(RuntimeBridgeProtocol.MessagePong, BuildStatus());
                    return;
                case RuntimeBridgeProtocol.CommandGetStatus:
                    await SendResponseAsync(data, () => BuildStatus());
                    return;
                case RuntimeBridgeProtocol.CommandGetSceneTree:
                    await SendResponseAsync(data, () => GetSceneTree(data));
                    return;
                case RuntimeBridgeProtocol.CommandGetNodeProperties:
                    await SendResponseAsync(data, () => GetNodeProperties(data));
                    return;
                case RuntimeBridgeProtocol.CommandCaptureFrame:
                    await SendResponseAsync(data, CaptureFrame);
                    return;
                case RuntimeBridgeProtocol.CommandCaptureScreenshot:
                    await SendResponseAsync(data, CaptureScreenshotAsync);
                    return;
                case RuntimeBridgeProtocol.CommandMonitorProperty:
                    await SendResponseAsync(data, () => MonitorPropertyAsync(data));
                    return;
                case RuntimeBridgeProtocol.CommandGetLogs:
                    await SendResponseAsync(data, () => GetLogs(data));
                    return;
                case RuntimeBridgeProtocol.CommandWatchSignal:
                    await SendResponseAsync(data, () => WatchSignalAsync(data));
                    return;
                case RuntimeBridgeProtocol.CommandWatchNodeLifecycle:
                    await SendResponseAsync(data, () => WatchNodeLifecycleAsync(data));
                    return;
                case RuntimeBridgeProtocol.CommandEvaluateExpression:
                    await SendResponseAsync(data, () => EvaluateExpression(data));
                    return;
                case RuntimeBridgeProtocol.CommandInputKey:
                    await SendResponseAsync(data, () => ExecuteInputKeyAsync(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandInputMouse:
                    await SendResponseAsync(data, () => ExecuteInputMouseAsync(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandInputAction:
                    await SendResponseAsync(data, () => ExecuteInputActionAsync(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandInputText:
                    await SendResponseAsync(data, () => ExecuteInputTextAsync(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandInputSequence:
                    await SendResponseAsync(data, () => ExecuteInputSequenceAsync(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandRecordMacro:
                    await SendResponseAsync(data, () => RecordMacro(ExtractPayload(data)));
                    return;
                case RuntimeBridgeProtocol.CommandPlaybackMacro:
                    await SendResponseAsync(data, () => PlaybackMacroAsync(ExtractPayload(data)));
                    return;
                default:
                    SendErrorResponse(data, $"Unknown runtime bridge command: {action}");
                    return;
            }
        }
        catch (Exception ex)
        {
            SendErrorResponse(data, ex.Message);
            PushLog($"Runtime bridge command failed: {ex.Message}", "error", "runtime");
        }
    }

    private async Task SendResponseAsync(GDArray data, Func<Dictionary> action)
    {
        var requestId = ExtractRequestId(data);
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var responseData = action();
        SendResponse(requestId, true, responseData, string.Empty);
        await Task.CompletedTask;
    }

    private async Task SendResponseAsync(GDArray data, Func<Task<Dictionary>> action)
    {
        var requestId = ExtractRequestId(data);
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var responseData = await action();
        SendResponse(requestId, true, responseData, string.Empty);
    }

    private void SendErrorResponse(GDArray data, string error)
    {
        var requestId = ExtractRequestId(data);
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        SendResponse(requestId, false, new Dictionary(), error);
    }

    private void SendResponse(string requestId, bool success, Dictionary responseData, string error)
    {
#if TOOLS
        var safeResponseData = SanitizeDictionaryForTransport(responseData);
        EngineDebugger.SendMessage(RuntimeBridgeProtocol.BuildMessage(RuntimeBridgeProtocol.MessageResponse), new GDArray
        {
            new Dictionary
            {
                { "request_id", requestId },
                { "success", success },
                { "data", safeResponseData },
                { "error", error }
            }
        });
#endif
    }

    private void SendNotification(string action, Dictionary payload)
    {
#if TOOLS
        if (!CanPublishDebuggerNotifications())
            return;

        EngineDebugger.SendMessage(RuntimeBridgeProtocol.BuildMessage(action), new GDArray { SanitizeDictionaryForTransport(payload) });
#endif
    }

    private static Dictionary SanitizeDictionaryForTransport(Dictionary payload)
    {
        var serializedPayload = BridgeSerialization.SerializeVariant(payload);
        return serializedPayload.VariantType == Variant.Type.Dictionary
            ? serializedPayload.AsGodotDictionary()
            : new Dictionary();
    }

    private void ConfirmEditorDebuggerConnection()
    {
#if TOOLS
        _editorDebuggerConfirmed = true;
        if (_readyNotificationSent)
            return;

        _readyNotificationSent = true;
        SendNotification(RuntimeBridgeProtocol.MessageReady, BuildStatus());
#endif
    }

    private bool CanPublishDebuggerNotifications()
    {
#if TOOLS
        return _editorDebuggerConfirmed && EngineDebugger.IsActive();
#else
        return false;
#endif
    }

    private Dictionary BuildStatus()
    {
        var currentScene = GetRuntimeSceneRoot();
        return new Dictionary
        {
            { "runtime_connected", true },
            { "current_scene_name", currentScene?.Name.ToString() ?? string.Empty },
            { "current_scene_path", currentScene?.SceneFilePath ?? string.Empty },
            { "current_scene_node_path", currentScene?.GetPath().ToString() ?? string.Empty },
            { "stored_macro_count", _storedMacros.Count },
            { "timestamp_ms", (long)Time.GetTicksMsec() },
        };
    }

    private Dictionary GetSceneTree(GDArray data)
    {
        var payload = ExtractPayload(data);
        var depth = Math.Clamp(payload.TryGetValue("depth", out var depthVariant) ? depthVariant.AsInt32() : 10, 1, 20);
        var includeInternal = payload.TryGetValue("include_internal", out var includeInternalVariant) && includeInternalVariant.AsBool();
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant) ? nodePathVariant.AsString() : string.Empty;
        var flatten = payload.TryGetValue("flatten", out var flattenVariant) && flattenVariant.AsBool();
        var offset = Math.Max(0, payload.TryGetValue("offset", out var offsetVariant) ? offsetVariant.AsInt32() : 0);
        var limit = Math.Clamp(payload.TryGetValue("limit", out var limitVariant) ? limitVariant.AsInt32() : 200, 1, 1000);

        var targetNode = string.IsNullOrWhiteSpace(nodePath)
            ? GetRuntimeSceneRoot()
            : GetNodeOrNull(nodePath);

        if (targetNode == null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(nodePath)
                ? "No runtime scene root is available."
                : $"Runtime node not found: {nodePath}");

        var flattenedNodes = new GDArray();
        CollectTreeNodes(targetNode, depth, 0, includeInternal, flattenedNodes);
        var pagedNodes = SliceArray(flattenedNodes, offset, limit);
        var hasMore = offset + pagedNodes.Count < flattenedNodes.Count;

        var result = new Dictionary
        {
            { "is_game_running", true },
            { "runtime_connected", true },
            { "depth", depth },
            { "flatten", flatten },
            { "offset", offset },
            { "limit", limit },
            { "has_more", hasMore },
            { "total_nodes", flattenedNodes.Count },
            { "returned_nodes", pagedNodes.Count },
            { "node_path", targetNode.GetPath().ToString() },
            { "nodes", pagedNodes },
            { "current_scene", BuildNodeSummary(GetRuntimeSceneRoot()) },
        };

        if (hasMore)
            result["next_offset"] = offset + pagedNodes.Count;
        if (!flatten)
            result["tree"] = BuildTree(targetNode, depth, 0, includeInternal);

        return result;
    }

    private Dictionary BuildTree(Node node, int maxDepth, int currentDepth, bool includeInternal)
    {
        var result = BuildNodeSummary(node);
        if (currentDepth >= maxDepth)
            return result;

        var children = new GDArray();
        for (int i = 0; i < node.GetChildCount(includeInternal); i++)
        {
            var child = node.GetChild(i, includeInternal);
            if (!includeInternal && child.Name.ToString().StartsWith("@"))
                continue;

            children.Add(BuildTree(child, maxDepth, currentDepth + 1, includeInternal));
        }

        result["children"] = children;
        return result;
    }

    private void CollectTreeNodes(Node node, int maxDepth, int currentDepth, bool includeInternal, GDArray target)
    {
        target.Add(BuildNodeSummary(node));
        if (currentDepth >= maxDepth)
            return;

        for (int i = 0; i < node.GetChildCount(includeInternal); i++)
        {
            var child = node.GetChild(i, includeInternal);
            if (!includeInternal && child.Name.ToString().StartsWith("@", StringComparison.Ordinal))
                continue;

            CollectTreeNodes(child, maxDepth, currentDepth + 1, includeInternal, target);
        }
    }

    private Dictionary GetNodeProperties(GDArray data)
    {
        var payload = ExtractPayload(data);
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant)
            ? nodePathVariant.AsString()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(nodePath))
            throw new InvalidOperationException("node_path is required.");

        var node = GetNodeOrNull(nodePath);
        if (node == null)
            throw new InvalidOperationException($"Runtime node not found: {nodePath}");

        var requestedProperties = payload.TryGetValue("property_names", out var propertyNamesVariant) && propertyNamesVariant.VariantType == Variant.Type.Array
            ? propertyNamesVariant.AsGodotArray()
            : null;
        var offset = Math.Max(0, payload.TryGetValue("offset", out var offsetVariant) ? offsetVariant.AsInt32() : 0);
        var limit = Math.Clamp(payload.TryGetValue("limit", out var limitVariant) ? limitVariant.AsInt32() : 200, 1, 500);

        var properties = new Dictionary();
        var propertyOrder = new GDArray();
        int totalProperties = 0;
        int returnedProperties = 0;
        foreach (var property in node.GetPropertyList())
        {
            var propertyDict = property;
            var propertyName = propertyDict["name"].AsString();

            if (requestedProperties != null && requestedProperties.Count > 0)
            {
                bool isRequested = false;
                foreach (var requestedProperty in requestedProperties)
                {
                    if (string.Equals(requestedProperty.AsString(), propertyName, StringComparison.Ordinal))
                    {
                        isRequested = true;
                        break;
                    }
                }

                if (!isRequested)
                    continue;
            }

            totalProperties++;
            if (totalProperties <= offset)
                continue;
            if (returnedProperties >= limit)
                break;

            try
            {
                var value = node.Get(propertyName);
                properties[propertyName] = BridgeSerialization.BuildPropertySnapshot(propertyName, value, propertyDict);
            }
            catch (Exception ex)
            {
                properties[propertyName] = new Dictionary
                {
                    { "name", propertyName },
                    { "type", "error" },
                    { "display_value", $"<unreadable: {ex.Message}>" },
                    { "raw_value", default(Variant) },
                };
            }

            propertyOrder.Add(propertyName);
            returnedProperties++;
        }

        var hasMore = offset + returnedProperties < totalProperties;

        return new Dictionary
        {
            { "node_path", nodePath },
            { "type", node.GetClass() },
            { "properties", properties },
            { "property_order", propertyOrder },
            { "total_properties", totalProperties },
            { "returned_properties", returnedProperties },
            { "offset", offset },
            { "limit", limit },
            { "has_more", hasMore },
            { "next_offset", hasMore ? offset + returnedProperties : -1 },
        };
    }

    private Dictionary CaptureFrame()
    {
        var fps = Engine.GetFramesPerSecond();
        var frameTime = fps <= 0 ? 0.0 : Math.Round(1000.0 / fps, 2);
        return new Dictionary
        {
            { "fps", fps },
            { "frame_time_ms", frameTime },
            { "object_count", Performance.GetMonitor(Performance.Monitor.ObjectCount) },
            { "node_count", Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) },
            { "orphan_nodes", Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) },
            { "render_objects_in_frame", Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame) },
            { "render_draw_calls", Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame) },
            { "video_memory_bytes", Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) },
            { "physics_2d_active_objects", Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects) },
            { "static_memory_bytes", Performance.GetMonitor(Performance.Monitor.MemoryStatic) },
            { "timestamp_ms", (long)Time.GetTicksMsec() },
        };
    }

    private async Task<Dictionary> MonitorPropertyAsync(GDArray data)
    {
        var payload = ExtractPayload(data);
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant)
            ? nodePathVariant.AsString()
            : string.Empty;
        var property = payload.TryGetValue("property", out var propertyVariant)
            ? propertyVariant.AsString()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(nodePath) || string.IsNullOrWhiteSpace(property))
            throw new InvalidOperationException("node_path and property are required.");

        var durationMs = Math.Max(0, payload.TryGetValue("duration", out var durationVariant) ? durationVariant.AsInt32() : 1000);
        var intervalMs = Math.Clamp(payload.TryGetValue("interval_ms", out var intervalVariant) ? intervalVariant.AsInt32() : 100, 16, 1000);
        var maxSamples = Math.Clamp(payload.TryGetValue("max_samples", out var maxSamplesVariant) ? maxSamplesVariant.AsInt32() : 128, 1, 512);

        var samples = new GDArray();
        var startedAt = Time.GetTicksMsec();
        ulong endedAt = startedAt + (ulong)durationMs;

        while (samples.Count < maxSamples)
        {
            var node = GetNodeOrNull(nodePath);
            if (node == null)
                throw new InvalidOperationException($"Runtime node not found during monitoring: {nodePath}");

            Variant value;
            try
            {
                value = node.Get(property);
            }
            catch (Exception ex)
            {
                value = $"<unreadable: {ex.Message}>";
            }

            samples.Add(new Dictionary
            {
                { "timestamp_ms", (long)Time.GetTicksMsec() },
                { "value", BridgeSerialization.SerializeVariant(value) },
                { "type", BridgeSerialization.GetVariantTypeName(value.VariantType) },
                { "display_value", value.ToString() },
            });

            if (Time.GetTicksMsec() >= endedAt)
                break;

            await ToSignal(GetTree().CreateTimer(intervalMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
        }

        return new Dictionary
        {
            { "node_path", nodePath },
            { "property", property },
            { "duration_ms", durationMs },
            { "interval_ms", intervalMs },
            { "sample_count", samples.Count },
            { "samples", samples },
            { "truncated", samples.Count >= maxSamples && Time.GetTicksMsec() < endedAt },
        };
    }

    private async Task<Dictionary> CaptureScreenshotAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var viewport = GetViewport();
        var image = RenderingServer.Texture2DGet(viewport.GetTexture().GetRid());
        if (image == null || image.IsEmpty())
            throw new InvalidOperationException("Failed to capture runtime viewport.");

        var savePath = ProjectSettings.GlobalizePath($"user://mcp_game_screenshot_{Time.GetTicksMsec()}.png");
        var error = image.SavePng(savePath);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Failed to save runtime screenshot: {error}");

        return new Dictionary
        {
            { "path", savePath },
            { "format", "png" },
            { "width", image.GetWidth() },
            { "height", image.GetHeight() },
        };
    }

    private Dictionary EvaluateExpression(GDArray data)
    {
        var payload = ExtractPayload(data);
        var expressionText = payload.TryGetValue("expression", out var expressionVariant)
            ? expressionVariant.AsString()
            : string.Empty;
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant)
            ? nodePathVariant.AsString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(expressionText))
            throw new InvalidOperationException("expression is required.");

        Node? baseNode = null;
        if (!string.IsNullOrWhiteSpace(nodePath))
        {
            baseNode = GetNodeOrNull(nodePath);
            if (baseNode == null)
                throw new InvalidOperationException($"Runtime node not found: {nodePath}");
        }

        var expression = new Expression();
        var error = expression.Parse(expressionText);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Parse error: {expression.GetErrorText()}");

        var result = expression.Execute(new GDArray(), baseNode, true, true);
        if (expression.HasExecuteFailed())
            throw new InvalidOperationException($"Execution error: {expression.GetErrorText()}");

        return new Dictionary
        {
            { "expression", expressionText },
            { "node_path", nodePath },
            { "readonly", true },
            { "result", new Dictionary
                {
                    { "type", BridgeSerialization.GetVariantTypeName(result.VariantType) },
                    { "display_value", result.ToString() },
                    { "raw_value", BridgeSerialization.SerializeVariant(result) },
                }
            },
        };
    }

    private async Task<Dictionary> WatchSignalAsync(GDArray data)
    {
        var payload = ExtractPayload(data);
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant) ? nodePathVariant.AsString() : string.Empty;
        var signalName = payload.TryGetValue("signal", out var signalVariant) ? signalVariant.AsString() : string.Empty;
        var durationMs = Math.Max(0, payload.TryGetValue("duration", out var durationVariant) ? durationVariant.AsInt32() : 1000);
        var maxEvents = Math.Clamp(payload.TryGetValue("max_events", out var maxEventsVariant) ? maxEventsVariant.AsInt32() : 128, 1, 512);

        if (string.IsNullOrWhiteSpace(nodePath) || string.IsNullOrWhiteSpace(signalName))
            throw new InvalidOperationException("node_path and signal are required.");

        var node = GetNodeOrNull(nodePath);
        if (node == null)
            throw new InvalidOperationException($"Runtime node not found: {nodePath}");
        if (!node.HasSignal(signalName))
            throw new InvalidOperationException($"Signal '{signalName}' does not exist on node {nodePath}.");

        var events = new GDArray();
        var emissionCount = 0;
        var callable = Callable.From(() =>
        {
            if (events.Count >= maxEvents)
                return;

            emissionCount++;
            events.Add(new Dictionary
            {
                { "index", emissionCount },
                { "timestamp_ms", (long)Time.GetTicksMsec() },
                { "signal", signalName },
            });
        });

        var connectError = node.Connect(signalName, callable, (uint)ConnectFlags.ReferenceCounted);
        if (connectError != Error.Ok && connectError != Error.AlreadyInUse)
            throw new InvalidOperationException($"Failed to connect signal watcher: {connectError}");

        try
        {
            await ToSignal(GetTree().CreateTimer(durationMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
        }
        finally
        {
            if (IsInstanceValid(node) && node.IsConnected(signalName, callable))
                node.Disconnect(signalName, callable);
        }

        return new Dictionary
        {
            { "node_path", nodePath },
            { "signal", signalName },
            { "duration_ms", durationMs },
            { "event_count", events.Count },
            { "truncated", events.Count >= maxEvents },
            { "events", events },
        };
    }

    private async Task<Dictionary> WatchNodeLifecycleAsync(GDArray data)
    {
        var payload = ExtractPayload(data);
        var nodePath = payload.TryGetValue("node_path", out var nodePathVariant) ? nodePathVariant.AsString() : string.Empty;
        var durationMs = Math.Max(0, payload.TryGetValue("duration", out var durationVariant) ? durationVariant.AsInt32() : 1000);
        var pollIntervalMs = Math.Clamp(payload.TryGetValue("poll_interval_ms", out var pollVariant) ? pollVariant.AsInt32() : 100, 16, 1000);
        var maxSamples = Math.Clamp(payload.TryGetValue("max_samples", out var maxSamplesVariant) ? maxSamplesVariant.AsInt32() : 256, 1, 1024);

        if (string.IsNullOrWhiteSpace(nodePath))
            throw new InvalidOperationException("node_path is required.");

        var samples = new GDArray();
        var transitions = new GDArray();
        bool? lastExists = null;
        bool? lastInsideTree = null;
        long lastInstanceId = -1;
        var startedAt = Time.GetTicksMsec();
        var endsAt = startedAt + (ulong)durationMs;

        while (samples.Count < maxSamples)
        {
            var node = GetNodeOrNull(nodePath);
            var exists = node != null;
            var insideTree = node?.IsInsideTree() ?? false;
            var instanceId = exists ? (long)node!.GetInstanceId() : -1;
            var currentPath = exists ? node!.GetPath().ToString() : string.Empty;

            var sample = new Dictionary
            {
                { "timestamp_ms", (long)Time.GetTicksMsec() },
                { "exists", exists },
                { "inside_tree", insideTree },
                { "instance_id", instanceId },
                { "current_path", currentPath },
            };
            samples.Add(sample);

            if (lastExists != exists || lastInsideTree != insideTree || lastInstanceId != instanceId)
            {
                transitions.Add(new Dictionary
                {
                    { "timestamp_ms", (long)Time.GetTicksMsec() },
                    { "exists", exists },
                    { "inside_tree", insideTree },
                    { "instance_id", instanceId },
                    { "current_path", currentPath },
                });
            }

            lastExists = exists;
            lastInsideTree = insideTree;
            lastInstanceId = instanceId;

            if (Time.GetTicksMsec() >= endsAt)
                break;

            await ToSignal(GetTree().CreateTimer(pollIntervalMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
        }

        return new Dictionary
        {
            { "node_path", nodePath },
            { "duration_ms", durationMs },
            { "poll_interval_ms", pollIntervalMs },
            { "sample_count", samples.Count },
            { "samples", samples },
            { "transitions", transitions },
            { "truncated", samples.Count >= maxSamples && Time.GetTicksMsec() < endsAt },
        };
    }

    private Dictionary RecordMacro(Dictionary payload)
    {
        var name = payload.TryGetValue("name", out var nameVariant) ? nameVariant.AsString() : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("name is required.");
        if (!payload.TryGetValue("steps", out var stepsVariant) || stepsVariant.VariantType != Variant.Type.Array)
            throw new InvalidOperationException("steps is required.");

        var sourceSteps = stepsVariant.AsGodotArray();
        var steps = new GDArray();
        foreach (var step in sourceSteps)
            steps.Add(step);

        _storedMacros[name] = steps;
        return new Dictionary
        {
            { "name", name },
            { "step_count", steps.Count },
            { "stored_macro_count", _storedMacros.Count },
        };
    }

    private async Task<Dictionary> PlaybackMacroAsync(Dictionary payload)
    {
        var name = payload.TryGetValue("name", out var nameVariant) ? nameVariant.AsString() : string.Empty;
        var loopCount = Math.Clamp(payload.TryGetValue("loop_count", out var loopVariant) ? loopVariant.AsInt32() : 1, 1, 32);
        if (!_storedMacros.TryGetValue(name, out var steps))
            throw new InvalidOperationException($"Recorded macro not found: {name}");

        for (int i = 0; i < loopCount; i++)
        {
            await ExecuteInputSequenceAsync(new Dictionary
            {
                { "steps", steps },
            });
        }

        return new Dictionary
        {
            { "name", name },
            { "loop_count", loopCount },
            { "step_count", steps.Count },
            { "executed_steps", steps.Count * loopCount },
        };
    }

    private async Task<Dictionary> ExecuteInputKeyAsync(Dictionary payload)
    {
        var keyName = payload.TryGetValue("key", out var keyVariant) ? keyVariant.AsString() : string.Empty;
        var pressed = payload.TryGetValue("pressed", out var pressedVariant) ? pressedVariant.AsBool() : true;
        var durationMs = payload.TryGetValue("duration", out var durationVariant) ? durationVariant.AsInt32() : 0;

        if (string.IsNullOrWhiteSpace(keyName))
            throw new InvalidOperationException("key is required.");

        var keycode = OS.FindKeycodeFromString(keyName);
        if (keycode == Key.None)
            throw new InvalidOperationException($"Unknown key: {keyName}");

        SendKeyEvent(keycode, pressed);

        if (pressed && durationMs > 0)
        {
            await ToSignal(GetTree().CreateTimer(durationMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
            SendKeyEvent(keycode, false);
        }

        return new Dictionary
        {
            { "key", keyName },
            { "pressed", pressed },
            { "duration_ms", durationMs },
        };
    }

    private async Task<Dictionary> ExecuteInputMouseAsync(Dictionary payload)
    {
        if (!payload.TryGetValue("position", out var positionVariant) || positionVariant.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("position is required.");

        var positionDict = positionVariant.AsGodotDictionary();
        var position = new Vector2(
            positionDict.TryGetValue("x", out var xVariant) ? (float)xVariant.AsDouble() : 0,
            positionDict.TryGetValue("y", out var yVariant) ? (float)yVariant.AsDouble() : 0
        );
        var button = payload.TryGetValue("button", out var buttonVariant) ? buttonVariant.AsString() : "left";
        var action = payload.TryGetValue("action", out var actionVariant) ? actionVariant.AsString() : "click";

        if (action.Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            var motion = new InputEventMouseMotion
            {
                Position = position,
                GlobalPosition = position,
                Relative = Vector2.Zero,
            };
            Input.ParseInputEvent(motion);
        }
        else
        {
            var buttonIndex = button switch
            {
                "right" => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => MouseButton.Left,
            };

            var motion = new InputEventMouseMotion
            {
                Position = position,
                GlobalPosition = position,
                Relative = Vector2.Zero,
            };
            Input.ParseInputEvent(motion);

            if (action is "press" or "click")
                SendMouseButtonEvent(position, buttonIndex, true);
            if (action is "release" or "click")
                SendMouseButtonEvent(position, buttonIndex, false);
        }

        await Task.CompletedTask;
        return new Dictionary
        {
            { "action", action },
            { "button", button },
            { "position", new Dictionary { { "x", position.X }, { "y", position.Y } } },
        };
    }

    private async Task<Dictionary> ExecuteInputActionAsync(Dictionary payload)
    {
        var actionName = payload.TryGetValue("action_name", out var actionVariant) ? actionVariant.AsString() : string.Empty;
        var pressed = payload.TryGetValue("pressed", out var pressedVariant) ? pressedVariant.AsBool() : true;
        var strength = payload.TryGetValue("strength", out var strengthVariant) ? (float)strengthVariant.AsDouble() : 1.0f;
        var durationMs = payload.TryGetValue("duration", out var durationVariant) ? durationVariant.AsInt32() : 0;

        if (string.IsNullOrWhiteSpace(actionName))
            throw new InvalidOperationException("action_name is required.");
        if (!InputMap.HasAction(actionName))
            throw new InvalidOperationException($"Unknown input action: {actionName}");

        if (pressed)
            Input.ActionPress(actionName, strength);
        else
            Input.ActionRelease(actionName);

        if (pressed && durationMs > 0)
        {
            await ToSignal(GetTree().CreateTimer(durationMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
            Input.ActionRelease(actionName);
        }

        return new Dictionary
        {
            { "action_name", actionName },
            { "pressed", pressed },
            { "strength", strength },
            { "duration_ms", durationMs },
        };
    }

    private async Task<Dictionary> ExecuteInputTextAsync(Dictionary payload)
    {
        var text = payload.TryGetValue("text", out var textVariant) ? textVariant.AsString() : string.Empty;
        foreach (var ch in text)
        {
            var keycode = OS.FindKeycodeFromString(ch.ToString());
            var pressEvent = new InputEventKey
            {
                Pressed = true,
                Keycode = keycode,
                PhysicalKeycode = keycode,
                Unicode = ch,
            };
            Input.ParseInputEvent(pressEvent);

            var releaseEvent = new InputEventKey
            {
                Pressed = false,
                Keycode = keycode,
                PhysicalKeycode = keycode,
                Unicode = ch,
            };
            Input.ParseInputEvent(releaseEvent);
        }

        await Task.CompletedTask;
        return new Dictionary
        {
            { "typed", text },
            { "length", text.Length },
        };
    }

    private async Task<Dictionary> ExecuteInputSequenceAsync(Dictionary payload)
    {
        if (!payload.TryGetValue("steps", out var stepsVariant) || stepsVariant.VariantType != Variant.Type.Array)
            throw new InvalidOperationException("steps is required.");

        var steps = stepsVariant.AsGodotArray();
        var totalDelay = 0;
        foreach (var stepVariant in steps)
        {
            if (stepVariant.VariantType != Variant.Type.Dictionary)
                continue;

            var step = stepVariant.AsGodotDictionary();
            var type = step.TryGetValue("type", out var typeVariant) ? typeVariant.AsString() : string.Empty;
            var stepParams = step.TryGetValue("params", out var paramsVariant) && paramsVariant.VariantType == Variant.Type.Dictionary
                ? paramsVariant.AsGodotDictionary()
                : new Dictionary();

            switch (type)
            {
                case "key":
                    await ExecuteInputKeyAsync(stepParams);
                    break;
                case "mouse":
                    await ExecuteInputMouseAsync(stepParams);
                    break;
                case "action":
                    await ExecuteInputActionAsync(stepParams);
                    break;
                case "text":
                    await ExecuteInputTextAsync(stepParams);
                    break;
                case "wait":
                    break;
                default:
                    throw new InvalidOperationException($"Unknown input sequence step: {type}");
            }

            var delayMs = step.TryGetValue("delay_ms", out var delayVariant) ? Math.Max(0, delayVariant.AsInt32()) : 0;
            totalDelay += delayMs;
            if (delayMs > 0)
                await ToSignal(GetTree().CreateTimer(delayMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
        }

        return new Dictionary
        {
            { "steps_queued", steps.Count },
            { "total_duration_ms", totalDelay },
        };
    }

    private static void SendKeyEvent(Key keycode, bool pressed, uint unicode = 0)
    {
        var inputEvent = new InputEventKey
        {
            Pressed = pressed,
            Keycode = keycode,
            PhysicalKeycode = keycode,
            Unicode = unicode,
        };
        Input.ParseInputEvent(inputEvent);
    }

    private static void SendMouseButtonEvent(Vector2 position, MouseButton button, bool pressed)
    {
        var inputEvent = new InputEventMouseButton
        {
            Position = position,
            GlobalPosition = position,
            ButtonIndex = button,
            Pressed = pressed,
        };
        Input.ParseInputEvent(inputEvent);
    }

    private Dictionary GetLogs(GDArray data)
    {
        var payload = ExtractPayload(data);
        var count = Math.Clamp(payload.TryGetValue("count", out var countVariant) ? countVariant.AsInt32() : 50, 1, MaxLogEntries);
        var level = payload.TryGetValue("level", out var levelVariant) ? levelVariant.AsString() : string.Empty;

        var logs = new GDArray();
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

            logs.Add(entry);
        }

        return new Dictionary
        {
            { "logs", logs },
            { "count", logs.Count },
        };
    }

    private void PushLog(string message, string level, string source)
    {
        var entry = new Dictionary
        {
            { "message", message },
            { "level", level },
            { "source", source },
            { "timestamp_ms", (long)Time.GetTicksMsec() },
        };

        _recentLogs.Add(entry);
        while (_recentLogs.Count > MaxLogEntries)
            _recentLogs.RemoveAt(0);

        SendNotification(RuntimeBridgeProtocol.MessageLog, entry);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        PushLog($"Unhandled runtime exception: {args.ExceptionObject}", "error", "runtime");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        PushLog($"Unobserved runtime task exception: {args.Exception}", "error", "runtime");
        args.SetObserved();
    }

    private static string ExtractRequestId(GDArray data)
    {
        if (data.Count == 0)
            return string.Empty;

        return data[0].AsString();
    }

    private static Dictionary ExtractPayload(GDArray data)
    {
        if (data.Count > 1 && data[1].VariantType == Variant.Type.Dictionary)
            return data[1].AsGodotDictionary();

        return new Dictionary();
    }

    private Node? GetRuntimeSceneRoot()
    {
        return GetTree().CurrentScene ?? GetTree().Root;
    }

    private static GDArray SliceArray(GDArray source, int offset, int limit)
    {
        var result = new GDArray();
        for (int i = offset; i < Math.Min(source.Count, offset + limit); i++)
            result.Add(source[i]);
        return result;
    }

    private string GetSceneSignature()
    {
        var root = GetRuntimeSceneRoot();
        return root == null
            ? string.Empty
            : $"{root.Name}|{root.SceneFilePath}|{root.GetChildCount()}|{root.GetPath()}";
    }

    private static Dictionary BuildNodeSummary(Node? node)
    {
        return BridgeSerialization.BuildNodeSummary(node, includeGroups: true);
    }
}
