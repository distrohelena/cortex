import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { isAbsolute, join } from "node:path";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import type { NormalizedInboundMessage } from "./channel-adapters/types.js";
import type { NativeMemoryBinding } from "./native-ingest.js";
import type { MemoryHistoryPage } from "./native-memory-recovery.js";
import { ingestDigest } from "./native-ingest-store.js";

const hash = (value: string) => createHash("sha256").update(value).digest("hex");
const object = (value: unknown): Record<string, unknown> => value && typeof value === "object" && !Array.isArray(value)
  ? value as Record<string, unknown> : {};
function resolveHistoryActor(raw: string, binding: NativeMemoryBinding) {
  const adapter = getChannelAdapter("whatsapp")!;
  let actor = adapter.normalizeActor({ accountId: binding.conversation.accountId, nativeId: raw });
  if (!actor?.unresolvedAlternate || !binding.historyIdentityDirectory) return actor;
  const lid = /^(\d+)@lid$/.exec(actor.nativeId)?.[1];
  if (!lid || !isAbsolute(binding.historyIdentityDirectory)) throw new Error("history_identity_mapping_invalid");
  try {
    const pn: unknown = JSON.parse(readFileSync(join(binding.historyIdentityDirectory, `lid-mapping-${lid}_reverse.json`), "utf8"));
    if (typeof pn !== "string" || !/^\d{7,15}$/.test(pn)) throw new Error("history_identity_mapping_invalid");
    const reverse: unknown = JSON.parse(readFileSync(join(binding.historyIdentityDirectory, `lid-mapping-${pn}.json`), "utf8"));
    if (reverse !== lid) throw new Error("history_identity_mapping_invalid");
    actor = adapter.normalizeActor({ accountId: binding.conversation.accountId, nativeId: `${pn}@s.whatsapp.net` });
    return actor;
  } catch (e) {
    if (e && typeof e === "object" && "code" in e && e.code === "ENOENT") return undefined;
    throw new Error("history_identity_mapping_invalid");
  }
}
function body(value: unknown): { text?: string; context: Record<string, unknown> } {
  let m = object(value);
  for (let depth = 0; depth < 4 && m.ephemeralMessage; depth++) m = object(object(m.ephemeralMessage).message);
  // View-once, audio and binary media are not silently converted to invented text.
  const part = object(m.extendedTextMessage ?? m.imageMessage ?? m.videoMessage);
  const text = m.conversation ?? part.text ?? part.caption;
  return { text: typeof text === "string" ? text : undefined, context: object(part.contextInfo) };
}

/** Read-only adapter for the existing retained native stores. Never claims full WhatsApp history. */
export async function readNativeMemoryHistory(file: string, binding: NativeMemoryBinding): Promise<MemoryHistoryPage> {
  const c = binding.conversation;
  if (c.channel !== "whatsapp" || c.kind !== "group" || !/^\d+(?:-\d+)?@g\.us$/.test(c.nativeId))
    throw new Error("history_binding_invalid");
  const account = hash(c.accountId).slice(0, 24);
  const scope = hash(JSON.stringify(["observation", JSON.stringify(["whatsapp", account]), c.nativeId]));
  const db = new DatabaseSync(file, { readOnly: true });
  const gaps = new Set<string>(["available_local_history_only"]);
  const messages = new Map<string, NormalizedInboundMessage>();
  let skippedSelf = 0, unsupported = 0;
  const consume = (payload: string) => {
    const p = object(JSON.parse(payload));
    if (!Object.keys(p).length) { gaps.add("retained_payload_missing"); return; }
    const m = object(p.message), key = object(m.key), identity = object(p.observationIdentity);
    if (key.remoteJid !== c.nativeId || typeof key.id !== "string" || !key.id || typeof key.fromMe !== "boolean")
      throw new Error("history_binding_invalid");
    if (key.fromMe) { skippedSelf++; return; }
    let nativeActor = identity.actorId;
    if (nativeActor === undefined) {
      nativeActor = [key.participant, key.participantAlt].find(v => typeof v === "string" && /^\d+(?::\d+)?@s\.whatsapp\.net$/.test(v))
        ?? key.participant;
    }
    const actor = typeof nativeActor === "string" ? resolveHistoryActor(nativeActor, binding) : undefined;
    if (!actor || actor.unresolvedAlternate || !/^\d+@s\.whatsapp\.net$/.test(actor.nativeId)) {
      unsupported++; gaps.add("actor_unresolved"); return;
    }
    if (binding.selfActorIds.includes(actor.stableId)) { skippedSelf++; return; }
    const content = body(m.message);
    if (!content.text?.trim()) { unsupported++; gaps.add("non_text_not_ingested"); return; }
    const rawTimestamp = m.messageTimestamp;
    const seconds = typeof rawTimestamp === "number" ? rawTimestamp
      : typeof rawTimestamp === "string" && /^\d+$/.test(rawTimestamp) ? Number(rawTimestamp) : NaN;
    const timestamp = seconds * 1000;
    if (!Number.isSafeInteger(timestamp) || !Number.isFinite(new Date(timestamp).getTime())) {
      unsupported++; gaps.add("provider_timestamp_unavailable"); return;
    }
    if (Object.keys(identity).length && (identity.nativeMessageId !== key.id || identity.fromMe !== false
      || identity.occurredAt !== timestamp)) throw new Error("history_integrity_failure");
    const message: NormalizedInboundMessage = {
      direction: "inbound", providerLabel: "WhatsApp", providerMessageId: key.id, conversation: c,
      content: content.text, referenceTime: new Date(timestamp), originalTimestamp: timestamp, timestampSource: "provider",
      sender: { channel: "whatsapp", accountId: c.accountId, nativeId: actor.nativeId, stableId: actor.stableId,
        actorKind: "person", bindAsPerson: true, identitySource: "trusted_native_observation", unresolvedAlternate: false },
    };
    if (typeof content.context.stanzaId === "string" && content.context.stanzaId) {
      const quote = body(content.context.quotedMessage).text;
      message.replyTo = { providerMessageId: content.context.stanzaId, isQuote: true, ...(quote ? { content: quote } : {}) };
    }
    const previous = messages.get(key.id);
    if (previous && ingestDigest(previous) !== ingestDigest(message)) throw new Error("history_identity_conflict");
    messages.set(key.id, message);
  };
  try {
    db.exec("BEGIN");
    const rows = db.prepare("SELECT * FROM native_observation_records WHERE scope=? ORDER BY occurred_at,sequence LIMIT 50001").all(scope);
    if (rows.length > 50000) throw new Error("history_scan_limit_reached");
    for (const r of rows) {
      const projection = { epoch: r.epoch, sequence: r.sequence, scope: r.scope, native_id: r.native_id,
        actor_ref: r.actor_ref, self_ref: r.self_ref, from_me: r.from_me, occurred_at: r.occurred_at, payload_json: r.payload_json };
      const expected = hash(JSON.stringify([JSON.stringify(r.membership_lease_ref == null ? projection
        : { ...projection, membership_lease_ref: r.membership_lease_ref })]));
      if (r.digest !== expected) throw new Error("history_integrity_failure");
      consume(String(r.payload_json));
    }
    const ingress = db.prepare(`SELECT payload_json FROM channel_ingress_events
      WHERE channel_id='whatsapp' AND account_id=? AND lane_key=? LIMIT 50001`).all(account, c.nativeId);
    if (ingress.length > 50000) throw new Error("history_scan_limit_reached");
    for (const r of ingress) consume(String(r.payload_json ?? "null"));
    db.exec("COMMIT");
    return { messages: [...messages.values()], gaps: [...gaps], skippedSelf, unsupported };
  } finally { db.close(); }
}
