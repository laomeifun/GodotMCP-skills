import { z } from "zod";
import { buildImageResponseFromPath, registerGodotTool } from "../server.js";

registerGodotTool("editor_screenshot", "Capture the editor viewport and return the image plus capture metadata.", "editor", "screenshot",
  { viewport: z.enum(["2d", "3d", "full"]).default("full").describe("Which viewport to capture") },
  {
    outputSchema: {
      path: z.string(),
      format: z.string(),
      width: z.number().optional(),
      height: z.number().optional(),
    },
    responseBuilder: buildImageResponseFromPath,
  },
);
registerGodotTool("editor_game_screenshot", "Capture the running game viewport and return the image plus capture metadata.", "editor", "game_screenshot", undefined,
  {
    outputSchema: {
      path: z.string(),
      format: z.string(),
      width: z.number().optional(),
      height: z.number().optional(),
    },
    responseBuilder: buildImageResponseFromPath,
  },
);
registerGodotTool("editor_get_errors", "Get recent Godot MCP plugin errors, warnings, and bridge events captured inside the editor.", "editor", "get_errors", {
  count: z.number().default(50).describe("Max number of log entries to return"),
}, {
  outputSchema: {
    errors: z.array(z.object({}).passthrough()),
    count: z.number(),
    compilation_errors: z.array(z.object({}).passthrough()),
    compilation_error_count: z.number(),
    compilation_warning_count: z.number(),
  },
});
registerGodotTool("editor_get_compilation_errors", "Get structured compilation-like diagnostics parsed from recent editor and bridge logs.", "editor", "get_compilation_errors", {
  count: z.number().default(100).describe("Max number of recent log entries to inspect for diagnostics"),
  language: z.enum(["cs", "gd"]).optional().describe("Optional language filter"),
}, {
  outputSchema: {
    diagnostics: z.array(z.object({}).passthrough()),
    count: z.number(),
    error_count: z.number(),
    warning_count: z.number(),
  },
});
registerGodotTool("editor_execute_gdscript", "Execute a GDScript expression in the editor context.", "editor", "execute_gdscript", {
  code: z.string().describe("GDScript code to execute"),
}, {
  outputSchema: {
    result: z.string(),
  },
});
registerGodotTool("editor_reload_project", "Reload the current project in the editor.", "editor", "reload_project", undefined, {
  outputSchema: {
    reloading: z.boolean(),
  },
});
registerGodotTool("editor_get_open_files", "List all currently open files in the script editor.", "editor", "get_open_files", undefined, {
  outputSchema: {
    files: z.array(z.object({}).passthrough()),
    count: z.number(),
  },
});
registerGodotTool("editor_open_file", "Open a file in the script editor.", "editor", "open_file", {
  path: z.string().describe("Path to file to open"),
  line: z.number().optional().describe("Line number to jump to"),
}, {
  outputSchema: {
    path: z.string(),
    line: z.number().optional(),
  },
});
