import assert from "node:assert/strict";
import { once } from "node:events";
import test from "node:test";
import WebSocket, { WebSocketServer } from "ws";
import { GodotCommandError } from "./errors.js";
import { GodotConnection, calculateBackoffDelayMs } from "./godot-connection.js";

async function createFakeGodotServer(
  onConnection: (socket: WebSocket) => void,
): Promise<{ server: WebSocketServer; port: number }> {
  const server = new WebSocketServer({ port: 0 });
  await once(server, "listening");
  server.on("connection", onConnection);

  const address = server.address();
  if (typeof address !== "object" || !address) {
    throw new Error("Failed to obtain fake Godot server address.");
  }

  return { server, port: address.port };
}

async function closeFakeGodotServer(server: WebSocketServer | null): Promise<void> {
  if (!server) {
    return;
  }

  await new Promise<void>((resolve, reject) => {
    server.close((error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });
}

test("calculateBackoffDelayMs applies exponential growth and bounded jitter", () => {
  assert.equal(calculateBackoffDelayMs(3, 500, 8000, 0.2, () => 1), 2400);
  assert.equal(calculateBackoffDelayMs(3, 500, 8000, 0.2, () => 0), 1600);
  assert.equal(calculateBackoffDelayMs(6, 500, 2000, 0.2, () => 1), 2400);
});

test("GodotConnection sends a command and receives a fake Godot response", async () => {
  let server: WebSocketServer | null = null;
  let connection: GodotConnection | null = null;

  try {
    const fakeServer = await createFakeGodotServer((socket) => {
      socket.on("message", (rawData) => {
        const request = JSON.parse(rawData.toString()) as {
          id: string;
          category: string;
          command: string;
          params: Record<string, unknown>;
        };

        socket.send(JSON.stringify({
          id: request.id,
          success: true,
          data: {
            echoed_command: `${request.category}.${request.command}`,
            params: request.params,
          },
        }));
      });
    });

    server = fakeServer.server;
    connection = new GodotConnection(fakeServer.port, 1000, 1, 10, 20, 60_000, 5_000);

    const result = await connection.send("runtime", "get_bridge_status", { probe: true });
    assert.deepEqual(result, {
      echoed_command: "runtime.get_bridge_status",
      params: { probe: true },
    });
  } finally {
    connection?.disconnect();
    await closeFakeGodotServer(server);
  }
});

test("GodotConnection surfaces fake Godot error responses and custom timeouts", async () => {
  let server: WebSocketServer | null = null;
  let connection: GodotConnection | null = null;

  try {
    const fakeServer = await createFakeGodotServer((socket) => {
      socket.on("message", (rawData) => {
        const request = JSON.parse(rawData.toString()) as { id: string; command: string };
        if (request.command === "fail") {
          socket.send(JSON.stringify({
            id: request.id,
            success: false,
            error: "boom",
            error_code: "FAKE_FAILURE",
            error_context: { source: "test" },
            retriable: true,
          }));
        }
      });
    });

    server = fakeServer.server;
    connection = new GodotConnection(fakeServer.port, 1000, 1, 10, 20, 60_000, 5_000);
    const activeConnection = connection;
    assert.ok(activeConnection);

    await assert.rejects(async () => {
      await activeConnection.send("runtime", "fail", {});
    }, (error: unknown) => {
      assert.ok(error instanceof GodotCommandError);
      assert.equal(error.code, "FAKE_FAILURE");
      assert.equal(error.retriable, true);
      assert.equal(error.category, "runtime");
      assert.equal(error.command, "fail");
      assert.deepEqual(error.context, { source: "test" });
      assert.equal(error.message, "boom");
      return true;
    });

    await assert.rejects(
      connection.send("runtime", "never_returns", {}, { timeoutMs: 50 }),
      /timed out after 50ms/,
    );
  } finally {
    connection?.disconnect();
    await closeFakeGodotServer(server);
  }
});
