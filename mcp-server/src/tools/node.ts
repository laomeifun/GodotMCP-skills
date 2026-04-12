import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool("node_add", "Add a new node to the scene tree.", "node", "add", {
  parent_path: z.string().describe("Path to parent node"),
  type: z.string().describe("Node type (e.g. 'CharacterBody2D', 'Sprite2D')"),
  name: z.string().describe("Name for the new node"),
}, {
  outputSchema: {
    node_path: z.string(),
    name: z.string(),
  },
});
registerGodotTool("node_delete", "Remove a node from the scene tree.", "node", "delete", {
  node_path: z.string().describe("Path to node to delete"),
}, {
  outputSchema: {
    deleted: z.string(),
  },
});
registerGodotTool("node_rename", "Rename a node.", "node", "rename", {
  node_path: z.string().describe("Path to node to rename"),
  new_name: z.string().describe("New name for the node"),
}, {
  outputSchema: {
    old_name: z.string(),
    new_name: z.string(),
  },
});
registerGodotTool("node_duplicate", "Duplicate a node and all its children.", "node", "duplicate", {
  node_path: z.string().describe("Path to node to duplicate"),
  new_name: z.string().optional().describe("Name for the duplicate"),
}, {
  outputSchema: {
    node_path: z.string(),
    name: z.string(),
  },
});
registerGodotTool("node_move", "Reparent a node to a new parent.", "node", "move", {
  node_path: z.string().describe("Path to node to move"),
  new_parent_path: z.string().describe("Path to new parent node"),
}, {
  outputSchema: {
    node_path: z.string(),
  },
});
registerGodotTool("node_get_properties", "Get all properties of a node.", "node", "get_properties", {
  node_path: z.string().describe("Path to node"),
  property_names: z.array(z.string()).optional().describe("Optional explicit property names to return"),
  offset: z.number().default(0).describe("Pagination offset into the property list"),
  limit: z.number().default(200).describe("Maximum number of properties to return"),
}, {
  outputSchema: {
    properties: z.record(z.unknown()),
    property_order: z.array(z.string()),
    node_path: z.string(),
    type: z.string(),
    total_properties: z.number(),
    returned_properties: z.number(),
    offset: z.number(),
    limit: z.number(),
    has_more: z.boolean(),
    next_offset: z.number(),
  },
});
registerGodotTool("node_set_property", "Set a property on a node.", "node", "set_property", {
  node_path: z.string().describe("Path to node"),
  property: z.string().describe("Property name (e.g. 'position', 'visible')"),
  value: z.unknown().describe("Value to set (type depends on property)"),
}, {
  outputSchema: {
    property: z.string(),
    value: z.unknown(),
  },
});
registerGodotTool("node_get_signals", "List all signals defined on a node.", "node", "get_signals", {
  node_path: z.string().describe("Path to node"),
}, {
  outputSchema: {
    signals: z.array(z.object({
      name: z.unknown(),
      args: z.array(z.unknown()).optional(),
    }).passthrough()),
  },
});
registerGodotTool("node_connect_signal", "Connect a signal to a method on another node.", "node", "connect_signal", {
  node_path: z.string().describe("Path to node emitting the signal"),
  signal: z.string().describe("Signal name"),
  target_path: z.string().describe("Path to target node"),
  method: z.string().describe("Method name on target"),
}, {
  outputSchema: {
    connected: z.string(),
  },
});
registerGodotTool("node_disconnect_signal", "Disconnect a signal connection.", "node", "disconnect_signal", {
  node_path: z.string().describe("Path to node emitting the signal"),
  signal: z.string().describe("Signal name"),
  target_path: z.string().describe("Path to target node"),
  method: z.string().describe("Method name on target"),
}, {
  outputSchema: {
    disconnected: z.string(),
  },
});
registerGodotTool("node_get_children", "List immediate children of a node.", "node", "get_children", {
  node_path: z.string().describe("Path to parent node"),
}, {
  outputSchema: {
    children: z.array(z.object({
      name: z.unknown(),
      type: z.string(),
      index: z.number(),
    }).passthrough()),
    count: z.number(),
  },
});

registerGodotTool("node_inspect_deep", "Inspect a node deeply, including typed properties, signals, and a recursive child tree summary.", "node", "inspect_deep", {
  node_path: z.string().describe("Path to the node to inspect"),
  depth: z.number().default(2).describe("Maximum child depth to include in the inspection tree"),
  include_child_properties: z.boolean().default(false).describe("Whether child nodes should also include a small typed property snapshot"),
  property_offset: z.number().default(0).describe("Pagination offset into the root node property list"),
  property_limit: z.number().default(200).describe("Maximum number of root node properties to return"),
}, {
  outputSchema: {
    node: z.object({}).passthrough(),
    signals: z.array(z.object({}).passthrough()),
    tree: z.object({}).passthrough(),
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

registerGodotTool("node_batch_update", "Apply a batch of node operations as a single editor undo/redo action.", "node", "batch_update", {
  action_name: z.string().default("Batch Node Update").describe("Undo/redo action label"),
  operations: z.array(z.object({
    type: z.string(),
  }).passthrough()).describe("Ordered node operations such as add, rename, set_property, move, attach_script, or signal connect/disconnect"),
}, {
  outputSchema: {
    action_name: z.string(),
    operation_count: z.number(),
    operations: z.array(z.object({}).passthrough()),
    aliases: z.record(z.string()),
  },
});
