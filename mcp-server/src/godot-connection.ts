import WebSocket from "ws";

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
}

export class GodotConnection {
  private ws: WebSocket | null = null;
  private pendingRequests = new Map<string, {
    resolve: (value: GodotResponse) => void;
    reject: (reason: Error) => void;
    timer: ReturnType<typeof setTimeout>;
  }>();
  private port: number;
  private requestTimeout: number;
  private maxRetries: number;
  private retryDelayMs: number;
  private connecting: Promise<void> | null = null;

  constructor(port = 6550, requestTimeout = 15000, maxRetries = 3, retryDelayMs = 1000) {
    this.port = port;
    this.requestTimeout = requestTimeout;
    this.maxRetries = maxRetries;
    this.retryDelayMs = retryDelayMs;
  }

  async connect(): Promise<void> {
    if (this.ws?.readyState === WebSocket.OPEN) return;

    // 防止并发连接尝试
    if (this.connecting) return this.connecting;

    this.connecting = this.connectWithRetry();
    try {
      await this.connecting;
    } finally {
      this.connecting = null;
    }
  }

  private async connectWithRetry(): Promise<void> {
    let lastError: Error | null = null;

    for (let attempt = 1; attempt <= this.maxRetries; attempt++) {
      try {
        await this.attemptConnect();
        return; // 连接成功
      } catch (err) {
        lastError = err instanceof Error ? err : new Error(String(err));
        if (attempt < this.maxRetries) {
          console.error(
            `[GodotMCP] Connection attempt ${attempt}/${this.maxRetries} failed, ` +
            `retrying in ${this.retryDelayMs}ms...`
          );
          await new Promise(resolve => setTimeout(resolve, this.retryDelayMs));
        }
      }
    }

    throw new Error(
      `Cannot connect to Godot on port ${this.port} after ${this.maxRetries} attempts. ` +
      `Make sure the Godot editor is running with the MCP plugin enabled. ` +
      `Last error: ${lastError?.message}`
    );
  }

  private attemptConnect(): Promise<void> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${this.port}`);

      const connectTimeout = setTimeout(() => {
        ws.terminate();
        reject(new Error(`Connection timed out after 5000ms`));
      }, 5000);

      ws.on("open", () => {
        clearTimeout(connectTimeout);
        this.ws = ws;
        console.error(`[GodotMCP] Connected to Godot on port ${this.port}`);
        resolve();
      });

      ws.on("message", (data) => {
        const text = data.toString();
        try {
          const response: GodotResponse = JSON.parse(text);
          const pending = this.pendingRequests.get(response.id);
          if (pending) {
            clearTimeout(pending.timer);
            this.pendingRequests.delete(response.id);
            pending.resolve(response);
          }
        } catch (e) {
          console.error("[GodotMCP] Failed to parse response:", text);
        }
      });

      ws.on("close", () => {
        clearTimeout(connectTimeout);
        console.error("[GodotMCP] Disconnected from Godot");
        this.ws = null;
        for (const [id, pending] of this.pendingRequests) {
          clearTimeout(pending.timer);
          pending.reject(new Error("Connection closed"));
        }
        this.pendingRequests.clear();
      });

      ws.on("error", (err) => {
        clearTimeout(connectTimeout);
        // 如果 ws 还没设置为 this.ws，说明是在连接阶段失败
        if (this.ws !== ws) {
          reject(new Error(
            `Cannot connect to Godot on port ${this.port}: ${err.message}`
          ));
        } else {
          // 已连接后的错误，走 close 回调处理
          console.error(`[GodotMCP] WebSocket error: ${err.message}`);
        }
      });
    });
  }

  async send(category: string, command: string, params: Record<string, unknown> = {}): Promise<unknown> {
    // 自动重连（包含重试）
    await this.connect();

    const id = crypto.randomUUID();
    const message: GodotCommand = { id, category, command, params };

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pendingRequests.delete(id);
        reject(new Error(`Command ${category}.${command} timed out after ${this.requestTimeout}ms`));
      }, this.requestTimeout);

      this.pendingRequests.set(id, {
        resolve: (response) => {
          if (response.success) {
            resolve(response.data);
          } else {
            reject(new Error(response.error || "Unknown error from Godot"));
          }
        },
        reject,
        timer,
      });

      this.ws!.send(JSON.stringify(message));
    });
  }

  disconnect(): void {
    this.ws?.close();
    this.ws = null;
  }
}

