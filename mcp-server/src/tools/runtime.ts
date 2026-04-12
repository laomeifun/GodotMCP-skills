import { z } from "zod";
import { registerGodotTaskTool, registerGodotTool } from "../server.js";

registerGodotTool("runtime_get_bridge_status", "Report whether the editor has an active runtime debugger bridge to the currently running Godot game.", "runtime", "get_bridge_status", undefined, {
  outputSchema: {
    is_game_running: z.boolean(),
    runtime_connected: z.boolean(),
    active_session_id: z.number(),
    last_ready_ms: z.number(),
    last_pong_ms: z.number(),
    pending_requests: z.number(),
    recent_log_count: z.number(),
    current_scene_name: z.string().optional(),
    current_scene_path: z.string().optional(),
    current_scene_node_path: z.string().optional(),
    timestamp_ms: z.number().optional(),
  },
});

registerGodotTool("runtime_get_scene_tree", "Get the live runtime scene tree from the running Godot game through the debugger bridge.", "runtime", "get_scene_tree", {
  depth: z.number().default(10).describe("Maximum tree depth to return"),
  node_path: z.string().optional().describe("Optional node path to inspect as the root of the returned subtree"),
  include_internal: z.boolean().default(false).describe("Whether to include internal engine nodes"),
  flatten: z.boolean().default(false).describe("Return a paged flat node list in addition to or instead of the nested tree"),
  offset: z.number().default(0).describe("Pagination offset into the flattened node list"),
  limit: z.number().default(200).describe("Maximum number of flattened nodes to return"),
}, {
  outputSchema: {
    is_game_running: z.boolean(),
    runtime_connected: z.boolean(),
    depth: z.number(),
    flatten: z.boolean(),
    offset: z.number(),
    limit: z.number(),
    has_more: z.boolean(),
    total_nodes: z.number(),
    returned_nodes: z.number(),
    next_offset: z.number().optional(),
    node_path: z.string(),
    tree: z.record(z.unknown()).optional(),
    nodes: z.array(z.object({}).passthrough()),
    current_scene: z.record(z.unknown()),
  },
});

registerGodotTool("runtime_get_node_properties", "Get properties of a node in the running game, with optional property filtering and pagination.", "runtime", "get_node_properties", {
  node_path: z.string().describe("Node path in the running scene tree"),
  property_names: z.array(z.string()).optional().describe("Optional explicit property names to read"),
  offset: z.number().default(0).describe("Pagination offset into the node property list"),
  limit: z.number().default(200).describe("Maximum number of properties to return"),
}, {
  outputSchema: {
    node_path: z.string(),
    type: z.string(),
    properties: z.record(z.unknown()),
    property_order: z.array(z.string()),
    total_properties: z.number(),
    returned_properties: z.number(),
    offset: z.number(),
    limit: z.number(),
    has_more: z.boolean(),
    next_offset: z.number(),
  },
});

registerGodotTool("runtime_capture_frame", "Capture live frame and performance data from the running Godot game.", "runtime", "capture_frame", undefined, {
  outputSchema: {
    fps: z.number(),
    frame_time_ms: z.number(),
    object_count: z.number(),
    node_count: z.number(),
    orphan_nodes: z.number(),
    render_objects_in_frame: z.number(),
    render_draw_calls: z.number(),
    video_memory_bytes: z.number(),
    physics_2d_active_objects: z.number(),
    static_memory_bytes: z.number(),
    timestamp_ms: z.number(),
  },
});

registerGodotTool("runtime_get_recent_logs", "Get recent runtime bridge events and runtime-side error logs captured from the running game.", "runtime", "get_logs", {
  count: z.number().default(50).describe("Maximum number of runtime log entries to return"),
  level: z.string().optional().describe("Optional level filter such as info, warning, or error"),
}, {
  outputSchema: {
    logs: z.array(z.object({}).passthrough()),
    count: z.number(),
  },
});

registerGodotTool("runtime_run_smoke_check", "Run a Godot-side runtime bridge smoke check that validates live status, scene tree access, frame metrics, and runtime log buffering.", "runtime", "run_smoke_check", undefined, {
  outputSchema: {
    passed: z.boolean(),
    checks: z.array(z.object({}).passthrough()),
    status: z.record(z.unknown()),
    scene_tree: z.record(z.unknown()),
    frame: z.record(z.unknown()),
    recent_logs: z.array(z.object({}).passthrough()),
  },
});

registerGodotTaskTool("runtime_monitor_property", "Sample a runtime property over time and return the collected values. This tool is task-capable for longer captures.", "runtime", "monitor_property", {
  node_path: z.string().describe("Node path in the running game"),
  property: z.string().describe("Property name to monitor"),
  duration: z.number().default(1000).describe("Monitoring duration in ms"),
  interval_ms: z.number().default(100).describe("Sampling interval in ms"),
  max_samples: z.number().default(128).describe("Maximum number of samples to keep"),
}, {
  taskSupport: "optional",
  timeoutMs: (params) => {
    const duration = typeof params.duration === "number" ? params.duration : 1000;
    return duration + 5000;
  },
  outputSchema: {
    node_path: z.string(),
    property: z.string(),
    duration_ms: z.number(),
    interval_ms: z.number(),
    sample_count: z.number(),
    truncated: z.boolean(),
    samples: z.array(z.object({}).passthrough()),
  },
});

registerGodotTaskTool("runtime_wait_until_ready", "Wait until the runtime debugger bridge becomes ready after launching the game.", "runtime", "wait_until_ready", {
  timeout_ms: z.number().default(10000).describe("Maximum time to wait in ms"),
  poll_interval_ms: z.number().default(100).describe("Polling interval while waiting"),
}, {
  taskSupport: "optional",
  timeoutMs: (params) => {
    const timeoutMs = typeof params.timeout_ms === "number" ? params.timeout_ms : 10000;
    return timeoutMs + 2000;
  },
  outputSchema: {
    ready: z.boolean(),
    waited_ms: z.number(),
    status: z.record(z.unknown()),
  },
});

registerGodotTaskTool("runtime_watch_signal", "Watch a runtime node signal for a duration and capture structured emission events.", "runtime", "watch_signal", {
  node_path: z.string().describe("Runtime node path"),
  signal: z.string().describe("Signal name to watch"),
  duration: z.number().default(1000).describe("How long to watch in ms"),
  max_events: z.number().default(128).describe("Maximum number of signal events to keep"),
}, {
  taskSupport: "optional",
  timeoutMs: (params) => {
    const duration = typeof params.duration === "number" ? params.duration : 1000;
    return duration + 5000;
  },
  outputSchema: {
    node_path: z.string(),
    signal: z.string(),
    duration_ms: z.number(),
    event_count: z.number(),
    truncated: z.boolean(),
    events: z.array(z.object({}).passthrough()),
  },
});

registerGodotTaskTool("runtime_watch_node_lifecycle", "Sample runtime node existence and in-tree state over time to observe lifecycle transitions.", "runtime", "watch_node_lifecycle", {
  node_path: z.string().describe("Runtime node path to monitor"),
  duration: z.number().default(1000).describe("Monitoring duration in ms"),
  poll_interval_ms: z.number().default(100).describe("Polling interval between samples"),
  max_samples: z.number().default(256).describe("Maximum number of lifecycle samples to keep"),
}, {
  taskSupport: "optional",
  timeoutMs: (params) => {
    const duration = typeof params.duration === "number" ? params.duration : 1000;
    return duration + 5000;
  },
  outputSchema: {
    node_path: z.string(),
    duration_ms: z.number(),
    poll_interval_ms: z.number(),
    sample_count: z.number(),
    samples: z.array(z.object({}).passthrough()),
    transitions: z.array(z.object({}).passthrough()),
    truncated: z.boolean(),
  },
});

registerGodotTool("runtime_evaluate_gdscript", "Evaluate a readonly Godot Expression inside the running game, optionally with a node as the base instance.", "runtime", "evaluate_expression", {
  expression: z.string().describe("Readonly Godot expression to evaluate"),
  node_path: z.string().optional().describe("Optional node path used as the expression base instance"),
}, {
  outputSchema: {
    expression: z.string(),
    node_path: z.string(),
    readonly: z.boolean(),
    result: z.object({}).passthrough(),
  },
});
