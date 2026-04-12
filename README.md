# Godot MCP

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) server that connects AI assistants like Claude to the Godot 4.6+ editor. Control scenes, nodes, scripts, and even run your game — all from your AI coding tool.

Built for **Godot .NET (C#)**.

## Getting Started

### Prerequisites

- [Godot 4.6+](https://godotengine.org/) with .NET / C# support
- [Node.js](https://nodejs.org/) 18+

### 1. Install the Godot Plugin

Copy the `godot-mcp/` folder into your Godot project's `addons/` directory:

```
your-project/
  addons/
    godot-mcp/
      MCPPlugin.cs
      WebSocketServer.cs
      CommandRouter.cs
      Handlers/
      plugin.cfg
```

Then enable the plugin in **Project > Project Settings > Plugins** — check **Godot MCP**.

### 2. Build the MCP Server

```bash
cd mcp-server
npm install
npm run build
```

### 3. Configure Your AI Tool

Add the server to your MCP client config. For **Claude Code**, add to your `.claude/settings.json`:

```json
{
  "mcpServers": {
    "godot": {
      "command": "node",
      "args": ["<path-to>/mcp-server/build/index.js"]
    }
  }
}
```

### 4. Open Godot and Start Using

Open your Godot project in the editor. The plugin starts a WebSocket server on port `6550` automatically. The MCP server connects to it when your AI tool makes its first request.

When the plugin is enabled, it also installs a temporary runtime bridge autoload plus an editor debugger bridge so `runtime_*` capabilities can inspect the *actual running game*, not just the editor process.

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `GODOT_MCP_PORT` | `6550` | WebSocket port the MCP server connects to |
| `GODOT_MCP_TIMEOUT` | `15000` | Request timeout in milliseconds |
| `GODOT_MCP_MAX_RETRIES` | `5` | Maximum WebSocket reconnect attempts |
| `GODOT_MCP_RETRY_INITIAL_DELAY` | `500` | Initial reconnect delay in milliseconds |
| `GODOT_MCP_RETRY_MAX_DELAY` | `8000` | Maximum reconnect delay in milliseconds |
| `GODOT_MCP_HEARTBEAT_INTERVAL` | `10000` | WebSocket heartbeat interval in milliseconds |
| `GODOT_MCP_HEARTBEAT_TIMEOUT` | `4000` | WebSocket heartbeat acknowledgement timeout in milliseconds |

## Architecture

```
AI Tool (Claude Code, etc.)
  ↕ stdio (MCP protocol)
MCP Server (Node.js / TypeScript)
  ↕ WebSocket (localhost:6550)
Godot Editor Plugin (C# EditorPlugin)
  ↕ EditorDebuggerPlugin / runtime autoload bridge
Running Game Process
  ↕ Godot API
Your Game Project
```

Two components:

- **`mcp-server/`** — TypeScript MCP server using stdio transport. Validates inputs with Zod, forwards commands over WebSocket.
- **`godot-mcp/`** — C# EditorPlugin that runs inside the Godot editor. Receives commands via WebSocket, executes editor-side commands directly, and proxies runtime commands through a debugger bridge into the running game process.

## Tools

64 tools across 7 categories, plus 10 fixed resources, 2 runtime resource templates, and 3 prompts.

### Project

| Tool | Description |
|---|---|
| `project_get_settings` | Read project.godot settings. Returns all settings if no section/key specified. |
| `project_list_files` | List files in a directory. Supports glob-like filtering and recursive scan. |
| `project_read_file` | Read the contents of a project file. |
| `project_write_file` | Write or create a file in the project. |
| `project_get_uid` | Convert a resource path to its UID. |
| `project_get_path_from_uid` | Convert a UID back to a resource path. |
| `project_search_text` | Search plain text across project files and return structured matches. |
| `asset_get_dependencies` | Inspect normalized dependency information for a scene or asset resource. |
| `asset_find_unused` | Heuristically find assets with no inbound references. |

### Scene

| Tool | Description |
|---|---|
| `scene_create` | Create a new scene file with a root node of the specified type. |
| `scene_open` | Open a scene in the editor. |
| `scene_get_current` | Get info about the currently open scene. |
| `scene_save` | Save the current or specified scene. |
| `scene_delete` | Delete a scene file from the project. |
| `scene_instance` | Instance a PackedScene as a child of a node in the current scene. |
| `scene_get_tree` | Inspect the current scene or a target scene as a nested tree and/or paged flat node list. |
| `scene_play` | Run the project or a specific scene. |
| `scene_stop` | Stop the currently running game. |

### Node

| Tool | Description |
|---|---|
| `node_add` | Add a new node to the scene tree. |
| `node_delete` | Remove a node from the scene tree. |
| `node_rename` | Rename a node. |
| `node_duplicate` | Duplicate a node and all its children. |
| `node_move` | Reparent a node to a new parent. |
| `node_get_properties` | Get typed node properties with optional filtering and pagination. |
| `node_set_property` | Set a property on a node. |
| `node_get_signals` | List all signals defined on a node. |
| `node_connect_signal` | Connect a signal to a method on another node. |
| `node_disconnect_signal` | Disconnect a signal connection. |
| `node_get_children` | List immediate children of a node. |
| `node_inspect_deep` | Inspect a node deeply, including signals, typed properties, and recursive child context. |
| `node_batch_update` | Apply a batch of node edits as a single undo/redo action. |

### Script

| Tool | Description |
|---|---|
| `script_list` | List all scripts in the project. Filter by language (C#, GDScript, or both). |
| `script_read` | Read a script file's source code. |
| `script_create` | Create a new script file. |
| `script_edit` | Replace the contents of an existing script file. |
| `script_attach` | Attach a script to a node in the current scene. |
| `script_detach` | Detach the script from a node. |
| `script_find_references` | Search project scripts for references to a symbol with structured text matches. |

### Editor

| Tool | Description |
|---|---|
| `editor_screenshot` | Capture the editor viewport (2D, 3D, or full) and return the image plus capture metadata. |
| `editor_game_screenshot` | Capture the running game viewport and return the image plus capture metadata. |
| `editor_get_errors` | Get recent Godot MCP plugin errors, warnings, and bridge events captured inside the editor. |
| `editor_get_compilation_errors` | Get structured compilation-like diagnostics parsed from recent editor and bridge logs. |
| `editor_execute_gdscript` | Execute a GDScript expression in the editor context. |
| `editor_reload_project` | Reload the current project in the editor. |
| `editor_get_open_files` | List all currently open files in the script editor. |
| `editor_open_file` | Open a file in the script editor at an optional line number. |

### Input

| Tool | Description |
|---|---|
| `input_key` | Simulate a keyboard key press/release in the running game via the runtime bridge. |
| `input_mouse` | Simulate a mouse event (click, press, release, move) in the running game. |
| `input_action` | Trigger a Godot input action (e.g. `move_up`, `jump`). |
| `input_text` | Type a text string character by character. |
| `input_sequence` | Execute a timed sequence of input actions. |
| `input_record_macro` | Store a reusable named input macro as a sequence of steps. |
| `input_playback_macro` | Replay a previously stored input macro through the runtime bridge. |

### Runtime

| Tool | Description |
|---|---|
| `runtime_get_bridge_status` | Report whether the editor has an active runtime debugger bridge to the currently running Godot game. |
| `runtime_get_scene_tree` | Get the live runtime scene tree from the running Godot game through the debugger bridge. |
| `runtime_get_node_properties` | Get properties of a runtime node with optional property filtering and pagination. |
| `runtime_capture_frame` | Capture live frame and performance data from the running Godot game. |
| `runtime_get_recent_logs` | Get recent runtime bridge events and runtime-side error logs captured from the running game. |
| `runtime_run_smoke_check` | Run a Godot-side runtime bridge smoke check across status, scene tree, frame metrics, and runtime logs. |
| `runtime_monitor_property` | Sample a runtime property over time; this tool is task-capable for longer captures. |
| `runtime_wait_until_ready` | Wait until the runtime debugger bridge becomes ready after launching the game. |
| `runtime_watch_signal` | Watch a runtime signal for a duration and capture structured emission events. |
| `runtime_watch_node_lifecycle` | Sample node existence and in-tree state over time to observe lifecycle transitions. |
| `runtime_evaluate_gdscript` | Evaluate a readonly Godot expression inside the running game. |

## Resources

These MCP resources provide structured context without requiring a tool call.

| Resource URI | Description |
|---|---|
| `godot://scene/current` | Metadata for the scene currently open in the Godot editor. |
| `godot://scene/tree/current` | Tree structure for the scene currently open in the Godot editor. |
| `godot://project/settings` | Project settings visible from the current Godot editor session. |
| `godot://editor/open-files` | Files currently open in the Godot script editor. |
| `godot://editor/errors/recent` | Recent Godot MCP plugin errors, warnings, and bridge events captured in the editor. |
| `godot://editor/compilation-errors` | Structured compilation diagnostics parsed from recent editor and bridge logs. |
| `godot://runtime/status` | Runtime bridge connectivity and live scene identity for the running Godot game. |
| `godot://runtime/scene/tree/current` | Live runtime scene tree for the running game at the default depth. |
| `godot://runtime/frame/latest` | Latest runtime performance counters from the running game. |
| `godot://runtime/logs/recent` | Recent runtime bridge events and runtime-side error logs. |

### Resource Templates

| Resource Template | Description |
|---|---|
| `godot://runtime/scene/tree/{depth}` | Live runtime scene tree at a requested depth. |
| `godot://runtime/node/{nodePath}` | Live property snapshot for a runtime node. `nodePath` should be URI-encoded. |

## Prompts

These MCP prompts package common Godot review/diagnostic workflows into reusable templates.

| Prompt | Description |
|---|---|
| `analyze_current_scene` | Generate a review prompt for the currently open scene, optionally focused on a specific concern. |
| `diagnose_recent_errors` | Generate a debugging prompt from recent Godot MCP plugin errors, warnings, and bridge events. |
| `analyze_runtime_scene` | Generate a live runtime analysis prompt from the runtime scene tree, frame metrics, and recent runtime logs. |

## Behavior Notes

- `editor_screenshot` and `editor_game_screenshot` now return image content to the MCP client and include capture metadata as structured output.
- `editor_game_screenshot`, `input_*`, and the rest of `runtime_*` depend on the runtime debugger bridge, so the game must be launched from the Godot editor and allowed to reach a ready state.
- `editor_get_errors` reports errors, warnings, and key events captured by the Godot MCP bridge itself; it is not a full mirror of every Godot editor log line.
- `editor_get_compilation_errors` is a best-effort structured diagnostic layer built from recent editor and bridge logs; it is intentionally lighter than a full compiler service.
- `runtime_*` tools now use a dedicated runtime debugger bridge backed by an auto-installed autoload plus an `EditorDebuggerPlugin`, so scene trees, properties, frame stats, logs, and monitors come from the actual running game process.
- Runtime-only resources and the `analyze_runtime_scene` prompt appear dynamically when the runtime bridge is connected, and the server emits MCP `resources/list_changed`, `prompts/list_changed`, and resource-updated notifications as live state changes.
- `runtime_monitor_property` is task-capable, so long captures can run through MCP task-aware flows instead of blocking the main request path.
- `runtime_run_smoke_check` gives you an executable Godot-side integration smoke test for the runtime bridge.
- Input simulation and running-game screenshots now go through the runtime bridge instead of a Win32-only host-window path, so they are no longer Windows-specific.
- File and scene operations are restricted to `res://` and `user://` paths to reduce accidental project escape.

## License

MIT
