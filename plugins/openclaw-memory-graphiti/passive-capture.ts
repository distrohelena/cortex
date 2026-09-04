import { createHash } from "node:crypto";

import type { BoundEntityInput } from "./graphiti.js";

// Versioned constants: changing either value would intentionally fork identity.
export const FILOMENA_EPISODE_NAMESPACE_V1 = "b7897b2d-9c72-5ea9-8f1f-19b097cd37f8";
export const FILOMENA_PARTICIPANT_NAMESPACE_V1 = "cc5bf3bb-68cb-5e0e-a63e-cf8e76cf5f8b";

type PassiveMessageEvent = {
  content?: string;
  timestamp?: number;
  messageId?: string;
  senderId?: string;
  replyToId?: string;
  replyToIdFull?: string;
  replyToBody?: string;
  replyToSender?: string;
  replyToIsQuote?: boolean;
  providerUpdate?: Record<string, unknown>;
  metadata?: Record<string, unknown>;
};

type PassiveMessageContext = {
  channelId?: string;
  conversationId?: string;
  messageId?: string;
  senderId?: string;
};

export type NormalizedSenderId = {
  canonicalId: string;
  source: "sender_e164" | "sender_id";
  unresolvedAlternate: boolean;
};

export type PassiveCapture = {
  episodeUuid: string;
  episodeName: string;
  episodeBody: string;
  extractionBody: string;
  referenceTime: string;
  conversationJid: string;
  providerMessageId: string;
  sender: NormalizedSenderId;
  boundEntity: BoundEntityInput;
};

function parseUuid(uuid: string): Buffer {
  const hex = uuid.replaceAll("-", "");
  if (!/^[0-9a-f]{32}$/i.test(hex)) throw new Error(`Invalid UUID namespace: ${uuid}`);
  return Buffer.from(hex, "hex");
}

/** RFC 4122 UUIDv5 without adding a runtime dependency. */
export function uuidV5(namespace: string, key: string): string {
  const digest = createHash("sha1").update(parseUuid(namespace)).update(key, "utf8").digest();
  digest[6] = (digest[6] & 0x0f) | 0x50;
  digest[8] = (digest[8] & 0x3f) | 0x80;
  const hex = digest.subarray(0, 16).toString("hex");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function nonBlank(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function normalizeE164(value: string): string | undefined {
  const digits = value.replace(/\D/g, "");
  return /^\d{7,15}$/.test(digits) ? `${digits}@s.whatsapp.net` : undefined;
}

export function normalizeWhatsAppSenderId(
  senderId: string,
  senderE164?: string,
): NormalizedSenderId | undefined {
  const e164 = senderE164 ? normalizeE164(senderE164) : undefined;
  if (e164) {
    return { canonicalId: e164, source: "sender_e164", unresolvedAlternate: false };
  }

  let raw = senderId.trim().toLowerCase();
  raw = raw.replace(/^whatsapp:/, "");
  if (!raw) return undefined;

  if (/^\+?[\d\s().-]+$/.test(raw)) {
    const canonicalId = normalizeE164(raw);
    return canonicalId
      ? { canonicalId, source: "sender_id", unresolvedAlternate: false }
      : undefined;
  }

  const separator = raw.lastIndexOf("@");
  if (separator <= 0 || separator === raw.length - 1) return undefined;
  let local = raw.slice(0, separator);
  let domain = raw.slice(separator + 1);

  // Baileys/WhatsApp device JIDs qualify the local part as user:device.
  local = local.replace(/:\d+$/, "");
  if (domain === "c.us") domain = "s.whatsapp.net";
  if (!local || !/^[a-z0-9._-]+$/i.test(local) || !/^[a-z0-9.-]+$/i.test(domain)) return undefined;

  return {
    canonicalId: `${local}@${domain}`,
    source: "sender_id",
    // LID cannot be mapped to a PN JID without provider-supplied canonical E.164 metadata.
    unresolvedAlternate: domain === "lid",
  };
}

export function participantUuid(groupId: string, canonicalSenderId: string): string {
  const stableId = `whatsapp:${canonicalSenderId}`;
  return uuidV5(FILOMENA_PARTICIPANT_NAMESPACE_V1, `${groupId}\0${stableId}`);
}

function episodeUuid(channel: string, conversationJid: string, providerMessageId: string): string {
  return uuidV5(
    FILOMENA_EPISODE_NAMESPACE_V1,
    `${channel}\0${conversationJid}\0${providerMessageId}`,
  );
}

function timestampToIso(timestamp: number): string | undefined {
  if (!Number.isFinite(timestamp) || timestamp <= 0) return undefined;
  const milliseconds = timestamp < 1_000_000_000_000 ? timestamp * 1000 : timestamp;
  const date = new Date(milliseconds);
  return Number.isNaN(date.valueOf()) ? undefined : date.toISOString();
}

function normalizeConversationJid(value: string): string | undefined {
  const normalized = value.trim().toLowerCase().replace(/^whatsapp:/, "");
  return normalized || undefined;
}

export function buildPassiveCapture(input: {
  event: PassiveMessageEvent;
  context: PassiveMessageContext;
  groupId: string;
}): PassiveCapture | undefined {
  const { event, context, groupId } = input;
  const channel = nonBlank(context.channelId)?.toLowerCase();
  const conversationJid = context.conversationId
    ? normalizeConversationJid(context.conversationId)
    : undefined;
  const providerMessageId = nonBlank(event.messageId) ?? nonBlank(context.messageId);
  const rawSenderId = nonBlank(event.senderId) ?? nonBlank(context.senderId);
  const text = typeof event.content === "string" ? event.content : undefined;
  const referenceTime = typeof event.timestamp === "number" ? timestampToIso(event.timestamp) : undefined;
  if (!channel || channel !== "whatsapp" || !conversationJid || !providerMessageId || !rawSenderId || !text || !referenceTime) {
    return undefined;
  }

  const metadata = event.metadata ?? {};
  const displayName = nonBlank(metadata.senderName) ?? nonBlank(metadata.senderUsername);
  const senderE164 = nonBlank(metadata.senderE164);
  const sender = normalizeWhatsAppSenderId(rawSenderId, senderE164);
  if (!sender) return undefined;

  const stableId = `whatsapp:${sender.canonicalId}`;
  const canonicalUuid = participantUuid(groupId, sender.canonicalId);
  const id = episodeUuid(channel, conversationJid, providerMessageId);
  const aliases = displayName ? [displayName] : [];
  const boundEntity: BoundEntityInput = {
    uuid: canonicalUuid,
    role: "speaker",
    name: stableId,
    entity_type: "Person",
    labels: ["Person"],
    attributes: {
      stable_id: stableId,
      display_name_current: displayName ?? "",
      aliases,
      identity_provider: "whatsapp",
      identity_source: sender.source,
    },
  };

  const reply = {
    id: nonBlank(event.replyToId),
    id_full: nonBlank(event.replyToIdFull),
    body: typeof event.replyToBody === "string" ? event.replyToBody : undefined,
    sender: nonBlank(event.replyToSender),
    is_quote: typeof event.replyToIsQuote === "boolean" ? event.replyToIsQuote : undefined,
  };
  const hasReply = Object.values(reply).some((value) => value !== undefined);

  const episodeBody = JSON.stringify({
    schema: "filomena.openclaw.inbound.v1",
    channel,
    conversation_jid: conversationJid,
    provider_message_id: providerMessageId,
    sender: {
      raw_id: rawSenderId,
      canonical_id: sender.canonicalId,
      stable_id: stableId,
      display_name: displayName ?? null,
      e164: senderE164 ?? null,
      identity_source: sender.source,
      unresolved_alternate: sender.unresolvedAlternate,
    },
    timestamp: { original: event.timestamp, iso: referenceTime },
    reply: hasReply ? reply : null,
    provider_update: event.providerUpdate ?? null,
    text,
  });

  return {
    episodeUuid: id,
    episodeName: `inbound_${id}`,
    episodeBody,
    extractionBody: text,
    referenceTime,
    conversationJid,
    providerMessageId,
    sender,
    boundEntity,
  };
}
