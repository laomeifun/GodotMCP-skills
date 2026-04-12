import { readFile } from "node:fs/promises";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import {
  InMemoryTaskMessageQueue,
  InMemoryTaskStore,
} from "@modelcontextprotocol/sdk/experimental/tasks";
import type { ToolAnnotations } from "@modelcontextprotocol/sdk/types.js";
import { z, ZodRawShape } from "zod";
import { formatToolErrorText, normalizeToolError } from "./errors.js";
import { GodotConnection } from "./godot-connection.js";

export const godot = new GodotConnection(
  parseInt(process.env.GODOT_MCP_PORT || "6550", 10),
  parseInt(process.env.GODOT_MCP_TIMEOUT || "15000", 10),
  parseInt(process.env.GODOT_MCP_MAX_RETRIES || "5", 10),
  parseInt(process.env.GODOT_MCP_RETRY_INITIAL_DELAY || "500", 10),
  parseInt(process.env.GODOT_MCP_RETRY_MAX_DELAY || "8000", 10),
  parseInt(process.env.GODOT_MCP_HEARTBEAT_INTERVAL || "10000", 10),
  parseInt(process.env.GODOT_MCP_HEARTBEAT_TIMEOUT || "4000", 10),
);

const taskStore = new InMemoryTaskStore();
const taskMessageQueue = new InMemoryTaskMessageQueue();

export const server = new McpServer({
  name: "godot-mcp-server",
  version: "0.1.0",
}, {
  capabilities: {
    tasks: {
      list: {},
      cancel: {},
      requests: {
        tools: {
          call: {},
        },
      },
    },
  },
  taskStore,
  taskMessageQueue,
});

type TextContent = {
  type: "text";
  text: string;
};

type ImageContent = {
  type: "image";
  data: string;
  mimeType: string;
};

type ToolContent = TextContent | ImageContent;

type ToolResponse = {
  content: ToolContent[];
  structuredContent?: Record<string, unknown>;
  isError?: boolean;
};

type RegisterGodotToolOptions = {
  isImage?: boolean;
  outputSchema?: ZodRawShape;
  transform?: (data: unknown) => unknown | Promise<unknown>;
  structuredContent?: (data: unknown) => Record<string, unknown> | undefined;
  responseBuilder?: (data: unknown) => ToolResponse | Promise<ToolResponse>;
  annotations?: ToolAnnotations;
  timeoutMs?: number | ((params: Record<string, unknown>) => number | undefined);
};

type RegisterGodotTaskToolOptions = RegisterGodotToolOptions & {
  taskSupport?: "optional" | "required";
};

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function toTextContent(result: unknown): TextContent {
  return {
    type: "text",
    text: typeof result === "string" ? result : JSON.stringify(result, null, 2),
  };
}

function getImageMimeType(format: string | undefined): string {
  switch ((format || "").toLowerCase()) {
    case "jpg":
    case "jpeg":
      return "image/jpeg";
    case "webp":
      return "image/webp";
    default:
      return "image/png";
  }
}

export function createToolErrorResponse(error: unknown): ToolResponse {
  const normalizedError = normalizeToolError(error);
  return {
    isError: true,
    content: [{ type: "text", text: `Error: ${formatToolErrorText(normalizedError)}` }],
    structuredContent: {
      error: normalizedError,
    },
  };
}

export function inferAnnotations(command: string, annotations?: ToolAnnotations): ToolAnnotations {
  const normalizedCommand = command.toLowerCase();
  const readOnlyHint = /^(get_|list_|read_|capture_|monitor_|search_|watch_|wait_|inspect_|evaluate_)/.test(normalizedCommand)
    || normalizedCommand.includes("screenshot");
  const destructiveHint = /^(delete|stop|reload_project|write_file|edit|detach|set_property|rename|move|batch_update)$/.test(normalizedCommand);

  return {
    openWorldHint: false,
    ...annotations,
    readOnlyHint: annotations?.readOnlyHint ?? readOnlyHint,
    destructiveHint: annotations?.destructiveHint ?? (readOnlyHint ? false : destructiveHint),
    idempotentHint: annotations?.idempotentHint ?? false,
  };
}

function resolveTimeoutMs(
  timeoutMs: RegisterGodotToolOptions["timeoutMs"],
  params: Record<string, unknown>,
): number | undefined {
  if (typeof timeoutMs === "function") {
    return timeoutMs(params);
  }

  return timeoutMs;
}

async function buildToolResponse(result: unknown, options?: RegisterGodotToolOptions): Promise<ToolResponse> {
  if (options?.responseBuilder) {
    return options.responseBuilder(result);
  }

  const structuredContent = options?.outputSchema
    ? (isPlainObject(result) ? result : undefined)
    : options?.structuredContent?.(result);

  if (options?.isImage && typeof result === "string") {
    return {
      content: [{ type: "image", data: result, mimeType: "image/png" }],
      ...(structuredContent ? { structuredContent } : {}),
    };
  }

  return {
    content: [toTextContent(result)],
    ...(structuredContent ? { structuredContent } : {}),
  };
}

export async function executeGodotCommand(
  category: string,
  command: string,
  params: Record<string, unknown>,
  options?: RegisterGodotToolOptions,
): Promise<ToolResponse> {
  try {
    const data = await godot.send(category, command, params, {
      timeoutMs: resolveTimeoutMs(options?.timeoutMs, params),
    });
    const result = options?.transform ? await options.transform(data) : data;
    return await buildToolResponse(result, options);
  } catch (error) {
    return createToolErrorResponse(error);
  }
}

export async function buildImageResponseFromPath(data: unknown): Promise<ToolResponse> {
  if (!isPlainObject(data) || typeof data.path !== "string") {
    throw new Error("Screenshot result is missing a file path.");
  }

  const buffer = await readFile(data.path);
  const mimeType = getImageMimeType(typeof data.format === "string" ? data.format : undefined);
  const structuredContent: Record<string, unknown> = {
    path: data.path,
    format: typeof data.format === "string" ? data.format : "png",
  };

  if (typeof data.width === "number") structuredContent.width = data.width;
  if (typeof data.height === "number") structuredContent.height = data.height;

  return {
    content: [{
      type: "image",
      data: buffer.toString("base64"),
      mimeType,
    }],
    structuredContent,
  };
}

export function registerGodotTool(
  name: string,
  description: string,
  category: string,
  command: string,
  inputSchema?: ZodRawShape,
  options?: RegisterGodotToolOptions,
) {
  const toolConfig: {
    description: string;
    inputSchema?: ZodRawShape;
    outputSchema?: ZodRawShape;
    annotations?: ToolAnnotations;
  } = {
    description,
    annotations: inferAnnotations(command, options?.annotations),
  };

  if (inputSchema) {
    toolConfig.inputSchema = inputSchema;
  }

  if (options?.outputSchema) {
    toolConfig.outputSchema = options.outputSchema;
  }

  server.registerTool(name, toolConfig, (params) => executeGodotCommand(category, command, params, options));
}

export function registerGodotTaskTool(
  name: string,
  description: string,
  category: string,
  command: string,
  inputSchema: ZodRawShape,
  options?: RegisterGodotTaskToolOptions,
) {
  server.experimental.tasks.registerToolTask(name, {
    description,
    inputSchema,
    outputSchema: options?.outputSchema,
    annotations: inferAnnotations(command, options?.annotations),
    execution: { taskSupport: options?.taskSupport ?? "optional" },
  }, {
    async createTask(params, extra) {
      const task = await extra.taskStore.createTask({ ttl: extra.taskRequestedTtl ?? 300_000 });
      await extra.taskStore.updateTaskStatus(task.taskId, "working");

      void (async () => {
        const result = await executeGodotCommand(category, command, params, options);
        await extra.taskStore.storeTaskResult(
          task.taskId,
          result.isError ? "failed" : "completed",
          result,
        );
      })().catch(async (error: unknown) => {
        await extra.taskStore.storeTaskResult(task.taskId, "failed", createToolErrorResponse(error));
      });

      return { task };
    },
    async getTask(_args, extra) {
      return extra.taskStore.getTask(extra.taskId);
    },
    async getTaskResult(_args, extra) {
      return extra.taskStore.getTaskResult(extra.taskId) as Promise<ToolResponse>;
    },
  });
}

export { z };
