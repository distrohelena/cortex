import { buildConversationRef, legacyNamespaceSegment } from "../conversation-identity.js";
import { nonBlank, normalizedReply, recordValue, safeProvenance, timestampDate } from "./hook-normalization.js";
import type { ActorInput, ActorRef, ChannelMemoryAdapter, ConversationInput, ConversationKind, NormalizedInboundMessage, NormalizedOutboundMessage, NormalizedTurnOrigin } from "./types.js";

function normalizeE164(value: string): string | undefined {
  const digits = value.replace(/\D/g, "");
  return /^\d{7,15}$/.test(digits) ? `${digits}@s.whatsapp.net` : undefined;
}

export type NormalizedWhatsAppId = {
  canonicalId: string;
  source: "sender_e164" | "sender_id";
  unresolvedAlternate: boolean;
};

export function normalizeWhatsAppId(senderId: string, senderE164?: string): NormalizedWhatsAppId | undefined {
  const e164 = senderE164 ? normalizeE164(senderE164) : undefined;
  if (e164) return { canonicalId: e164, source: "sender_e164", unresolvedAlternate: false };
  const raw = senderId.trim().toLowerCase().replace(/^whatsapp:/, "");
  if (!raw) return undefined;
  if (/^\+?[\d\s().-]+$/.test(raw)) {
    const canonicalId = normalizeE164(raw);
    return canonicalId ? { canonicalId, source: "sender_id", unresolvedAlternate: false } : undefined;
  }
  const separator = raw.lastIndexOf("@");
  if (separator <= 0 || separator === raw.length - 1) return undefined;
  const local = raw.slice(0, separator).replace(/:\d+$/, "");
  const rawDomain = raw.slice(separator + 1);
  const domain = rawDomain === "c.us" ? "s.whatsapp.net" : rawDomain;
  if (!local || !/^[a-z0-9._-]+$/i.test(local) || !/^[a-z0-9.-]+$/i.test(domain)) return undefined;
  return { canonicalId: `${local}@${domain}`, source: "sender_id", unresolvedAlternate: domain === "lid" };
}

function normalizeConversationId(value: string, kind: ConversationKind): string | undefined {
  const raw = value.trim().toLowerCase().replace(/^whatsapp:/, "");
  if (!raw) return undefined;
  return kind === "group" ? raw : normalizeWhatsAppId(raw)?.canonicalId;
}

function normalizeConversation(input: ConversationInput) {
  const raw = nonBlank(input.conversationId);
  if (!raw) return undefined;
  const withoutPrefix = raw.toLowerCase().replace(/^whatsapp:/, "");
  const kind = input.kind ?? (withoutPrefix.endsWith("@g.us") ? "group" : "dm");
  const nativeId = normalizeConversationId(raw, kind);
  if (!nativeId) return undefined;
  return buildConversationRef({
    channel: "whatsapp",
    accountId: nonBlank(input.accountId) ?? "default",
    nativeId,
    kind,
    ...(input.threadId != null ? { threadId: String(input.threadId) } : {}),
  }, legacyNamespaceSegment(nativeId));
}

function normalizeActor(input: ActorInput): ActorRef | undefined {
  const rawNativeId = nonBlank(input.nativeId);
  if (!rawNativeId) return undefined;
  const normalized = normalizeWhatsAppId(rawNativeId, nonBlank(input.e164));
  if (!normalized) return undefined;
  const displayName = nonBlank(input.displayName);
  const username = nonBlank(input.username);
  const actorKind = input.actorKind ?? "person";
  return {
    channel: "whatsapp",
    accountId: nonBlank(input.accountId) ?? "default",
    nativeId: normalized.canonicalId,
    stableId: `whatsapp:${normalized.canonicalId}`,
    ...(displayName ? { displayName } : {}),
    ...(username ? { username } : {}),
    actorKind,
    bindAsPerson: actorKind === "person",
    identitySource: normalized.source,
    unresolvedAlternate: normalized.unresolvedAlternate,
    rawNativeId,
  };
}

function normalizeInbound(event: Record<string, unknown>, context: Record<string, unknown>): NormalizedInboundMessage | undefined {
  let conversation = normalizeConversation({
    accountId: nonBlank(context.accountId) ?? nonBlank(event.accountId),
    conversationId: nonBlank(context.conversationId) ?? nonBlank(event.conversationId) ?? nonBlank(event.from),
  });
  const providerMessageId = nonBlank(event.messageId) ?? nonBlank(context.messageId);
  const referenceTime = timestampDate(event.timestamp);
  if (!conversation || !providerMessageId || !referenceTime || typeof event.content !== "string" || event.content.length === 0) return undefined;
  const metadata = recordValue(event.metadata);
  const rawSenderId = nonBlank(event.senderId) ?? nonBlank(context.senderId);
  const sender = rawSenderId ? normalizeActor({
    accountId: conversation.accountId,
    nativeId: rawSenderId,
    displayName: nonBlank(event.senderName) ?? nonBlank(metadata?.senderName),
    username: nonBlank(event.senderUsername) ?? nonBlank(metadata?.senderUsername),
    e164: nonBlank(event.senderE164) ?? nonBlank(metadata?.senderE164),
  }) : undefined;
  if (!sender) return undefined;
  // The public hook may project To (our own account) as conversationId in a
  // DM. From is the inbound peer; use it only with structural sender agreement.
  if (conversation.kind === "dm" && nonBlank(event.from)) {
    const peer = normalizeConversation({ accountId: conversation.accountId, conversationId: nonBlank(event.from) });
    const fromActor = normalizeWhatsAppId(String(event.from), nonBlank(event.senderE164) ?? nonBlank(metadata?.senderE164));
    if (!peer || peer.kind !== "dm" || fromActor?.canonicalId !== sender.nativeId) return undefined;
    conversation = normalizeConversation({ accountId: conversation.accountId, conversationId: sender.nativeId })!;
  }
  const replyTo = normalizedReply(event, context);
  const provenance = safeProvenance(event);
  return {
    direction: "inbound",
    providerLabel: "WhatsApp",
    providerMessageId,
    conversation,
    content: event.content,
    referenceTime,
    originalTimestamp: event.timestamp as number,
    timestampSource: "provider",
    sender,
    ...(replyTo ? { replyTo } : {}),
    ...(provenance ? { provenance } : {}),
  };
}

function normalizeOutbound(
  event: Record<string, unknown>,
  context: Record<string, unknown>,
  options?: { observedAt?: Date; selfActorIds?: string[] },
): NormalizedOutboundMessage | undefined {
  const conversation = normalizeConversation({
    accountId: nonBlank(context.accountId) ?? nonBlank(event.accountId),
    conversationId: nonBlank(context.conversationId) ?? nonBlank(event.to),
  });
  const providerMessageId = nonBlank(event.messageId);
  const content = typeof event.content === "string" ? event.content : undefined;
  const referenceTime = options?.observedAt ?? new Date();
  if (!conversation || event.success !== true || !providerMessageId || !content?.trim() || Number.isNaN(referenceTime.valueOf())) return undefined;
  let structuralAddressee: ActorRef | undefined;
  if (conversation.kind === "dm") {
    const peer = normalizeActor({ accountId: conversation.accountId, nativeId: conversation.nativeId });
    const selfIds = new Set((options?.selfActorIds ?? [])
      .map((value) => normalizeWhatsAppId(value)?.canonicalId)
      .filter((value): value is string => Boolean(value)));
    if (peer && !selfIds.has(peer.nativeId)) structuralAddressee = peer;
  }
  const provenance = safeProvenance(event);
  return {
    direction: "outbound",
    providerLabel: "WhatsApp",
    providerMessageId,
    conversation,
    content,
    referenceTime,
    timestampSource: "message_sent_observed",
    sender: { role: "filomena" },
    ...(nonBlank(event.runId) ? { runId: nonBlank(event.runId) } : {}),
    ...(structuralAddressee ? { structuralAddressee } : {}),
    ...(provenance ? { provenance } : {}),
  };
}

function normalizeTurnOrigin(event: Record<string, unknown>): NormalizedTurnOrigin | undefined {
  const ctx = recordValue(event.ctx);
  const runId = nonBlank(event.runId);
  const conversation = ctx ? normalizeConversation({
    accountId: nonBlank(ctx.AccountId),
    conversationId: nonBlank(ctx.OriginatingTo) ?? nonBlank(ctx.To) ?? nonBlank(ctx.From),
  }) : undefined;
  const providerMessageId = ctx
    ? nonBlank(ctx.MessageSidFull) ?? nonBlank(ctx.MessageSid) ?? nonBlank(ctx.MessageSidFirst) ?? nonBlank(ctx.MessageSidLast)
    : undefined;
  const senderId = ctx ? nonBlank(ctx.SenderId) : undefined;
  const participant = senderId ? normalizeActor({
    accountId: conversation?.accountId,
    nativeId: senderId,
    displayName: ctx ? nonBlank(ctx.SenderName) : undefined,
    username: ctx ? nonBlank(ctx.SenderUsername) : undefined,
    e164: ctx ? nonBlank(ctx.SenderE164) : undefined,
  }) : undefined;
  return runId && conversation && providerMessageId && participant
    ? { runId, inboundProviderMessageId: providerMessageId, conversation, participant }
    : undefined;
}

export const whatsappMemoryAdapter: ChannelMemoryAdapter = {
  channel: "whatsapp",
  normalizeConversation,
  normalizeActor,
  normalizeInbound,
  normalizeOutbound,
  normalizeTurnOrigin,
};
