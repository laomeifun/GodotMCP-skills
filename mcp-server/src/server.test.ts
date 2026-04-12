import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { z } from "zod";
import { GodotCommandError } from "./errors.js";
import { buildImageResponseFromPath, executeGodotCommand, godot, server } from "./server.js";

type MockSend = typeof godot.send;

async function withMockedSend<T>(mockSend: MockSend, action: () => Promise<T>): Promise<T> {
  const originalSend = godot.send.bind(godot);
  godot.send = mockSend;
  try {
    return await action();
  } finally {
    godot.send = originalSend;
  }
}

test("executeGodotCommand returns structured project results", async () => {
  const response = await withMockedSend(async (category, command, params, options) => {
    assert.equal(category, "project");
    assert.equal(command, "read_file");
    assert.deepEqual(params, { path: "res://scripts/Player.gd" });
    assert.deepEqual(options, { timeoutMs: undefined });
    return {
      path: "res://scripts/Player.gd",
      content: "extends Node\n",
    };
  }, async () => executeGodotCommand("project", "read_file", { path: "res://scripts/Player.gd" }, {
    outputSchema: {
      path: z.string(),
      content: z.string(),
    },
  }));

  assert.equal(response.isError, undefined);
  assert.equal(response.content[0]?.type, "text");
  assert.deepEqual(JSON.parse(response.content[0]?.type === "text" ? response.content[0].text : "{}"), {
    path: "res://scripts/Player.gd",
    content: "extends Node\n",
  });
  assert.deepEqual(response.structuredContent, {
    path: "res://scripts/Player.gd",
    content: "extends Node\n",
  });
});

test("executeGodotCommand formats structured scene errors", async () => {
  const response = await withMockedSend(async () => {
    throw new GodotCommandError({
      code: "INVALID_PATH",
      message: "path must stay within res://",
      retriable: false,
      category: "scene",
      command: "open",
      context: { provided: "../bad.tscn" },
    });
  }, async () => executeGodotCommand("scene", "open", { path: "../bad.tscn" }));

  assert.equal(response.isError, true);
  assert.equal(response.content[0]?.type, "text");
  assert.equal(
    response.content[0]?.type === "text" ? response.content[0].text : "",
    "Error: [INVALID_PATH] path must stay within res:// scene.open",
  );
  assert.deepEqual(response.structuredContent, {
    error: {
      code: "INVALID_PATH",
      message: "path must stay within res://",
      retriable: false,
      category: "scene",
      command: "open",
      context: { provided: "../bad.tscn" },
    },
  });
});

test("executeGodotCommand applies node transforms before building structured output", async () => {
  const response = await withMockedSend(async (category, command) => {
    assert.equal(category, "node");
    assert.equal(command, "get_properties");
    return {
      node_path: "/root/Main/Player",
      properties: {
        position: {
          type: "vector2",
          display_value: "(12, 34)",
          raw_value: { x: 12, y: 34 },
        },
      },
    };
  }, async () => executeGodotCommand("node", "get_properties", { node_path: "/root/Main/Player" }, {
    transform: async (data) => ({
      ...(data as Record<string, unknown>),
      inspected: true,
    }),
    outputSchema: {
      node_path: z.string(),
      properties: z.record(z.unknown()),
      inspected: z.boolean(),
    },
  }));

  assert.equal(response.isError, undefined);
  assert.deepEqual(response.structuredContent, {
    node_path: "/root/Main/Player",
    properties: {
      position: {
        type: "vector2",
        display_value: "(12, 34)",
        raw_value: { x: 12, y: 34 },
      },
    },
    inspected: true,
  });
});

test("executeGodotCommand passes resolved runtime timeout values to godot.send", async () => {
  let capturedTimeoutMs: number | undefined;

  const response = await withMockedSend(async (_category, _command, _params, options) => {
    capturedTimeoutMs = options?.timeoutMs;
    return {
      ready: true,
      waited_ms: 1200,
      status: { runtime_connected: true },
    };
  }, async () => executeGodotCommand("runtime", "wait_until_ready", { timeout_ms: 1500 }, {
    timeoutMs: (params) => typeof params.timeout_ms === "number" ? params.timeout_ms + 250 : undefined,
    outputSchema: {
      ready: z.boolean(),
      waited_ms: z.number(),
      status: z.record(z.unknown()),
    },
  }));

  assert.equal(capturedTimeoutMs, 1750);
  assert.deepEqual(response.structuredContent, {
    ready: true,
    waited_ms: 1200,
    status: { runtime_connected: true },
  });
});

test("buildImageResponseFromPath reads screenshot bytes and infers mime type", async () => {
  const tempDir = await mkdtemp(join(tmpdir(), "godot-mcp-server-test-"));
  const imagePath = join(tempDir, "shot.jpg");
  const bytes = Buffer.from([0xff, 0xd8, 0xff, 0xd9]);

  try {
    await writeFile(imagePath, bytes);
    const response = await buildImageResponseFromPath({
      path: imagePath,
      format: "jpeg",
      width: 1,
      height: 1,
    });

    assert.equal(response.content[0]?.type, "image");
    if (response.content[0]?.type !== "image") {
      throw new Error("Expected image content");
    }

    assert.equal(response.content[0].mimeType, "image/jpeg");
    assert.equal(response.content[0].data, bytes.toString("base64"));
    assert.deepEqual(response.structuredContent, {
      path: imagePath,
      format: "jpeg",
      width: 1,
      height: 1,
    });
  } finally {
    await rm(tempDir, { recursive: true, force: true });
  }
});

test("server configures task infrastructure for task-capable tools", () => {
  const internalServer = server.server as unknown as {
    _taskStore?: unknown;
    _taskMessageQueue?: unknown;
    getCapabilities?: () => {
      tasks?: {
        requests?: {
          tools?: {
            call?: Record<string, never>;
          };
        };
      };
    };
  };

  assert.ok(internalServer._taskStore);
  assert.ok(internalServer._taskMessageQueue);
  assert.ok(internalServer.getCapabilities?.().tasks?.requests?.tools?.call);
});