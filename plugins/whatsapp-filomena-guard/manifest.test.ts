import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("manifest selects the hook-only guard during gateway startup", async () => {
  const manifestUrl = new URL("./openclaw.plugin.json", import.meta.url);
  const manifest = JSON.parse(await readFile(manifestUrl, "utf8")) as {
    activation?: { onCapabilities?: string[] };
  };

  assert.deepEqual(manifest.activation?.onCapabilities, ["hook"]);
});
