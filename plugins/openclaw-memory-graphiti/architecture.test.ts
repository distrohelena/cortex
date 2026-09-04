import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";

function source(name: string): string {
  return readFileSync(new URL(name, import.meta.url), "utf8");
}

describe("multichannel architecture boundaries", () => {
  test("common lifecycle, recall and Graphiti client do not import channel adapters", () => {
    for (const file of ["message-lifecycle.ts", "social-recall.ts", "graphiti.ts"]) {
      const text = source(file);
      expect(text).not.toMatch(/channel-adapters\/(?:whatsapp|telegram)/);
      expect(text).not.toMatch(/from ["'][^"']*(?:whatsapp|telegram)/i);
    }
  });

  test("adapter registry is the sole channel adapter selector", () => {
    const registry = source("channel-adapters/registry.ts");
    expect(registry).toContain('./whatsapp.js');
    expect(registry).toContain('./telegram.js');
    expect(source("index.ts")).toContain('getChannelAdapter');
    expect(source("index.ts")).not.toMatch(/channel-adapters\/(?:whatsapp|telegram)/);
  });

  test("identity core depends only on normalized references", () => {
    const identity = source("identity.ts");
    expect(identity).not.toContain("@g.us");
    expect(identity).not.toContain("s.whatsapp.net");
    expect(identity).not.toContain("telegram:user:");
  });
});
