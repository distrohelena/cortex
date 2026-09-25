import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, expect, test } from "vitest";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import { NativeIngestStore } from "./native-ingest-store.js";
import { NativeIngestService, type NativeMemoryBinding } from "./native-ingest.js";

const dirs: string[] = [];
const stores: NativeIngestStore[] = [];
afterEach(() => { stores.splice(0).forEach(s => s.close()); dirs.splice(0).forEach(d => rmSync(d, { recursive: true })); });
function message(accountId = "test-account") {
  return getChannelAdapter("whatsapp")!.normalizeInbound({
    messageId: "synthetic-1", senderId: "15550000001@s.whatsapp.net", content: "Gosto de café.",
    timestamp: 1_790_000_000_000,
  }, { accountId, conversationId: "12345@g.us" })!;
}
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), "native-memory-test-")); dirs.push(dir);
  const file = join(dir, "receipts.sqlite");
  const store = new NativeIngestStore(file); stores.push(store);
  const binding: NativeMemoryBinding = { bindingRef: "test-binding", conversation: message().conversation,
    notBefore: "2026-01-01T00:00:00.000Z", selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"] };
  const submitted: string[] = [];
  const service = new NativeIngestService(store, [binding], {
    async submitInbound(m) { submitted.push(m.content); return { state: "queued" as const }; },
    async confirmInbound() { return "pending" as const; },
  });
  return { file, store, binding, service, submitted, request: { version: "native-memory-ingest:v1" as const, bindingRef: binding.bindingRef, message: message() } };
}
test("wrong binding never reaches recorder", async () => {
  const f = fixture(); await expect(f.service.submit({ ...f.request, bindingRef: "foreign" })).rejects.toThrow("binding");
  expect(f.submitted).toEqual([]);
});
test.each(["account", "namespace", "kind", "actor", "self", "date", "version"])("rejects untrusted %s before submission", async kind => {
  const f = fixture(); const r = f.request;
  if (kind === "account") r.message.conversation.accountId = "other";
  if (kind === "namespace") r.message.conversation.namespace = "foreign";
  if (kind === "kind") r.message.conversation.kind = "dm";
  if (kind === "actor") r.message.sender!.unresolvedAlternate = true;
  if (kind === "self") r.message.sender!.stableId = f.binding.selfActorIds[0];
  if (kind === "date") r.message.referenceTime = new Date("2025-01-01");
  if (kind === "version") (r as { version: string }).version = "future";
  await expect(f.service.submit(r)).rejects.toThrow(); expect(f.submitted).toEqual([]);
});
test("duplicate valid native identity reuses receipt across concurrent callers", async () => {
  const f = fixture(); const receipts = await Promise.all([f.service.submit(f.request), f.service.submit(f.request)]);
  expect(receipts[0].requestRef).toBe(receipts[1].requestRef);
  expect(f.submitted).toEqual(["Gosto de café."]);
  expect((await f.service.getStatus("test-binding", receipts[0].requestRef)).state).toBe("queued");
});
test("same id with changed content is rejected without overwriting original", async () => {
  const f = fixture(); const receipt = await f.service.submit(f.request);
  await expect(f.service.submit({ ...f.request, message: { ...f.request.message, content: "different" } })).rejects.toThrow("conflict");
  expect(f.store.get(receipt.requestRef)?.message.content).toBe("Gosto de café.");
  expect(f.submitted).toHaveLength(1);
});
test("account collision in legacy episode uuid is rejected", async () => {
  const f = fixture(); await f.service.submit(f.request);
  const other = new NativeIngestService(f.store, [{ ...f.binding, bindingRef: "other", conversation: message("other").conversation }], {
    async submitInbound() { throw new Error("must not submit"); }, async confirmInbound() { return "pending"; },
  });
  await expect(other.submit({ ...f.request, bindingRef: "other", message: message("other") })).rejects.toThrow("conflict");
});
test("reopen keeps uncertain work and cannot resubmit automatically", async () => {
  const f = fixture();
  const failing = new NativeIngestService(f.store, [f.binding], {
    async submitInbound() { throw new Error("remote connection lost after acceptance"); }, async confirmInbound() { return "pending"; },
  });
  const receipt = await failing.submit(f.request);
  expect(receipt).toMatchObject({ state: "failed", retryDisposition: "reconcile" });
  f.store.close(); const reopened = new NativeIngestStore(f.file); stores.push(reopened);
  const resumed = new NativeIngestService(reopened, [f.binding], {
    async submitInbound() { throw new Error("must not retry"); }, async confirmInbound() { return "pending"; },
  });
  expect((await resumed.submit(f.request)).state).toBe("failed");
  expect((await resumed.reconcile("test-binding", receipt.requestRef)).state).not.toBe("persisted");
});
test("wrong scope cannot read status", async () => {
  const f = fixture(); const receipt = await f.service.submit(f.request);
  await expect(f.service.getStatus("wrong", receipt.requestRef)).rejects.toThrow("binding");
});
