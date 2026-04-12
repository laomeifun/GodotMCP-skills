import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool("script_list", "List all scripts in the project.", "script", "list", {
  path: z.string().default("res://").describe("Directory to search in"),
  language: z.enum(["cs", "gd", "all"]).default("all").describe("Filter by language"),
}, {
  outputSchema: {
    scripts: z.array(z.string()),
    count: z.number(),
  },
});
registerGodotTool("script_read", "Read a script file's source code.", "script", "read", {
  path: z.string().describe("Script path (e.g. 'res://scenes/Player.cs')"),
}, {
  outputSchema: {
    path: z.string(),
    content: z.string(),
  },
});
registerGodotTool("script_create", "Create a new script file.", "script", "create", {
  path: z.string().describe("Path for new script"),
  content: z.string().describe("Script source code"),
  language: z.enum(["cs", "gd"]).default("cs").describe("Script language"),
}, {
  outputSchema: {
    path: z.string(),
  },
});
registerGodotTool("script_edit", "Replace the contents of an existing script file.", "script", "edit", {
  path: z.string().describe("Script path to edit"),
  content: z.string().describe("New source code"),
}, {
  outputSchema: {
    path: z.string(),
  },
});
registerGodotTool("script_attach", "Attach a script to a node in the current scene.", "script", "attach", {
  node_path: z.string().describe("Path to the node"),
  script_path: z.string().describe("Path to the script file"),
}, {
  outputSchema: {
    node_path: z.string(),
    script_path: z.string(),
  },
});
registerGodotTool("script_detach", "Detach the script from a node.", "script", "detach", {
  node_path: z.string().describe("Path to the node"),
}, {
  outputSchema: {
    node_path: z.string(),
  },
});

registerGodotTool("script_find_references", "Search project scripts for references to a symbol using structured text matches.", "script", "find_references", {
  symbol: z.string().describe("Symbol or identifier to search for"),
  path: z.string().default("res://").describe("Directory to search in"),
  language: z.enum(["cs", "gd", "all"]).default("all").describe("Filter by script language"),
  whole_word: z.boolean().default(true).describe("Whether to only match whole words"),
  case_sensitive: z.boolean().default(false).describe("Whether the search is case sensitive"),
  max_results: z.number().default(200).describe("Maximum number of matches to return"),
}, {
  outputSchema: {
    symbol: z.string(),
    references: z.array(z.object({
      path: z.string(),
      line: z.number(),
      column: z.number(),
      match: z.string(),
      excerpt: z.string(),
      language: z.string(),
    })),
    count: z.number(),
    truncated: z.boolean(),
  },
});
