import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool(
  "project_get_settings",
  "Read project.godot settings. Returns all settings if no section/key specified.",
  "project", "get_settings",
  {
    section: z.string().optional().describe("Settings section (e.g. 'application')"),
    key: z.string().optional().describe("Specific key within section"),
  },
  {
    outputSchema: {
      value: z.unknown().optional(),
      settings: z.record(z.unknown()).optional(),
      truncated: z.boolean().optional(),
    },
  }
);

registerGodotTool(
  "project_list_files",
  "List files in the project directory. Supports glob-like filtering.",
  "project", "list_files",
  {
    path: z.string().default("res://").describe("Directory path to list (e.g. 'res://scenes/')"),
    filter: z.string().optional().describe("File extension filter (e.g. '*.tscn', '*.cs')"),
    recursive: z.boolean().default(false).describe("List files recursively"),
    offset: z.number().default(0).describe("Pagination offset into the matched file list"),
    limit: z.number().default(200).describe("Maximum number of files to return"),
  },
  {
    outputSchema: {
      files: z.array(z.string()),
      total_files: z.number(),
      returned_files: z.number(),
      offset: z.number(),
      limit: z.number(),
      has_more: z.boolean(),
      next_offset: z.number().optional(),
      truncated: z.boolean().optional(),
    },
  }
);

registerGodotTool("project_read_file", "Read the contents of a project file.", "project", "read_file", {
  path: z.string().describe("Resource path (e.g. 'res://scenes/Player.tscn')"),
}, {
  outputSchema: {
    path: z.string(),
    content: z.string(),
  },
});

registerGodotTool("project_write_file", "Write or create a file in the project.", "project", "write_file", {
  path: z.string().describe("Resource path to write to"),
  content: z.string().describe("File content"),
}, {
  outputSchema: {
    path: z.string(),
  },
});

registerGodotTool("project_get_uid", "Convert a resource path to its UID.", "project", "get_uid", {
  path: z.string().describe("Resource path (e.g. 'res://scenes/Player.tscn')"),
}, {
  outputSchema: {
    uid: z.string(),
    path: z.string(),
  },
});

registerGodotTool("project_get_path_from_uid", "Convert a UID back to a resource path.", "project", "get_path_from_uid", {
  uid: z.string().describe("Resource UID string"),
}, {
  outputSchema: {
    path: z.string(),
    uid: z.string(),
  },
});

registerGodotTool("project_search_text", "Search text across project files and return structured matches.", "project", "search_text", {
  path: z.string().default("res://").describe("Directory path to search under"),
  query: z.string().describe("Plain text query to search for"),
  recursive: z.boolean().default(true).describe("Whether to search subdirectories recursively"),
  filter: z.string().optional().describe("Optional file filter like '*.gd' or '*.tscn'"),
  case_sensitive: z.boolean().default(false).describe("Whether the search is case sensitive"),
  whole_word: z.boolean().default(false).describe("Whether to only match whole words"),
  max_results: z.number().default(200).describe("Maximum number of matches to return"),
}, {
  outputSchema: {
    query: z.string(),
    matches: z.array(z.object({
      path: z.string(),
      line: z.number(),
      column: z.number(),
      match: z.string(),
      excerpt: z.string(),
    })),
    count: z.number(),
    truncated: z.boolean(),
  },
});

registerGodotTool("asset_get_dependencies", "Get normalized dependency information for a scene or asset resource.", "project", "get_dependencies", {
  path: z.string().describe("Resource path to inspect"),
}, {
  outputSchema: {
    path: z.string(),
    dependencies: z.array(z.object({
      raw: z.string(),
      path: z.string(),
      uid: z.string(),
    })),
    count: z.number(),
  },
});

registerGodotTool("asset_find_unused", "Heuristically find project assets with no inbound references.", "project", "find_unused_assets", {
  path: z.string().default("res://").describe("Directory path to scan for assets"),
  include_scripts: z.boolean().default(false).describe("Whether .gd/.cs scripts should also be considered candidate assets"),
}, {
  outputSchema: {
    unused_assets: z.array(z.object({
      path: z.string(),
      inbound_ref_count: z.number(),
      dependency_count: z.number(),
      confidence: z.string(),
      reason: z.string(),
    })),
    count: z.number(),
    truncated: z.boolean(),
    include_scripts: z.boolean(),
  },
});
