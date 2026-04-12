import { randomUUID } from "node:crypto";
import WebSocket from "ws";
import { GodotCommandError } from "./errors.js";

interface GodotCommand {
  id: string;
  category: string;
  command: string;
  params: Record<string, unknown>;
}

interface GodotResponse {
  id: string;
  success: boolean;
  data?: unknown;
  error?: string;
  error_code?: string;
  error_context?: Record<string, unknown>;
  retriable?: boolean;
}

type PendingRequest = {
  resolve: (value: GodotResponse) => void;
  reject: (reason: Error) => void;
  timer: ReturnType<typeof setTimeout>;
};

export interface GodotSendOptions {
  timeoutMs?: number;
}

export function calculateBackoffDelayMs(
  attempt: number,
  initialDelayMs: number,
  maxDelayMs: number,
  jitterRatio = 0.2,
  random = Math.random,
): number {
  const exponentialDelay = Math.min(maxDelayMs, initialDelayMs * 2 ** Math.max(0, attempt - 1));
  const jitterWindow = exponentialDelay * jitterRatio;
  const jitter = (random() * 2 - 1) * jitterWindow;
  return Math.max(initialDelayMs, Math.round(exponentialDelay + jitter));
}

function toError(error: unknown): Error {
  return error instanceof Error ? error : new Error(String(error));
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export class GodotConnection {
  private ws: WebSocket | null = null;
  private readonly pendingRequests = new Map<string, PendingRequest>();
  private readonly port: number;
  private readonly requestTimeout: number;
  private readonly maxRetries: number;
  private readonly initialRetryDelayMs: number;
  private readonly maxRetryDelayMs: number;
  private readonly heartbeatIntervalMs: number;
  private readonly heartbeatTimeoutMs: number;
  private connecting: Promise<void> | null = null;
  private heartbeatInterval: ReturnType<typeof setInterval> | null = null;
  private heartbeatTimeout: ReturnType<typeof setTimeout> | null = null;
  private awaitingHeartbeatAck = false;

  constructor(
    port = 6550,
    requestTimeout = 15000,
    maxRetries = 5,
    initialRetryDelayMs = 500,
    maxRetryDelayMs = 8000,
    heartbeatIntervalMs = 10000,
    heartbeatTimeoutMs = 4000,
  ) {
    this.port = port;
    this.requestTimeout = requestTimeout;
    this.maxRetries = maxRetries;
    this.initialRetryDelayMs = initialRetryDelayMs;
    this.maxRetryDelayMs = maxRetryDelayMs;
    this.heartbeatIntervalMs = heartbeatIntervalMs;
    this.heartbeatTimeoutMs = heartbeatTimeoutMs;
  }

  async connect(): Promise<void> {
    if (this.isConnected()) {
      return;
    }

    if (this.connecting) {
      return this.connecting;
    }

    this.connecting = this.connectWithRetry();
    try {
      await this.connecting;
    } finally {
      this.connecting = null;
    }
  }

  isConnected(): boolean {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  private async connectWithRetry(): Promise<void> {
    let lastError: Error | null = null;

    for (let attempt = 1; attempt <= this.maxRetries; attempt += 1) {
      try {
        await this.attemptConnect();
        return;
      } catch (error) {
        lastError = toError(error);
        if (attempt >= this.maxRetries) {
          break;
        }

        const retryDelayMs = calculateBackoffDelayMs(
          attempt,
          this.initialRetryDelayMs,
          this.maxRetryDelayMs,
        );

        console.error(
          `[GodotMCP] Connection attempt ${attempt}/${this.maxRetries} failed (${lastError.message}). ` +
          `Retrying in ${retryDelayMs}ms...`,
        );
        await sleep(retryDelayMs);
      }
    }

    throw new Error(
      `Cannot connect to Godot on port ${this.port} after ${this.maxRetries} attempts. ` +
      `Make sure the Godot editor is running with the MCP plugin enabled. ` +
      `Last error: ${lastError?.message ?? "unknown error"}`,
    );
  }

  private attemptConnect(): Promise<void> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${this.port}`);
      const connectTimeout = setTimeout(() => {
        ws.terminate();
        reject(new Error("Connection timed out after 5000ms"));
      }, 5000);

      const onOpen = () => {
        cleanupHandshakeListeners();
        this.attachSocket(ws);
        console.error(`[GodotMCP] Connected to Godot on port ${this.port}`);
        resolve();
      };

      const onError = (error: Error) => {
        cleanupHandshakeListeners();
        reject(new Error(`Cannot connect to Godot on port ${this.port}: ${error.message}`));
      };

      const onClose = () => {
        cleanupHandshakeListeners();
        reject(new Error("Connection closed during handshake"));
      };

      const cleanupHandshakeListeners = () => {
        clearTimeout(connectTimeout);
        ws.off("open", onOpen);
        ws.off("error", onError);
        ws.off("close", onClose);
      };

      ws.once("open", onOpen);
      ws.once("error", onError);
      ws.once("close", onClose);
    });
  }

  private attachSocket(socket: WebSocket): void {
    this.cleanupSocket();
    this.ws = socket;

    socket.on("message", (data) => this.handleMessage(data));
    socket.on("close", () => this.handleClose(socket));
    socket.on("error", (error) => this.handleSocketError(socket, error));
    socket.on("pong", () => this.markHeartbeatHealthy());

    this.startHeartbeat();
  }

  private handleMessage(data: WebSocket.RawData): void {
    this.markHeartbeatHealthy();

    const text = data.toString();
    try {
      const response: GodotResponse = JSON.parse(text);
      const pendingRequest = this.pendingRequests.get(response.id);
      if (!pendingRequest) {
        return;
      }

      clearTimeout(pendingRequest.timer);
      this.pendingRequests.delete(response.id);
      pendingRequest.resolve(response);
    } catch (error) {
      console.error("[GodotMCP] Failed to parse response:", text, toError(error).message);
    }
  }

  private handleClose(socket: WebSocket): void {
    if (this.ws !== socket) {
      return;
    }

    console.error("[GodotMCP] Disconnected from Godot");
    this.cleanupSocket();
    this.ws = null;
    this.rejectPendingRequests(new Error("Connection closed"));
  }

  private handleSocketError(socket: WebSocket, error: Error): void {
    if (this.ws !== socket) {
      return;
    }

    console.error(`[GodotMCP] WebSocket error: ${error.message}`);
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();

    this.heartbeatInterval = setInterval(() => {
      if (!this.isConnected() || !this.ws) {
        return;
      }

      if (this.awaitingHeartbeatAck) {
        console.error("[GodotMCP] Heartbeat timed out, terminating stale socket.");
        this.ws.terminate();
        return;
      }

      this.awaitingHeartbeatAck = true;
      this.heartbeatTimeout = setTimeout(() => {
        if (this.awaitingHeartbeatAck && this.ws) {
          console.error("[GodotMCP] Heartbeat acknowledgement not received in time.");
          this.ws.terminate();
        }
      }, this.heartbeatTimeoutMs);

      try {
        this.ws.ping();
      } catch (error) {
        console.error(`[GodotMCP] Failed to send heartbeat ping: ${toError(error).message}`);
        this.ws.terminate();
      }
    }, this.heartbeatIntervalMs);

    this.heartbeatInterval.unref?.();
  }

  private stopHeartbeat(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }

    if (this.heartbeatTimeout) {
      clearTimeout(this.heartbeatTimeout);
      this.heartbeatTimeout = null;
    }

    this.awaitingHeartbeatAck = false;
  }

  private markHeartbeatHealthy(): void {
    this.awaitingHeartbeatAck = false;
    if (this.heartbeatTimeout) {
      clearTimeout(this.heartbeatTimeout);
      this.heartbeatTimeout = null;
    }
  }

  private cleanupSocket(): void {
    this.stopHeartbeat();

    if (!this.ws) {
      return;
    }

    this.ws.removeAllListeners("message");
    this.ws.removeAllListeners("close");
    this.ws.removeAllListeners("error");
    this.ws.removeAllListeners("pong");
  }

  private rejectPendingRequests(reason: Error): void {
    for (const [requestId, pendingRequest] of this.pendingRequests) {
      clearTimeout(pendingRequest.timer);
      pendingRequest.reject(reason);
      this.pendingRequests.delete(requestId);
    }
  }

  async send(
    category: string,
    command: string,
    params: Record<string, unknown> = {},
    options?: GodotSendOptions,
  ): Promise<unknown> {
    await this.connect();

    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
      throw new Error("Godot WebSocket connection is not open.");
    }

    const timeoutMs = options?.timeoutMs ?? this.requestTimeout;
    const requestId = randomUUID();
    const message: GodotCommand = { id: requestId, category, command, params };

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pendingRequests.delete(requestId);
        reject(new Error(`Command ${category}.${command} timed out after ${timeoutMs}ms`));
      }, timeoutMs);

      this.pendingRequests.set(requestId, {
        resolve: (response) => {
          if (response.success) {
            resolve(response.data);
          } else {
            reject(new GodotCommandError({
              code: response.error_code || "COMMAND_FAILED",
              message: response.error || "Unknown error from Godot",
              retriable: response.retriable === true,
              ...(response.error_context ? { context: response.error_context } : {}),
              category,
              command,
            }));
          }
        },
        reject,
        timer,
      });

      this.ws?.send(JSON.stringify(message), (error) => {
        if (!error) {
          return;
        }

        const pendingRequest = this.pendingRequests.get(requestId);
        if (!pendingRequest) {
          return;
        }

        clearTimeout(pendingRequest.timer);
        this.pendingRequests.delete(requestId);
        pendingRequest.reject(toError(error));
      });
    });
  }

  disconnect(): void {
    const socket = this.ws;
    this.cleanupSocket();
    this.ws = null;

    if (socket && socket.readyState !== WebSocket.CLOSED && socket.readyState !== WebSocket.CLOSING) {
      socket.close();
    }

    this.rejectPendingRequests(new Error("Connection closed"));
  }
}

