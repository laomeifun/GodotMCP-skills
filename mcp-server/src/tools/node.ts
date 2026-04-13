import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool("node_delete", "Remove a node from the scene tree.", "node", "delete", {
  node_path: z.string().describe("Path to node to delete"),
}, {
  outputSchema: {
    deleted: z.string(),
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

registerGodotTool("node_inspect_deep", "Inspect a node deeply, including typed properties, signals, and a recursive child tree summary. Use this as the primary way to read node properties, signals, and children.", "node", "inspect_deep", {
  node_path: z.string().describe("Path to the node to inspect"),
  depth: z.number().default(2).describe("Maximum child depth to include in the inspection tree"),
  include_child_properties: z.boolean().default(false).describe("Whether child nodes should also include a small typed property snapshot"),
  property_names: z.array(z.string()).optional().describe("Optional explicit property names to return. If omitted, returns all editor-visible properties"),
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

registerGodotTool("node_batch_update", "Apply a batch of node operations as a single editor undo/redo action. Supports operation types: add, delete, rename, move, set_property, attach_script, detach_script, connect_signal, disconnect_signal.", "node", "batch_update", {
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
