import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool("scene_create", "Create a new scene file with a root node of the specified type.", "scene", "create", {
  path: z.string().describe("Scene file path (e.g. 'res://scenes/NewScene.tscn')"),
  root_type: z.string().describe("Root node type (e.g. 'Node2D', 'CharacterBody2D')"),
  root_name: z.string().optional().describe("Root node name (defaults to filename)"),
}, {
  outputSchema: {
    path: z.string(),
  },
});
registerGodotTool("scene_open", "Open a scene in the editor.", "scene", "open", {
  path: z.string().describe("Scene path to open"),
}, {
  outputSchema: {
    path: z.string(),
  },
});
registerGodotTool("scene_get_current", "Get info about the currently open scene.", "scene", "get_current", undefined, {
  outputSchema: {
    name: z.string(),
    path: z.string(),
    type: z.string(),
  },
});
registerGodotTool("scene_save", "Save the current or specified scene.", "scene", "save", {
  path: z.string().optional().describe("Scene path (saves current scene if omitted)"),
}, {
  outputSchema: {
    saved: z.string().optional(),
    path: z.string().optional(),
  },
});
registerGodotTool("scene_delete", "Delete a scene file from the project.", "scene", "delete", {
  path: z.string().describe("Scene path to delete"),
}, {
  outputSchema: {
    deleted: z.string(),
  },
});
registerGodotTool("scene_instance", "Instance a PackedScene as a child of a node in the current scene.", "scene", "instance", {
  scene_path: z.string().describe("Path to the scene to instance"),
  parent_path: z.string().describe("Node path of the parent"),
  name: z.string().optional().describe("Name for the instanced node"),
}, {
  outputSchema: {
    node_path: z.string(),
    name: z.string(),
  },
});
registerGodotTool("scene_get_tree", "Get the full scene tree structure as JSON.", "scene", "get_tree", {
  path: z.string().optional().describe("Scene path (uses current scene if omitted)"),
  node_path: z.string().optional().describe("Optional node path to inspect as the root of the returned subtree"),
  depth: z.number().default(10).describe("Maximum subtree depth to include"),
  flatten: z.boolean().default(false).describe("Return a paged flat node list in addition to or instead of the nested tree"),
  offset: z.number().default(0).describe("Pagination offset into the flattened node list"),
  limit: z.number().default(200).describe("Maximum number of flattened nodes to return"),
}, {
  outputSchema: {
    tree: z.record(z.unknown()).optional(),
    node_path: z.string(),
    depth: z.number(),
    flatten: z.boolean(),
    nodes: z.array(z.object({}).passthrough()),
    total_nodes: z.number(),
    returned_nodes: z.number(),
    offset: z.number(),
    limit: z.number(),
    has_more: z.boolean(),
    next_offset: z.number().optional(),
    path: z.string().optional(),
  },
});
registerGodotTool("scene_play", "Run the project or a specific scene.", "scene", "play", {
  scene_path: z.string().optional().describe("Scene to play (plays main scene if omitted)"),
}, {
  outputSchema: {
    playing: z.boolean(),
  },
});
registerGodotTool("scene_stop", "Stop the currently running game.", "scene", "stop", undefined, {
  outputSchema: {
    stopped: z.boolean(),
  },
});
