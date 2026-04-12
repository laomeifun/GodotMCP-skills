import assert from "node:assert/strict";
import test from "node:test";
import { formatToolErrorText, GodotCommandError, normalizeToolError } from "./errors.js";

test("normalizeToolError preserves Godot command error metadata", () => {
  const error = new GodotCommandError({
    code: "RUNTIME_NOT_READY",
    message: "Runtime bridge is not ready yet.",
    retriable: true,
    category: "runtime",
    command: "wait_until_ready",
    context: { timeout_ms: 1000 },
  });

  assert.deepEqual(normalizeToolError(error), {
    code: "RUNTIME_NOT_READY",
    message: "Runtime bridge is not ready yet.",
    retriable: true,
    category: "runtime",
    command: "wait_until_ready",
    context: { timeout_ms: 1000 },
  });
});

test("formatToolErrorText includes code retry hint and command context", () => {
  const text = formatToolErrorText({
    code: "INVALID_PATH",
    message: "path must stay within res://",
    retriable: false,
    category: "project",
    command: "read_file",
  });

  assert.equal(text, "[INVALID_PATH] path must stay within res:// project.read_file");
});

test("normalizeToolError falls back for plain Error values", () => {
  const normalized = normalizeToolError(new Error("boom"));
  assert.deepEqual(normalized, {
    code: "UNEXPECTED_ERROR",
    message: "boom",
    retriable: false,
  });
});