import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { createScopedMessageIdRegistry } from "./state.js";

test("provider message IDs are scoped by channel conversation and persist", () => {
  const directory = mkdtempSync(join(tmpdir(), "filomena-policy-"));
  const filePath = join(directory, "state.json");
  try {
    const first = createScopedMessageIdRegistry({ filePath, maxEntries: 10 });
    first.record("telegram:-1001", "42");
    assert.equal(first.has("telegram:-1001", "42"), true);
    assert.equal(first.has("telegram:-1002", "42"), false);
    assert.equal(first.has("whatsapp:42@s.whatsapp.net", "42"), false);

    const restarted = createScopedMessageIdRegistry({ filePath, maxEntries: 10 });
    assert.equal(restarted.has("telegram:-1001", "42"), true);
    assert.equal(restarted.has("telegram:-1002", "42"), false);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
