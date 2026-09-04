import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import plugin from "./index.ts";

test("registers response hooks only; passive observation remains owned by memory", () => {
  const directory = mkdtempSync(join(tmpdir(), "telegram-filomena-"));
  const hooks = [];
  try {
    plugin.register({
      pluginConfig: {
        respond: {
          direct: { mode: "allow", chats: [] },
          groups: { "-1001234567890": { mode: "mention-or-reply" } },
        },
        stateFile: join(directory, "state.json"),
      },
      resolvePath: (value) => value,
      logger: { debug() {}, info() {}, warn() {}, error() {} },
      on(name, handler, options) { hooks.push({ name, handler, options }); },
    });
    assert.deepEqual(hooks.map((entry) => entry.name), ["before_dispatch", "message_sent"]);
    assert.equal(hooks.some((entry) => entry.name === "message_received"), false);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
