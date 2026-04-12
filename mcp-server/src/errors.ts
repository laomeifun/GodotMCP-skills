export type ToolErrorShape = {
  code: string;
  message: string;
  retriable: boolean;
  context?: Record<string, unknown>;
  category?: string;
  command?: string;
};

export class GodotCommandError extends Error {
  readonly code: string;
  readonly retriable: boolean;
  readonly context?: Record<string, unknown>;
  readonly category?: string;
  readonly command?: string;

  constructor(options: ToolErrorShape) {
    super(options.message);
    this.name = "GodotCommandError";
    this.code = options.code;
    this.retriable = options.retriable;
    this.context = options.context;
    this.category = options.category;
    this.command = options.command;
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function normalizeToolError(error: unknown): ToolErrorShape {
  if (error instanceof GodotCommandError) {
    return {
      code: error.code,
      message: error.message,
      retriable: error.retriable,
      ...(error.context ? { context: error.context } : {}),
      ...(error.category ? { category: error.category } : {}),
      ...(error.command ? { command: error.command } : {}),
    };
  }

  if (error instanceof Error) {
    return {
      code: "UNEXPECTED_ERROR",
      message: error.message,
      retriable: false,
    };
  }

  if (isPlainObject(error)) {
    return {
      code: typeof error.code === "string" ? error.code : "UNKNOWN_ERROR",
      message: typeof error.message === "string" ? error.message : JSON.stringify(error, null, 2),
      retriable: typeof error.retriable === "boolean" ? error.retriable : false,
      ...(isPlainObject(error.context) ? { context: error.context } : {}),
      ...(typeof error.category === "string" ? { category: error.category } : {}),
      ...(typeof error.command === "string" ? { command: error.command } : {}),
    };
  }

  return {
    code: "UNKNOWN_ERROR",
    message: String(error),
    retriable: false,
  };
}

export function formatToolErrorText(error: ToolErrorShape): string {
  const prefix = error.code ? `[${error.code}] ` : "";
  const retryText = error.retriable ? " (retriable)" : "";
  const commandText = error.category && error.command
    ? ` ${error.category}.${error.command}`
    : "";

  return `${prefix}${error.message}${retryText}${commandText}`;
}