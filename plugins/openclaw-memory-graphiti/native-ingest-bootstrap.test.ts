import { expect, test, vi } from "vitest";
import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
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
  const routes: unknown[] = []; const services: { stop(): void | Promise<void> }[] = [];
  try {
    const config = join(dir, "host.json");
    writeFileSync(config, JSON.stringify({ version: "native-memory-host:v1", storePath: join(dir, "memory.sqlite"), capabilityEnv: "TEST_CAPABILITY",
      bindings: [{ bindingRef: "test", conversation: getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: "test", conversationId: "123@g.us" }),
        notBefore: "2026-01-01T00:00:00Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] }] }));
    composeNativeMemoryIngest({ registerHttpRoute(r) { routes.push(r); }, registerService(s) { services.push(s); } },
      {} as MessageLifecycleRecorder, { OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE: config, TEST_CAPABILITY: "test-capability-with-at-least-32-bytes", OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES: "true" });
    expect(routes).toHaveLength(2); expect(services).toHaveLength(1);
  } finally { for (const s of services) await s.stop(); rmSync(dir, { recursive: true }); }
});

test("recovery has authenticated status but provisioning suppression never scans or writes", async () => {
  const dir = mkdtempSync(join(tmpdir(), "memory-recovery-bootstrap-"));
  const paths: string[] = []; const services: { start(): Promise<void>; stop(): void | Promise<void> }[] = [];
  try {
    const config = join(dir, "host.json");
    writeFileSync(config, JSON.stringify({ version: "native-memory-host:v1", storePath: join(dir, "memory.sqlite"), capabilityEnv: "TEST_CAPABILITY",
      recovery: { sourceDatabasePath: join(dir, "must-not-be-opened.sqlite"), storePath: join(dir, "recovery.sqlite"), intervalMs: 30000 },
      bindings: [{ bindingRef: "test", conversation: getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: "test", conversationId: "123@g.us" }),
        notBefore: "2026-09-25T00:00:00Z", historyNotBefore: "2026-01-01T00:00:00Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] }] }));
    composeNativeMemoryIngest({ registerHttpRoute(r) { paths.push(r.path); }, registerService(s) { services.push(s); } },
      { async submitInbound() { throw new Error("unexpected backend"); } } as unknown as MessageLifecycleRecorder,
      { OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE: config, TEST_CAPABILITY: "test-capability-with-at-least-32-bytes", OPENCLAW_GRAPHITI_SUPPRESS_EXTERNAL_STARTUP_PROBES: "true" });
    await services[0].start();
    expect(paths).toContain("/plugins/memory-graphiti/native-ingest/v1/recovery");
  } finally { for (const s of services) await s.stop(); rmSync(dir, { recursive: true }); }
});

test("host start and recurring passes recover history with no conversational receiver, and stop drains the worker", async () => {
  vi.useFakeTimers();
  const dir = mkdtempSync(join(tmpdir(), "memory-recurring-bootstrap-"));
  const services: { start(): Promise<void>; stop(): void | Promise<void> }[] = [];
  const source = new DatabaseSync(join(dir, "source.sqlite"));
  source.exec(`CREATE TABLE native_observation_records(scope TEXT,occurred_at INTEGER,sequence INTEGER);
    CREATE TABLE channel_ingress_events(channel_id TEXT,account_id TEXT,lane_key TEXT,payload_json TEXT);`);
  const account = createHash("sha256").update("test").digest("hex").slice(0, 24);
  const insert = (id: string) => source.prepare("INSERT INTO channel_ingress_events VALUES('whatsapp',?,'123@g.us',?)")
    .run(account, JSON.stringify({ message: { key: { id, remoteJid: "123@g.us", fromMe: false, participant: "15550000001@s.whatsapp.net" },
      messageTimestamp: 1790000000, message: { conversation: "Uma mensagem antiga." } } }));
  try {
    insert("m1");
    const config = join(dir, "host.json");
    writeFileSync(config, JSON.stringify({ version: "native-memory-host:v1", storePath: join(dir, "memory.sqlite"), capabilityEnv: "TEST_CAPABILITY",
      recovery: { sourceDatabasePath: join(dir, "source.sqlite"), storePath: join(dir, "recovery.sqlite"), intervalMs: 1000 },
      bindings: [{ bindingRef: "test", conversation: getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: "test", conversationId: "123@g.us" }),
        notBefore: "2026-12-01T00:00:00Z", historyNotBefore: "2026-01-01T00:00:00Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] }] }));
    composeNativeMemoryIngest({ registerHttpRoute() {}, registerService(s) { services.push(s); } },
      { async submitInbound() { return { state: "queued" }; }, async confirmInbound() { return "persisted"; } } as unknown as MessageLifecycleRecorder,
      { OPENCLAW_NATIVE_MEMORY_INGEST_HOST_FILE: config, TEST_CAPABILITY: "test-capability-with-at-least-32-bytes" });
    await services[0].start(); await vi.advanceTimersByTimeAsync(1);
    insert("m2"); await vi.advanceTimersByTimeAsync(1000);
    await services[0].stop(); insert("after-stop"); await vi.advanceTimersByTimeAsync(2000);
    const audit = new DatabaseSync(join(dir, "recovery.sqlite"), { readOnly: true });
    try { expect(audit.prepare("SELECT native_id,persisted FROM recovery_items ORDER BY ordinal").all())
      .toEqual([{ native_id: "m1", persisted: 1 }, { native_id: "m2", persisted: 1 }]); }
    finally { audit.close(); }
  } finally { for (const s of services) await s.stop(); source.close(); vi.useRealTimers(); rmSync(dir, { recursive: true }); }
});
