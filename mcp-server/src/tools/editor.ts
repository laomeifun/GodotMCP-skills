import { z } from "zod";
import { buildImageResponseFromPath, registerGodotTool } from "../server.js";

registerGodotTool("screenshot", "Capture the editor viewport and return the image plus capture metadata.", "editor", "screenshot",
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
registerGodotTool("game_screenshot", "Capture the running game viewport and return the image plus capture metadata.", "editor", "game_screenshot", undefined,
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
registerGodotTool("get_errors", "Get recent Godot MCP plugin errors, warnings, and bridge events captured inside the editor.", "editor", "get_errors", {
  count: z.number().default(50).describe("Max number of log entries to return"),
  include_compilation: z.boolean().default(true).describe("Include parsed compilation diagnostics"),
  language: z.enum(["cs", "gd"]).optional().describe("Optional language filter for compilation errors"),
}, {
  outputSchema: {
    errors: z.array(z.object({
        message: z.string(),
        type: z.string(),
        source: z.string(),
        timestamp_ms: z.number()
    }).passthrough()),
    count: z.number(),
    compilation_errors: z.array(z.object({
        path: z.string(),
        line: z.number(),
        column: z.number(),
        severity: z.string(),
        message: z.string(),
        language: z.string()
    }).passthrough()),
    compilation_error_count: z.number(),
    compilation_warning_count: z.number(),
  },
});
registerGodotTool("execute_gdscript", "Execute a GDScript expression in the editor context.", "editor", "execute_gdscript", {
  code: z.string().describe("GDScript code to execute"),
}, {
  outputSchema: {
    result: z.string(),
  },
});
registerGodotTool("reload_project", "Reload the current project in the editor.", "editor", "reload_project", undefined, {
  outputSchema: {
    reloading: z.boolean(),
  },
});
registerGodotTool("get_open_files", "List all currently open files in the script editor.", "editor", "get_open_files", undefined, {
  outputSchema: {
    files: z.array(z.object({
        path: z.string(),
        type: z.string()
    }).passthrough()),
    count: z.number(),
  },
});
registerGodotTool("open_file", "Open a file in the script editor.", "editor", "open_file", {
  path: z.string().describe("Path to file to open"),
  line: z.number().optional().describe("Line number to jump to"),
}, {
  outputSchema: {
    path: z.string(),
    line: z.number().optional(),
  },
});
