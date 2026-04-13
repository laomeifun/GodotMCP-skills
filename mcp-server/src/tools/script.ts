import { z } from "zod";
import { registerGodotTool } from "../server.js";

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
