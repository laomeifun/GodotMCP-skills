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
registerGodotTool("input_sequence", "Execute a timed sequence of input actions.", "input", "sequence", {
  steps: z.array(z.object({
    type: z.enum(["key", "mouse", "action", "text", "wait"]).describe("Input type"),
    params: z.record(z.unknown()).describe("Parameters for the input"),
    delay_ms: z.number().default(0).describe("Delay in ms before next step"),
  })).describe("Sequence of input steps"),
}, {
  outputSchema: {
    steps_queued: z.number(),
    total_duration_ms: z.number(),
  },
});

registerGodotTool("input_record_macro", "Store a reusable named input macro as a sequence of steps.", "input", "record_macro", {
  name: z.string().describe("Macro name"),
  steps: z.array(z.object({
    type: z.enum(["key", "mouse", "action", "text", "wait"]),
    params: z.record(z.unknown()),
    delay_ms: z.number().default(0),
  })).describe("Sequence of steps to store"),
}, {
  outputSchema: {
    name: z.string(),
    step_count: z.number(),
    stored_macro_count: z.number(),
  },
});

registerGodotTool("input_playback_macro", "Replay a previously stored input macro through the runtime bridge.", "input", "playback_macro", {
  name: z.string().describe("Macro name to replay"),
  loop_count: z.number().default(1).describe("How many times to replay the macro"),
}, {
  outputSchema: {
    name: z.string(),
    loop_count: z.number(),
    step_count: z.number(),
    executed_steps: z.number(),
  },
});
