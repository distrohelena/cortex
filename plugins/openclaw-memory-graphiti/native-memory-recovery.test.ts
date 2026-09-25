import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, expect, test } from "vitest";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import { NativeIngestService, type NativeMemoryBinding } from "./native-ingest.js";
import { NativeIngestStore } from "./native-ingest-store.js";
import { NativeMemoryRecovery, type MemoryHistoryPage } from "./native-memory-recovery.js";

const cleanups: (() => void)[] = [];
afterEach(() => cleanups.splice(0).reverse().forEach(f => f()));
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), "memory-recovery-"));
  cleanups.push(() => rmSync(dir, { recursive: true }));
  const store = new NativeIngestStore(join(dir, "ingest.sqlite")); cleanups.push(() => store.close());
  const message = getChannelAdapter("whatsapp")!.normalizeInbound({ messageId: "m1", senderId: "15550000001@s.whatsapp.net",
    timestamp: 1_790_000_000_000, content: "Um fato do passado." }, { accountId: "test", conversationId: "123@g.us" })!;
  const binding: NativeMemoryBinding = { bindingRef: "group-a", conversation: message.conversation,
    selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"], notBefore: "2026-12-01T00:00:00Z", historyNotBefore: "2026-01-01T00:00:00Z" };
  const backend = { persisted: false, calls: [] as string[] };
  const service = new NativeIngestService(store, [binding], {
    async submitInbound(m) { backend.calls.push(m.providerMessageId); return { state: "queued" }; },
    async confirmInbound() { return backend.persisted ? "persisted" : "pending"; },
  });
  const page: MemoryHistoryPage = { messages: [message], gaps: ["available_local_history_only"], skippedSelf: 0, unsupported: 0 };
  const file = join(dir, "recovery.sqlite");
  const open = () => { const r = new NativeMemoryRecovery(file, [binding], service, async () => page); cleanups.push(() => r.close()); return r; };
  return { open, backend, page, binding, message };
}

test("queued is not checkpointed; restart resumes without re-submission and only confirmed persistence advances", async () => {
  const f = fixture(); let r = f.open();
  await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ discovered: 1, persisted: 0, cursor: 0, pending: 1 });
  expect(f.backend.calls).toEqual(["m1"]);
  r.close(); r = f.open(); f.backend.persisted = true;
  await r.runOnce(); await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ discovered: 1, persisted: 1, cursor: 1, pending: 0 });
  expect(f.backend.calls).toEqual(["m1"]);
});

test("late arrival older than last message is discovered, not skipped by timestamp high-water mark", async () => {
  const f = fixture(); const r = f.open(); f.backend.persisted = true;
  await r.runOnce();
  f.page.messages.push({ ...f.message, providerMessageId: "late", referenceTime: new Date(1_780_000_000_000), originalTimestamp: 1_780_000_000_000 });
  await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ persisted: 2, cursor: 2 });
  expect(f.backend.calls).toEqual(["m1", "late"]);
});

test("wrong namespace never reaches writer or checkpoint", async () => {
  const f = fixture(); f.page.messages[0] = { ...f.message, conversation: { ...f.message.conversation, namespace: "foreign" } };
  const r = f.open(); await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ cursor: 0, diagnostic: "history_binding_invalid" });
  expect(f.backend.calls).toEqual([]);
});

test("conflicting provider id is not re-ingested and remains visibly failed", async () => {
  const f = fixture(); const r = f.open(); await r.runOnce();
  f.page.messages[0] = { ...f.message, content: "Changed content" };
  await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ cursor: 0, diagnostic: "history_identity_conflict" });
  expect(f.backend.calls).toEqual(["m1"]);
});

test("empty local history does not prove a complete offline interval", async () => {
  const f = fixture(); f.page.messages = []; const r = f.open(); await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ cursor: 0, gaps: ["available_local_history_only"], coverage: "partial" });
});

test("source outage does not turn into an empty synced history", async () => {
  const f = fixture(); const page = f.page;
  Object.defineProperty(page, "messages", { get() { throw new Error("disk unavailable"); } });
  const r = f.open(); await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ cursor: 0, diagnostic: "history_source_unavailable", coverage: "partial" });
});

test("a failed old item does not starve later available history or advance the contiguous cursor", async () => {
  const f = fixture(); const r = f.open();
  f.page.messages = Array.from({ length: 20 }, (_, i) => ({ ...f.message, providerMessageId: `m${String(i).padStart(2, "0")}` }));
  await r.runOnce(); await r.runOnce();
  expect(new Set(f.backend.calls).size).toBe(20);
  expect(r.status("group-a")).toMatchObject({ cursor: 0, persisted: 0, pending: 20 });
});

test("recurring reconciliation drains already discovered work even if the source later goes offline", async () => {
  const f = fixture(); const r = f.open(); await r.runOnce();
  Object.defineProperty(f.page, "messages", { get() { throw new Error("offline"); } });
  f.backend.persisted = true; await r.runOnce();
  expect(r.status("group-a")).toMatchObject({ persisted: 1, cursor: 1, diagnostic: "history_source_unavailable" });
  expect(f.backend.calls).toEqual(["m1"]);
});

test("checkpoint cannot be reused under a different account, namespace or history authority", async () => {
  const f = fixture(); const r = f.open(); await r.runOnce(); r.close();
  f.binding.historyNotBefore = "2025-01-01T00:00:00Z";
  expect(() => f.open()).toThrow("history_authority_changed");
  expect(f.backend.calls).toEqual(["m1"]);
});
