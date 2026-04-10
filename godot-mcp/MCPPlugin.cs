#if TOOLS
using Godot;
using GodotMCP.Handlers;
using Error = Godot.Error;

namespace GodotMCP;

[Tool]
public partial class MCPPlugin : EditorPlugin
{
    private const int DefaultPort = 6550;
    private WebSocketServer? _wsServer;
    private CommandRouter? _router;

    public override void _EnterTree()
    {
        SetMeta("MCPPlugin", this);

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
            }

            // 使用 Callable 连接可以防止 C# 热重载时事件被清理
            _wsServer.Connect(WebSocketServer.SignalName.ClientConnected, Callable.From<int>(OnClientConnected));
            _wsServer.Connect(WebSocketServer.SignalName.ClientDisconnected, Callable.From<int>(OnClientDisconnected));
            _wsServer.Connect(WebSocketServer.SignalName.MessageReceived, Callable.From<int, string>(OnMessageReceived));
        }

        EnsureInitialized();

        GD.Print($"[GodotMCP] Plugin enabled (port={port})");
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

    public override void _ExitTree()
    {
        if (_wsServer != null && IsInstanceValid(_wsServer))
        {
            _wsServer.StopServer();
            _wsServer.QueueFree();
            _wsServer = null;
        }
        RemoveMeta("MCPPlugin");
        GD.Print("[GodotMCP] Plugin disabled");
    }

    public override void _Process(double delta)
    {
        EnsureInitialized();
        _wsServer?.Poll();
    }

    private void OnClientConnected(int clientId)
    {
        GD.Print($"[GodotMCP] Client {clientId} connected");
    }

    private void OnClientDisconnected(int clientId)
    {
        GD.Print($"[GodotMCP] Client {clientId} disconnected");
    }

    private void OnMessageReceived(int clientId, string message)
    {
        EnsureInitialized();
        if (_router == null || _wsServer == null)
        {
            GD.PrintErr("[GodotMCP] Plugin not ready, ignoring message");
            return;
        }
        var response = _router.Route(message);
        var err = _wsServer.SendText(clientId, response);
        if (err != Godot.Error.Ok)
            GD.PrintErr($"[GodotMCP] Failed to send response: {err}");
    }
}
#endif
