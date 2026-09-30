import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { spawnSync } from "node:child_process";
import { afterEach, expect, test } from "vitest";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import { NativeIngestStore, ingestDigest } from "./native-ingest-store.js";
import { NativeIngestService, type NativeMemoryBinding } from "./native-ingest.js";
import { NativeIngestAuthorizationError, buildInboundCapture } from "./message-lifecycle.js";
import { GraphitiRequestError } from "./graphiti.js";

test("durable failure evidence survives reconciliation without secrets or automatic retry", async () => {
  const f = fixture(); let submissions = 0;
  const service = new NativeIngestService(f.store, [f.binding], {
    async submitInbound() {
      submissions++;
      throw new GraphitiRequestError("http_rejection", "private-token private-message", 400);
    },
    async confirmInbound() { throw new Error("private-confirmation-data"); },
  });
  const receipt = await service.submit(f.request);
  expect(receipt).toMatchObject({ retryDisposition: "reconcile",
    submissionFailure: { category: "http_rejection", httpStatus: 400 } });
  await service.reconcile(f.binding.bindingRef, receipt.requestRef);
  f.store.close(); const reopened = new NativeIngestStore(f.file); stores.push(reopened);
  const saved = reopened.get(receipt.requestRef)!.receipt;
  expect(saved).toMatchObject({ submissionFailure: { category: "http_rejection", httpStatus: 400 },
    confirmationFailure: { category: "unclassified" }, retryDisposition: "reconcile" });
  expect(JSON.stringify(saved)).not.toContain("private-");
  const resumed = new NativeIngestService(reopened, [f.binding], {
    async submitInbound() { submissions++; return { state: "queued" }; },
    async confirmInbound() { return "pending"; },
  });
  await resumed.submit(f.request);
  expect(submissions).toBe(1);
});

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

test("historical opt-in admits old messages without weakening the live boundary", async () => {
  const f = fixture();
  const binding = { ...f.binding, notBefore: "2026-12-01T00:00:00Z", historyNotBefore: "2026-01-01T00:00:00Z" };
  const service = new NativeIngestService(f.store, [binding], {
    async submitInbound(m) { f.submitted.push(m.providerMessageId); return { state: "queued" }; },
    async confirmInbound() { return "persisted"; },
  });
  await expect(service.submit(f.request)).rejects.toThrow("message_invalid");
  const receipt = await service.submitHistorical(f.request);
  expect(receipt.state).toBe("queued");
  expect((await service.reconcile(binding.bindingRef, receipt.requestRef)).state).toBe("persisted");
  expect(f.submitted).toEqual(["synthetic-1"]);
});

test("history replay requires separate host opt-in even for a live-valid request", async () => {
  const f = fixture();
  await expect(f.service.submitHistorical(f.request)).rejects.toThrow("history_authority_required");
  expect(f.submitted).toEqual([]);
});
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

test("two store connections cannot claim the same work twice", async () => {
  const f = fixture(); const other = new NativeIngestStore(f.file); stores.push(other);
  const service = new NativeIngestService(other, [f.binding], {
    async submitInbound(m) { f.submitted.push(m.content); return { state: "queued" }; }, async confirmInbound() { return "pending"; },
  });
  await Promise.all([f.service.submit(f.request), service.submit(f.request)]);
  expect(f.submitted).toHaveLength(1);
});

test("persisted receipt cannot be downgraded by a slower submission", async () => {
  const f = fixture(); const r = await f.service.submit(f.request);
  f.store.save({ ...r, state: "persisted", retryDisposition: "none" });
  f.store.save({ ...r, state: "queued", retryDisposition: "reconcile" });
  expect((await f.service.getStatus("test-binding", r.requestRef)).state).toBe("persisted");
});

test("nonboolean actor authority is invalid", async () => {
  const f = fixture(); (f.request.message.sender as unknown as { bindAsPerson: unknown }).bindAsPerson = "true";
  await expect(f.service.submit(f.request)).rejects.toThrow("actor_invalid");
});

test("authorization outage before backend attempt is safely retryable", async () => {
  const f = fixture(); let available = false; let writes = 0;
  const service = new NativeIngestService(f.store, [f.binding], {
    async submitInbound() { if (!available) throw new NativeIngestAuthorizationError(); writes++; return { state: "queued" }; },
    async confirmInbound() { return "pending"; },
  });
  expect(await service.submit(f.request)).toMatchObject({ state: "failed", retryDisposition: "safe" });
  available = true;
  expect(await service.submit(f.request)).toMatchObject({ state: "queued" });
  expect(writes).toBe(1);
});

test("crash between safe failure receipt and claim release does not strand work", async () => {
  const f = fixture(); const r = await f.service.submit(f.request);
  // Durable state at the interrupted safe-failure boundary; no release step has run.
  f.store.save({ ...r, state: "failed", retryDisposition: "safe" });
  expect((await f.service.submit(f.request)).state).toBe("queued");
  expect(f.submitted).toHaveLength(2);
});

test("process exit after durable claim preserves uncertain work without backend resubmission", async () => {
  const f = fixture(); const capture = buildInboundCapture(f.request.message);
  const requestRef = ingestDigest(["test-account", capture.episodeUuid]);
  f.store.stage({ bindingRef: f.binding.bindingRef, digest: ingestDigest(f.request.message), message: f.request.message,
    receipt: { version: "native-memory-ingest:v1", requestRef, episodeUuid: capture.episodeUuid, state: "pending", retryDisposition: "safe" } });
  f.store.close();
  const source = `import { NativeIngestStore } from ${JSON.stringify(pathToFileURL(resolve("native-ingest-store.ts")).href)};
    const store = new NativeIngestStore(${JSON.stringify(f.file)});
    if (!store.claim(${JSON.stringify(requestRef)})) process.exit(2);
    process.exit(0);`; // Deliberately no graceful close/receipt; SQLite commit must survive.
  const child = spawnSync(process.execPath, ["--input-type=module", "-e", source], { encoding: "utf8", windowsHide: true });
  expect(child.status, child.stderr).toBe(0);
  const reopened = new NativeIngestStore(f.file); stores.push(reopened);
  const service = new NativeIngestService(reopened, [f.binding], {
    async submitInbound() { throw new Error("must not resubmit"); }, async confirmInbound() { return "pending"; },
  });
  expect(await service.submit(f.request)).toMatchObject({ state: "pending", retryDisposition: "reconcile" });
  expect(f.submitted).toHaveLength(0);
});
