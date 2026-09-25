import { expect, test } from "vitest";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { composeNativeMemoryIngest } from "./native-ingest-bootstrap.js";
import type { MessageLifecycleRecorder } from "./message-lifecycle.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";

test("startup has no ingest routes or backend calls without host opt-in", () => {
  composeNativeMemoryIngest({ registerHttpRoute() { throw new Error("unexpected route"); }, registerService() { throw new Error("unexpected service"); } },
    {} as MessageLifecycleRecorder, {});
});
test("explicit host config registers local routes without contacting either backend", async () => {
  const dir = mkdtempSync(join(tmpdir(), "memory-bootstrap-"));
  const routes: unknown[] = []; const services: { stop(): void }[] = [];
  try {
    const config = join(dir, "host.json");
    writeFileSync(config, JSON.stringify({ version: "native-memory-host:v1", storePath: join(dir, "memory.sqlite"), capabilityEnv: "TEST_CAPABILITY",
      bindings: [{ bindingRef: "test", conversation: getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: "test", conversationId: "123@g.us" }),
        notBefore: "2026-01-01T00:00:00Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] }] }));
    composeNativeMemoryIngest({ registerHttpRoute(r) { routes.push(r); }, registerService(s) { services.push(s); } },
      {} as MessageLifecycleRecorder, { OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE: config, TEST_CAPABILITY: "test-capability-with-at-least-32-bytes", OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES: "true" });
    expect(routes).toHaveLength(2); expect(services).toHaveLength(1);
  } finally { services.forEach(s => s.stop()); rmSync(dir, { recursive: true }); }
});
