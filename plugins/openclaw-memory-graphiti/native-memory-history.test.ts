import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, expect, test } from "vitest";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import { readNativeMemoryHistory } from "./native-memory-history.js";
import type { NativeMemoryBinding } from "./native-ingest.js";

const cleanup: (() => void)[] = [];
afterEach(() => cleanup.splice(0).reverse().forEach(f => f()));
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), "history-source-")); cleanup.push(() => rmSync(dir, { recursive: true }));
  const file = join(dir, "native.sqlite"); const db = new DatabaseSync(file); cleanup.push(() => db.close());
  db.exec(`CREATE TABLE channel_ingress_events(channel_id TEXT,account_id TEXT,lane_key TEXT,payload_json TEXT);
    CREATE TABLE native_observation_records(epoch TEXT,sequence INTEGER,scope TEXT,native_id TEXT,actor_ref TEXT,self_ref TEXT,
      from_me INTEGER,occurred_at INTEGER,payload_json TEXT,digest TEXT,membership_lease_ref TEXT);`);
  const binding: NativeMemoryBinding = { bindingRef: "g", conversation: getChannelAdapter("whatsapp")!.normalizeConversation({
    accountId: "test", conversationId: "123@g.us" })!, selfActorIds: ["whatsapp:15550000002@s.whatsapp.net"],
    notBefore: "2026-12-01T00:00:00Z", historyNotBefore: "2026-01-01T00:00:00Z" };
  const payload = { message: { key: { id: "old-message", remoteJid: "123@g.us", participant: "15550000001@s.whatsapp.net", fromMe: false },
    messageTimestamp: 1_790_000_000, message: { conversation: "Histórico disponível." } }, receivedAt: 1_790_000_100_000 };
  const account = createHash("sha256").update("test").digest("hex").slice(0, 24);
  const add = (value: unknown, group = "123@g.us", a = account) => db.prepare("INSERT INTO channel_ingress_events VALUES('whatsapp',?,?,?)")
    .run(a, group, JSON.stringify(value));
  return { dir, file, db, binding, payload, add, account };
}

test("reads exact group/account retained backlog without modifying or consuming queue", async () => {
  const f = fixture(); f.add(f.payload); f.add(f.payload, "other@g.us"); f.add(f.payload, "123@g.us", "foreign");
  const page = await readNativeMemoryHistory(f.file, f.binding);
  expect(page.messages).toHaveLength(1);
  expect(page.messages[0]).toMatchObject({ providerMessageId: "old-message", content: "Histórico disponível.",
    originalTimestamp: 1_790_000_000_000, sender: { stableId: "whatsapp:15550000001@s.whatsapp.net" } });
  expect(f.db.prepare("SELECT count(*) AS n FROM channel_ingress_events").get()?.n).toBe(3);
  expect(page.gaps).toContain("available_local_history_only");
});

test("deleted payload and unresolved actor are reported rather than silently considered recovered", async () => {
  const f = fixture(); f.add(null); f.payload.message.key.participant = "123@lid"; f.add(f.payload);
  const page = await readNativeMemoryHistory(f.file, f.binding);
  expect(page.messages).toEqual([]);
  expect(page.gaps).toEqual(expect.arrayContaining(["retained_payload_missing", "actor_unresolved"]));
});

test("self cannot become human memory and quote is provenance, not the current speaker's fact", async () => {
  const f = fixture(); f.add({ ...f.payload, message: { ...f.payload.message, key: { ...f.payload.message.key, fromMe: true } } });
  f.add({ ...f.payload, message: { ...f.payload.message, message: { extendedTextMessage: { text: "Ele disse isso.",
    contextInfo: { stanzaId: "quoted", quotedMessage: { conversation: "Eu sou astronauta." } } } } } });
  const page = await readNativeMemoryHistory(f.file, f.binding);
  expect(page.skippedSelf).toBe(1);
  expect(page.messages[0]).toMatchObject({ content: "Ele disse isso.", replyTo: { providerMessageId: "quoted", content: "Eu sou astronauta." } });
});

test("corrupt journal cannot be passed off as verified history", async () => {
  const f = fixture();
  const scope = createHash("sha256").update(JSON.stringify(["observation", JSON.stringify(["whatsapp", f.account]), "123@g.us"])).digest("hex");
  f.db.prepare("INSERT INTO native_observation_records VALUES('epoch',1,?,'old-message','actor','self',0,1790000000000,?,'corrupt',NULL)")
    .run(scope, JSON.stringify(f.payload));
  await expect(readNativeMemoryHistory(f.file, f.binding)).rejects.toThrow("history_integrity_failure");
});

test("LID uses only the host-selected reciprocal account mapping, never a display name", async () => {
  const f = fixture(); f.binding.historyIdentityDirectory = f.dir;
  f.payload.message.key.participant = "123@lid"; f.add(f.payload);
  writeFileSync(join(f.dir, "lid-mapping-123_reverse.json"), JSON.stringify("15550000001"));
  writeFileSync(join(f.dir, "lid-mapping-15550000001.json"), JSON.stringify("123"));
  const page = await readNativeMemoryHistory(f.file, f.binding);
  expect(page.messages[0]?.sender?.stableId).toBe("whatsapp:15550000001@s.whatsapp.net");
  writeFileSync(join(f.dir, "lid-mapping-15550000001.json"), JSON.stringify("456"));
  await expect(readNativeMemoryHistory(f.file, f.binding)).rejects.toThrow("history_identity_mapping_invalid");
});

test.skipIf(!process.env.NATIVE_MEMORY_HISTORY_AUDIT_HOST)("opt-in private source audit is read-only and emits only counts", async () => {
  const host = JSON.parse(readFileSync(process.env.NATIVE_MEMORY_HISTORY_AUDIT_HOST!, "utf8"));
  const conversation = getChannelAdapter("whatsapp")!.normalizeConversation({ accountId: host.accountId, conversationId: host.nativeSession });
  expect(conversation?.kind).toBe("group");
  const page = await readNativeMemoryHistory(join(host.stateDir, "state", "openclaw.sqlite"), {
    bindingRef: "private-audit", conversation: conversation!, selfActorIds: [],
    notBefore: new Date().toISOString(), historyNotBefore: "1970-01-01T00:00:00Z",
    historyIdentityDirectory: process.env.NATIVE_MEMORY_HISTORY_IDENTITY_DIRECTORY,
  });
  console.log(JSON.stringify({ audit: "read_only", messages: page.messages.length, skippedSelf: page.skippedSelf,
    unsupported: page.unsupported, gaps: page.gaps }));
  expect(page.gaps).toContain("available_local_history_only");
});
