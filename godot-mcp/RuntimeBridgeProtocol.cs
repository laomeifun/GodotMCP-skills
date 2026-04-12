using Godot;

namespace GodotMCP;

public static class RuntimeBridgeProtocol
{
    public const string MessageHeader = "godot_mcp_runtime";
    public const string AutoloadName = "GodotMCPRuntimeBridge";
    public const string AutoloadPath = "res://addons/godot-mcp/RuntimeBridgeAutoload.cs";

    public const string MessageReady = "ready";
    public const string MessagePing = "ping";
    public const string MessagePong = "pong";
    public const string MessageLog = "log";
    public const string MessageResponse = "response";
    public const string MessageSceneChanged = "scene_changed";

    public const string CommandGetStatus = "get_status";
    public const string CommandGetSceneTree = "get_scene_tree";
    public const string CommandGetNodeProperties = "get_node_properties";
    public const string CommandCaptureFrame = "capture_frame";
    public const string CommandCaptureScreenshot = "capture_screenshot";
    public const string CommandMonitorProperty = "monitor_property";
    public const string CommandGetLogs = "get_logs";
    public const string CommandWatchSignal = "watch_signal";
    public const string CommandWatchNodeLifecycle = "watch_node_lifecycle";
    public const string CommandEvaluateExpression = "evaluate_expression";
    public const string CommandInputKey = "input_key";
    public const string CommandInputMouse = "input_mouse";
    public const string CommandInputAction = "input_action";
    public const string CommandInputText = "input_text";
    public const string CommandInputSequence = "input_sequence";
    public const string CommandRecordMacro = "record_macro";
    public const string CommandPlaybackMacro = "playback_macro";

    public static string BuildMessage(string action) => $"{MessageHeader}:{action}";

    public static string TrimHeader(string message)
    {
        var separatorIndex = message.IndexOf(':');
        return separatorIndex >= 0 ? message[(separatorIndex + 1)..] : message;
    }
}
