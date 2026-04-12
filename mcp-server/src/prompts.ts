import { z } from "zod";
import { godot, server } from "./server.js";

async function getRuntimeStatus(): Promise<Record<string, unknown>> {
  return await godot.send("runtime", "get_bridge_status", {}) as Record<string, unknown>;
}

server.registerPrompt(
  "analyze_current_scene",
  {
    description: "Create a prompt that analyzes the scene currently open in the Godot editor.",
    argsSchema: {
      focus: z.string().optional().describe("Optional focus area such as performance, naming, UI flow, or signal wiring."),
    },
  },
  async ({ focus }) => {
    const sceneInfo = await godot.send("scene", "get_current", {});
    const sceneTree = await godot.send("scene", "get_tree", {});
    const focusText = focus
      ? `Focus especially on: ${focus}.`
      : "Focus on structure, naming, signal wiring, maintainability, and likely Godot-specific pitfalls.";

    return {
      description: "Analyze the currently open Godot scene.",
      messages: [
        {
          role: "user" as const,
          content: {
            type: "text" as const,
            text: [
              "Please analyze the current Godot scene and provide concrete improvement suggestions.",
              focusText,
              "",
              "Current scene:",
              JSON.stringify(sceneInfo, null, 2),
              "",
              "Scene tree:",
              JSON.stringify(sceneTree, null, 2),
            ].join("\n"),
          },
        },
      ],
    };
  }
);

server.registerPrompt(
  "diagnose_recent_errors",
  {
    description: "Create a prompt that diagnoses recent Godot MCP plugin errors and warnings.",
    argsSchema: {
      count: z.number().optional().describe("How many recent log entries to include."),
    },
  },
  async ({ count }) => {
    const errorLog = await godot.send("editor", "get_errors", { count: count ?? 50 });
    const compilationDiagnostics = await godot.send("editor", "get_compilation_errors", { count: count ?? 100 });

    return {
      description: "Diagnose recent Godot MCP bridge errors.",
      messages: [
        {
          role: "user" as const,
          content: {
            type: "text" as const,
            text: [
              "Review the following recent Godot MCP plugin errors, warnings, and events.",
              "Identify the most likely root causes, note any recurring patterns, and propose the next debugging steps.",
              "Also review the structured compilation diagnostics when present.",
              "",
              "Structured compilation diagnostics:",
              JSON.stringify(compilationDiagnostics, null, 2),
              "",
              "Recent errors / warnings / events:",
              JSON.stringify(errorLog, null, 2),
            ].join("\n"),
          },
        },
      ],
    };
  }
);

const analyzeRuntimeScenePrompt = server.registerPrompt(
  "analyze_runtime_scene",
  {
    description: "Create a prompt that analyzes the live runtime scene, frame metrics, and recent runtime logs.",
    argsSchema: {
      focus: z.string().optional().describe("Optional focus area such as runtime performance, scene transitions, or node state."),
      depth: z.number().optional().describe("Optional runtime tree depth to inspect."),
      logCount: z.number().optional().describe("How many recent runtime log entries to include."),
    },
  },
  async ({ focus, depth, logCount }) => {
    const runtimeStatus = await getRuntimeStatus();
    const runtimeTree = await godot.send("runtime", "get_scene_tree", { depth: depth ?? 6 });
    const frameStats = await godot.send("runtime", "capture_frame", {});
    const runtimeLogs = await godot.send("runtime", "get_logs", { count: logCount ?? 50 });

    const focusText = focus
      ? `Focus especially on: ${focus}.`
      : "Focus on runtime structure, state consistency, scene transitions, and likely performance/debugging issues.";

    return {
      description: "Analyze the current live Godot runtime scene.",
      messages: [
        {
          role: "user" as const,
          content: {
            type: "text" as const,
            text: [
              "Please analyze the live Godot runtime and propose concrete improvements or debugging steps.",
              focusText,
              "",
              "Runtime bridge status:",
              JSON.stringify(runtimeStatus, null, 2),
              "",
              "Runtime scene tree:",
              JSON.stringify(runtimeTree, null, 2),
              "",
              "Frame statistics:",
              JSON.stringify(frameStats, null, 2),
              "",
              "Recent runtime logs:",
              JSON.stringify(runtimeLogs, null, 2),
            ].join("\n"),
          },
        },
      ],
    };
  },
);

analyzeRuntimeScenePrompt.disable();

let runtimePromptEnabled = false;

async function pollPromptAvailability() {
  if (!server.isConnected()) {
    return;
  }

  let runtimeConnected = false;
  try {
    const runtimeStatus = await getRuntimeStatus();
    runtimeConnected = runtimeStatus.runtime_connected === true;
  } catch {
    runtimeConnected = false;
  }

  if (runtimeConnected === runtimePromptEnabled) {
    return;
  }

  runtimePromptEnabled = runtimeConnected;
  if (runtimeConnected) {
    analyzeRuntimeScenePrompt.enable();
  } else {
    analyzeRuntimeScenePrompt.disable();
  }

  if (server.isConnected()) {
    server.sendPromptListChanged();
  }
}

const promptPoller = setInterval(() => {
  void pollPromptAvailability();
}, 2500);

promptPoller.unref?.();
