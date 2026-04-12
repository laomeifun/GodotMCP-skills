#if TOOLS
using System;
using Godot;
using GodotMCP.Handlers;
using Error = Godot.Error;

namespace GodotMCP;

[Tool]
public partial class MCPPlugin : EditorPlugin
{
    private const int DefaultPort = 6550;
    private const ulong AutoloadRetryIntervalMs = 2000;
    private const ulong AutoloadBlockerLogIntervalMs = 30000;
    private WebSocketServer? _wsServer;
    private CommandRouter? _router;
    private RuntimeBridgeDebuggerPlugin? _runtimeDebuggerPlugin;
    private ulong _lastAutoloadInstallAttemptMs;
    private ulong _lastAutoloadBlockerLogMs;
    private string _lastAutoloadInstallBlocker = string.Empty;

    public override void _EnterTree()
    {
        SetMeta("MCPPlugin", this);
        EditorHandler.RecordLog("Godot MCP plugin is enabling.", "info", nameof(MCPPlugin));

        var port = DefaultPort;
        var envPort = System.Environment.GetEnvironmentVariable("GODOT_MCP_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var parsedPort))
            port = parsedPort;

        // 尝试获取已经存在的实例以防热重载时重复创建
        _wsServer = GetNodeOrNull<WebSocketServer>("MCPWebSocketServer");
        if (_wsServer == null)
        {
            _wsServer = new WebSocketServer();
            _wsServer.Name = "MCPWebSocketServer";
            AddChild(_wsServer);
            var err = _wsServer.StartServer(port);

            if (err != Godot.Error.Ok)
            {
                GD.PrintErr("[GodotMCP] ⚠ Plugin enabled but WebSocket server failed to start.");
                GD.PrintErr("[GodotMCP] MCP tools will not respond until the server is running.");
                EditorHandler.RecordLog("Plugin enabled but WebSocket server failed to start.", "error", nameof(MCPPlugin));
            }

            // 使用 Callable 连接可以防止 C# 热重载时事件被清理
            _wsServer.Connect(WebSocketServer.SignalName.ClientConnected, Callable.From<int>(OnClientConnected));
            _wsServer.Connect(WebSocketServer.SignalName.ClientDisconnected, Callable.From<int>(OnClientDisconnected));
            _wsServer.Connect(WebSocketServer.SignalName.MessageReceived, Callable.From<int, string>(OnMessageReceived));
        }

        EnsureInitialized();
        EnsureRuntimeBridgeInstalled();

        GD.Print($"[GodotMCP] Plugin enabled (port={port})");
        EditorHandler.RecordLog($"Plugin enabled (port={port})", "info", nameof(MCPPlugin));
    }

    private void EnsureInitialized()
    {
        if (_wsServer == null)
        {
            _wsServer = GetNodeOrNull<WebSocketServer>("MCPWebSocketServer");
        }

        if (_router == null)
        {
            _router = new CommandRouter();
            _router.RegisterHandler("project", new ProjectHandler(this));
            _router.RegisterHandler("scene", new SceneHandler(this));
            _router.RegisterHandler("node", new NodeHandler(this));
            _router.RegisterHandler("script", new ScriptHandler(this));
            _router.RegisterHandler("editor", new EditorHandler(this));
            _router.RegisterHandler("input", new InputHandler(this));
            _router.RegisterHandler("runtime", new RuntimeHandler(this));
        }
    }

    private void EnsureRuntimeBridgeInstalled()
    {
        if (_runtimeDebuggerPlugin == null || !IsInstanceValid(_runtimeDebuggerPlugin))
        {
            _runtimeDebuggerPlugin = new RuntimeBridgeDebuggerPlugin();
            AddDebuggerPlugin(_runtimeDebuggerPlugin);
            EditorHandler.RecordLog("Runtime debugger bridge installed.", "info", nameof(MCPPlugin));
        }

        RuntimeBridgeService.Instance.AttachDebugger(_runtimeDebuggerPlugin);

        if (ProjectSettings.HasSetting($"autoload/{RuntimeBridgeProtocol.AutoloadName}"))
        {
            _lastAutoloadInstallBlocker = string.Empty;
            return;
        }

        var now = Time.GetTicksMsec();
        if (now - _lastAutoloadInstallAttemptMs < AutoloadRetryIntervalMs)
            return;

        _lastAutoloadInstallAttemptMs = now;

        if (!CanInstallRuntimeBridgeAutoload(out var blocker))
        {
            LogAutoloadInstallBlocked(blocker);
            return;
        }

        AddAutoloadSingleton(RuntimeBridgeProtocol.AutoloadName, RuntimeBridgeProtocol.AutoloadPath);
        if (ProjectSettings.HasSetting($"autoload/{RuntimeBridgeProtocol.AutoloadName}"))
        {
            _lastAutoloadInstallBlocker = string.Empty;
            EditorHandler.RecordLog("Runtime bridge autoload installed.", "info", nameof(MCPPlugin));
            return;
        }

        LogAutoloadInstallBlocked($"Failed to register runtime bridge autoload at {RuntimeBridgeProtocol.AutoloadPath}. Build the C# project, make sure your .csproj includes addons/godot-mcp/**/*.cs, then reload the project.");
    }

    private bool CanInstallRuntimeBridgeAutoload(out string blocker)
    {
        if (!ResourceLoader.Exists(RuntimeBridgeProtocol.AutoloadPath))
        {
            blocker = $"Runtime bridge autoload script was not found at {RuntimeBridgeProtocol.AutoloadPath}.";
            return false;
        }

        var script = ResourceLoader.Load<Script>(RuntimeBridgeProtocol.AutoloadPath);
        if (script == null)
        {
            blocker = $"Runtime bridge autoload script could not be loaded from {RuntimeBridgeProtocol.AutoloadPath}.";
            return false;
        }

        if (!HasCSharpSolutionFiles())
        {
            blocker = $"Runtime bridge autoload script {RuntimeBridgeProtocol.AutoloadPath} is not instantiable because this project does not appear to have a C# solution yet. Create any C# script to generate the .csproj/.sln, build the project, then re-enable the plugin.";
            return false;
        }

        // NOTE:
        // For C# runtime-only scripts, querying Script.CanInstantiate() from the editor can
        // report false even when the project has compiled successfully and the autoload can be
        // registered for the running game. Using CanInstantiate() here caused false negatives
        // on real projects, so we only block on missing C# solution files or known compilation
        // errors and then let AddAutoloadSingleton() be the final source of truth.
        if (EditorHandler.TryGetRecentCompilationErrorSummary(out var diagnosticSummary))
        {
            blocker = $"Runtime bridge autoload script {RuntimeBridgeProtocol.AutoloadPath} is blocked by recent C# compilation errors: {diagnosticSummary}. Fix those errors, build the project again, then reload the project or wait for the plugin to retry automatically.";
            return false;
        }

        blocker = string.Empty;
        return true;
    }

    private static bool HasCSharpSolutionFiles()
    {
        var rootDir = DirAccess.Open("res://");
        if (rootDir == null)
            return false;

        foreach (var fileName in rootDir.GetFiles())
        {
            if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void LogAutoloadInstallBlocked(string blocker)
    {
        var now = Time.GetTicksMsec();
        if (string.Equals(blocker, _lastAutoloadInstallBlocker, StringComparison.Ordinal) &&
            now - _lastAutoloadBlockerLogMs < AutoloadBlockerLogIntervalMs)
        {
            return;
        }

        _lastAutoloadInstallBlocker = blocker;
        _lastAutoloadBlockerLogMs = now;
        GD.PrintErr($"[GodotMCP] {blocker}");
        EditorHandler.RecordLog(blocker, "warning", nameof(MCPPlugin));
    }

    public override void _ExitTree()
    {
        RuntimeBridgeService.Instance.Shutdown();

        if (_runtimeDebuggerPlugin != null)
        {
            RemoveDebuggerPlugin(_runtimeDebuggerPlugin);
            _runtimeDebuggerPlugin = null;
        }

        if (ProjectSettings.HasSetting($"autoload/{RuntimeBridgeProtocol.AutoloadName}"))
            RemoveAutoloadSingleton(RuntimeBridgeProtocol.AutoloadName);

        if (_wsServer != null && IsInstanceValid(_wsServer))
        {
            _wsServer.StopServer();
            _wsServer.QueueFree();
            _wsServer = null;
        }
        RemoveMeta("MCPPlugin");
        GD.Print("[GodotMCP] Plugin disabled");
        EditorHandler.RecordLog("Plugin disabled", "info", nameof(MCPPlugin));
    }

    public override void _Process(double delta)
    {
        EnsureInitialized();
        EnsureRuntimeBridgeInstalled();
        _wsServer?.Poll();
        RuntimeBridgeService.Instance.Update();
    }

    private void OnClientConnected(int clientId)
    {
        GD.Print($"[GodotMCP] Client {clientId} connected");
        EditorHandler.RecordLog($"Client {clientId} connected", "info", nameof(MCPPlugin));
    }

    private void OnClientDisconnected(int clientId)
    {
        GD.Print($"[GodotMCP] Client {clientId} disconnected");
        EditorHandler.RecordLog($"Client {clientId} disconnected", "info", nameof(MCPPlugin));
    }

    private async void OnMessageReceived(int clientId, string message)
    {
        EnsureInitialized();
        EnsureRuntimeBridgeInstalled();
        if (_router == null || _wsServer == null)
        {
            GD.PrintErr("[GodotMCP] Plugin not ready, ignoring message");
            EditorHandler.RecordLog("Plugin not ready, ignoring incoming message.", "warning", nameof(MCPPlugin));
            return;
        }

        try
        {
            var response = await _router.RouteAsync(message);
            if (_wsServer == null || !IsInstanceValid(_wsServer))
                return;

            var err = _wsServer.SendText(clientId, response);
            if (err != Godot.Error.Ok)
            {
                GD.PrintErr($"[GodotMCP] Failed to send response: {err}");
                EditorHandler.RecordLog($"Failed to send response to client {clientId}: {err}", "error", nameof(MCPPlugin));
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[GodotMCP] Failed to process incoming message: {ex.Message}");
            EditorHandler.RecordLog($"Failed to process incoming message: {ex.Message}", "error", nameof(MCPPlugin));
        }
    }
}
#endif
