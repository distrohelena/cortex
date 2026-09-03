import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";

describe("OpenClaw plugin manifest", () => {
  test("declares every registered agent tool", () => {
    const manifest = JSON.parse(
      readFileSync(new URL("./openclaw.plugin.json", import.meta.url), "utf8"),
    );

    expect(manifest.contracts?.tools).toEqual([
      "memory_recall",
      "memory_store",
      "memory_forget",
      "memory_status",
    ]);
  });
});
