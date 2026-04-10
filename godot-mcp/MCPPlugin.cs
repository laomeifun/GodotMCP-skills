#if TOOLS
using Godot;
using GodotMCP.Handlers;

namespace GodotMCP;

[Tool]
public partial class MCPPlugin : EditorPlugin
{
    private const int DefaultPort = 6550;
    private WebSocketServer _wsServer;
    private CommandRouter _router;

    public override void _EnterTree()
    {
        SetMeta("MCPPlugin", this);

        // 从环境变量读取端口号，保持与 MCP Server 同步
        var port = DefaultPort;
        var envPort = System.Environment.GetEnvironmentVariable("GODOT_MCP_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var parsedPort))
            port = parsedPort;

        _wsServer = new WebSocketServer();
        AddChild(_wsServer);
        var err = _wsServer.StartServer(port);

        if (err != Error.Ok)
        {
            GD.PrintErr("[GodotMCP] ⚠ Plugin enabled but WebSocket server failed to start.");
            GD.PrintErr("[GodotMCP] MCP tools will not respond until the server is running.");
            // 即使 WebSocket 失败也继续加载，避免 EditorPlugin 本身崩溃
        }

        _wsServer.ClientConnected += OnClientConnected;
        _wsServer.ClientDisconnected += OnClientDisconnected;
        _wsServer.MessageReceived += OnMessageReceived;

        _router = new CommandRouter();
        _router.RegisterHandler("project", new ProjectHandler(this));
        _router.RegisterHandler("scene", new SceneHandler(this));
        _router.RegisterHandler("node", new NodeHandler(this));
        _router.RegisterHandler("script", new ScriptHandler(this));
        _router.RegisterHandler("editor", new EditorHandler(this));
        _router.RegisterHandler("input", new InputHandler(this));
        _router.RegisterHandler("runtime", new RuntimeHandler(this));

        GD.Print($"[GodotMCP] Plugin enabled (port={port})");
    }

    public override void _ExitTree()
    {
        _wsServer?.StopServer();
        _wsServer?.QueueFree();
        RemoveMeta("MCPPlugin");
        GD.Print("[GodotMCP] Plugin disabled");
    }

    public override void _Process(double delta)
    {
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
        if (_router == null || _wsServer == null)
        {
            GD.PrintErr("[GodotMCP] Plugin not ready, ignoring message");
            return;
        }
        var response = _router.Route(message);
        var err = _wsServer.SendText(clientId, response);
        if (err != Error.Ok)
            GD.PrintErr($"[GodotMCP] Failed to send response: {err}");
    }
}
#endif
