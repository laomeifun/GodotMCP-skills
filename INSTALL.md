# GodotMCP 安装指南

## 前提条件

- [Godot 4.6+](https://godotengine.org/) （.NET / C# 版本）
- [Node.js](https://nodejs.org/) 18+
- 你的 Godot 项目已经创建了 C# 解决方案（项目中已有 `.csproj` / `.sln` 文件）

> **提示**：如果你的 Godot 项目还没有 C# 解决方案，在 Godot 编辑器中创建任意一个 C# 脚本即可自动生成。

---

## 步骤 1: 安装 Godot 插件

将本仓库中的 `godot-mcp/` 文件夹复制到你的 Godot 项目的 `addons/` 目录下：

```
your-project/
  addons/
    godot-mcp/
      BridgeSerialization.cs
      MCPPlugin.cs
      CommandRouter.cs
      RuntimeBridgeAutoload.cs
      RuntimeBridgeDebuggerPlugin.cs
      RuntimeBridgeProtocol.cs
      RuntimeBridgeService.cs
      WebSocketServer.cs
      Handlers/
        BaseHandler.cs
        EditorHandler.cs
        InputHandler.cs
        NodeHandler.cs
        ProjectHandler.cs
        RuntimeHandler.cs
        SceneHandler.cs
        ScriptHandler.cs
        Win32Helper.cs
      plugin.cfg
```

### 验证 .csproj 包含 addons 目录

打开你项目的 `.csproj` 文件，确认 **没有** 排除 `addons/` 目录。默认情况下 Godot 会编译项目中的所有 `.cs` 文件，以下配置应该能正常工作：

```xml
<!-- ✅ 正确：默认包含所有 .cs 文件 -->
<Project Sdk="Godot.NET.Sdk/4.6.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- ... -->
  </PropertyGroup>
</Project>
```

如果你的 `.csproj` 自定义了 `<ItemGroup>` 中的 `<Compile>` 或 `<None>` 标签，请确保没有排除 `addons/godot-mcp/` 路径。

### 构建项目

在 Godot 编辑器中点击底部面板的 **Build** 按钮（或使用外部 IDE 构建）。确保编译通过，没有错误。

### 启用插件

前往 **Project > Project Settings > Plugins**，勾选 **Godot MCP**。

在 Godot 的 **Output** 面板中应该看到：
```
[GodotMCP] WebSocket server listening on port 6550
[GodotMCP] Plugin enabled (port=6550)
```

---

## 步骤 2: 构建 MCP Server

```bash
cd mcp-server
npm install
# npm install 会自动触发 `prepare` 脚本执行 tsc 编译
# 如果需要手动构建：
npm run build
```

---

## 步骤 3: 配置 AI 工具

将 MCP Server 添加到你的 AI 工具配置中。

### Claude Code

在项目根目录创建或编辑 `.claude/settings.json`：

```json
{
  "mcpServers": {
    "godot": {
      "command": "node",
      "args": ["<absolute-path-to>/mcp-server/build/index.js"]
    }
  }
}
```

### Gemini CLI / Antigravity

在 MCP 配置中添加：

```json
{
  "mcpServers": {
    "godot": {
      "command": "node",
      "args": ["<absolute-path-to>/mcp-server/build/index.js"]
    }
  }
}
```

---

## 常见问题排查

### ❌ 编译失败：找不到 `GodotMCP` 命名空间

**原因**：你的 `.csproj` 可能排除了 `addons/` 目录的文件。

**解决方案**：确保 `.csproj` 使用默认的文件包含方式（不指定 `<Compile Include=...>`），或者手动添加：
```xml
<ItemGroup>
  <Compile Include="addons/godot-mcp/**/*.cs" />
</ItemGroup>
```

### ❌ `Failed to create an autoload, script 'res://addons/godot-mcp/RuntimeBridgeAutoload.cs' is not compiling`

**原因**：这通常不是 `RuntimeBridgeAutoload.cs` 单文件本身坏了，而是 Godot 当前无法实例化这份 C# 脚本。最常见的原因有：

- 项目还没有生成 C# 解决方案（缺少 `.csproj` / `.sln`）
- 你复制了新版插件，但项目的 `.csproj` 仍显式排除了 `addons/godot-mcp/**/*.cs`
- 还没先在 Godot 里 **Build** 一次 C# 项目
- 使用的 Godot 版本低于插件当前要求的 **Godot .NET 4.6+**

**排查步骤**：
1. 确认项目根目录里已经有 `.csproj` 或 `.sln`；如果没有，先创建任意一个 C# 脚本让 Godot 生成
2. 打开 `.csproj`，确认没有排除 `addons/godot-mcp/**/*.cs`
3. 在 Godot 编辑器里点击底部 **Build**，确保 C# 编译通过
4. 如果刚升级过插件，建议 **Project > Reload Current Project** 或重开 Godot
5. 确认你使用的是 **Godot .NET 4.6+**

**补充说明**：新版插件在检测到这类情况时，会先输出更明确的提示，而不是反复直接尝试创建 autoload。

### ❌ 端口 6550 被占用

**原因**：另一个 Godot 编辑器实例启用了 MCP 插件，或者其他程序占用了该端口。

**解决方案**：设置环境变量 `GODOT_MCP_PORT` 为其他端口号（如 `6551`），同时在 MCP Server 启动时也设置相同的端口：
```json
{
  "mcpServers": {
    "godot": {
      "command": "node",
      "args": ["<path>/mcp-server/build/index.js"],
      "env": {
        "GODOT_MCP_PORT": "6551"
      }
    }
  }
}
```

### ❌ MCP 工具报"Cannot connect to Godot"

**原因**：MCP Server 无法连接到 Godot 编辑器的 WebSocket 服务器。

**排查步骤**：
1. 确认 Godot 编辑器正在运行
2. 确认 MCP 插件已启用（Project > Project Settings > Plugins）
3. 检查 Godot Output 面板是否有 `[GodotMCP] WebSocket server listening on port 6550` 的日志
4. 如果看到 `Failed to start WebSocket server`，参考端口占用解决方案

### ❌ 游戏截图 / 输入 / `runtime_*` 工具不工作

**原因**：这些能力现在依赖 Godot 的 runtime debugger bridge，而不是旧的 Win32 宿主窗口方案。若游戏不是从 Godot 编辑器内启动，或者刚启动后 bridge 还没 ready，就会出现“没有活动 runtime session”或“runtime bridge not ready”之类的报错。

**排查步骤**：
1. 用 Godot 编辑器内的运行按钮，或通过 `scene_play` 启动游戏
2. 等待一小会儿，必要时先调用 `runtime_wait_until_ready`
3. 确认 Godot Output 中出现 runtime bridge 相关日志
4. 如果 Godot Output 里已经有你自己项目抛出的异常（尤其是资源校验、`_ValidateProperty()`、构造函数或 `_Ready()` 里的异常），先修这些异常；运行时场景没真正启动起来时，runtime bridge 也无法附着
5. 再调用 `editor_game_screenshot`、`input_*` 或其他 `runtime_*` 工具

**如果看到** `Unknown message: godot_mcp_runtime:log|ready|scene_changed`：

- 这通常不是业务逻辑本身坏掉，而是**运行时 autoload 在编辑器 debugger session 完全挂好之前，就先发了自定义消息**。
- 当前版本已改为：**先等编辑器发起第一次 debugger 握手，再发送 `ready/log/scene_changed` 这类主动通知**，以避免启动瞬间的竞态告警。
- 如果你升级源码后仍看到这类告警：
  1. 重新 Build C# 项目
  2. 重载或重开 Godot 项目
  3. 再次从编辑器内启动游戏

**现状**：这些能力现在走 Godot runtime bridge，理论上不再依赖 Windows 专属 API；`editor_screenshot` 仍然始终是纯编辑器侧能力。

### ❌ 插件启用后编辑器崩溃

**排查步骤**：
1. 关闭 Godot 编辑器
2. 删除项目中的 `.godot/` 目录
3. 重新打开项目，让 Godot 重新导入所有资源
4. 确保项目使用的 Godot SDK 版本与 `.csproj` 中的 `Godot.NET.Sdk` 版本一致
5. 重新构建项目

---

## 平台支持

| 功能 | Windows | Linux | macOS |
|------|---------|-------|-------|
| 项目/场景/节点/脚本管理 | ✅ | ✅ | ✅ |
| 编辑器截图 | ✅ | ✅ | ✅ |
| 游戏窗口截图（runtime bridge） | ✅ | ✅ | ✅ |
| 输入模拟（runtime bridge） | ✅ | ✅ | ✅ |
| 运行时监控 | ✅ | ✅ | ✅ |

> 注：游戏窗口截图、输入模拟和其他 `runtime_*` 能力都要求**从 Godot 编辑器内启动游戏**，以便 debugger bridge 能附着到实际运行的游戏进程。
