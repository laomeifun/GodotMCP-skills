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
      MCPPlugin.cs
      WebSocketServer.cs
      CommandRouter.cs
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

### ❌ 截图/输入功能不工作（Linux/macOS）

**原因**：Game screenshot 和 Input simulation 功能使用了 Win32 API，仅在 Windows 平台可用。

**现状**：在非 Windows 平台，这些功能会返回友好的错误提示，不会导致插件崩溃。Editor screenshot（编辑器截图）功能不受此限制，在所有平台都可用。

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
| 游戏窗口截图 | ✅ | ❌ | ❌ |
| 输入模拟 | ✅ | ❌ | ❌ |
| 运行时监控 | ✅ | ✅ | ✅ |
