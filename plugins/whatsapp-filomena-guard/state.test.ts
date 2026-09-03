import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { createMessageIdRegistry } from "./state.js";

function withTemporaryState(
  run: (params: { directory: string; filePath: string; warnings: string[] }) => void,
) {
  const directory = mkdtempSync(join(tmpdir(), "filomena-state-"));
  const filePath = join(directory, "state.json");
  const warnings: string[] = [];
  try {
    run({ directory, filePath, warnings });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test("persists normalized message IDs and reloads them", () => {
  withTemporaryState(({ filePath, warnings }) => {
    const registry = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });

    assert.equal(registry.record(" bot-1 "), true);
    assert.equal(registry.has("bot-1"), true);
    assert.deepEqual(registry.snapshot(), ["bot-1"]);

    const reloaded = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });
    assert.equal(reloaded.has(" bot-1 "), true);
    assert.deepEqual(reloaded.snapshot(), ["bot-1"]);
    assert.deepEqual(warnings, []);
  });
});

test("de-duplicates IDs, moves repeats to newest, and evicts the oldest", () => {
  withTemporaryState(({ filePath, warnings }) => {
    const registry = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });

    registry.record("bot-1");
    registry.record("bot-2");
    registry.record("bot-1");
    registry.record("bot-3");
    registry.record("bot-4");

    assert.deepEqual(registry.snapshot(), ["bot-1", "bot-3", "bot-4"]);
    assert.equal(registry.has("bot-2"), false);
    assert.deepEqual(warnings, []);
  });
});

test("rejects blank and non-string IDs without writing state", () => {
  withTemporaryState(({ filePath, warnings }) => {
    const registry = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });

    assert.equal(registry.record("   "), false);
    assert.equal(registry.record(42), false);
    assert.equal(registry.has(undefined), false);
    assert.deepEqual(registry.snapshot(), []);
    assert.throws(() => readFileSync(filePath, "utf8"), /ENOENT/);
    assert.deepEqual(warnings, []);
  });
});

test("starts empty without warning when the state file is missing", () => {
  withTemporaryState(({ filePath, warnings }) => {
    const registry = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });

    assert.deepEqual(registry.snapshot(), []);
    assert.deepEqual(warnings, []);
  });
});

test("warns and remains usable when persisted JSON is malformed", () => {
  withTemporaryState(({ filePath, warnings }) => {
    writeFileSync(filePath, "not-json", "utf8");
    const registry = createMessageIdRegistry({
      filePath,
      maxEntries: 3,
      warn: (message: string) => warnings.push(message),
    });

    assert.deepEqual(registry.snapshot(), []);
    assert.equal(warnings.length, 1);
    assert.match(warnings[0]!, /could not load state/);
    assert.equal(registry.record("bot-after-error"), true);
    assert.equal(registry.has("bot-after-error"), true);
  });
});
