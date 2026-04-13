import { z } from "zod";
import { registerGodotTool } from "../server.js";

registerGodotTool("input_key", "Simulate a keyboard key press/release in the running game.", "input", "key", {
  key: z.string().describe("Key name (e.g. 'W', 'Space', 'Escape', 'ArrowUp')"),
  pressed: z.boolean().default(true).describe("True for press, false for release"),
  duration: z.number().optional().describe("Hold duration in milliseconds (press then release)"),
}, {
  outputSchema: {
    key: z.string(),
    pressed: z.boolean(),
    duration_ms: z.number(),
  },
});
registerGodotTool("input_mouse", "Simulate a mouse event in the running game.", "input", "mouse", {
  position: z.object({ x: z.number(), y: z.number() }).describe("Screen position"),
  button: z.enum(["left", "right", "middle"]).default("left").describe("Mouse button"),
  action: z.enum(["click", "press", "release", "move"]).default("click").describe("Mouse action type"),
}, {
  outputSchema: {
    action: z.string(),
    button: z.string(),
    position: z.object({ x: z.number(), y: z.number() }),
  },
});
registerGodotTool("input_action", "Trigger a Godot input action.", "input", "action", {
  action_name: z.string().describe("Input action name (e.g. 'move_up', 'jump', 'shoot')"),
  pressed: z.boolean().default(true).describe("True for press, false for release"),
  strength: z.number().min(0).max(1).default(1).describe("Action strength (0.0 to 1.0)"),
  duration: z.number().optional().describe("Optional hold duration before auto-release"),
}, {
  outputSchema: {
    action_name: z.string(),
    pressed: z.boolean(),
    strength: z.number(),
    duration_ms: z.number(),
  },
});
registerGodotTool("input_text", "Type a text string character by character.", "input", "text", {
  text: z.string().describe("Text to type"),
}, {
  outputSchema: {
    typed: z.string(),
    length: z.number(),
  },
});
registerGodotTool("input_sequence", "Execute a timed sequence of input actions. Can also save sequences as named macros and replay them.", "input", "sequence", {
  steps: z.array(z.object({
    type: z.enum(["key", "mouse", "action", "text", "wait"]).describe("Input type"),
    params: z.record(z.unknown()).describe("Parameters for the input"),
    delay_ms: z.number().default(0).describe("Delay in ms before next step"),
  })).optional().describe("Sequence of input steps (required unless replaying a macro)"),
  save_as: z.string().optional().describe("Save this sequence as a named macro for later replay"),
  replay: z.string().optional().describe("Replay a previously saved macro by name (steps not required when set)"),
  loop_count: z.number().default(1).describe("How many times to replay (only used with replay)"),
}, {
  outputSchema: {
    steps_queued: z.number().optional(),
    total_duration_ms: z.number().optional(),
    name: z.string().optional(),
    step_count: z.number().optional(),
    loop_count: z.number().optional(),
    executed_steps: z.number().optional(),
    stored_macro_count: z.number().optional(),
  },
});
