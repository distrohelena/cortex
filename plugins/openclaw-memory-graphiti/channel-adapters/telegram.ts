import { buildConversationRef } from "../conversation-identity.js";
import { nonBlank, normalizedReply, recordValue, safeProvenance, timestampDate } from "./hook-normalization.js";
import type { ActorInput, ActorRef, ChannelMemoryAdapter, ConversationInput, ConversationKind, NormalizedInboundMessage, NormalizedOutboundMessage, NormalizedTurnOrigin } from "./types.js";

function parseConversation(value: string): { nativeId: string; threadId?: string } | undefined {
  const raw = value.trim().toLowerCase().replace(/^telegram:/, "");
  const match = raw.match(/^(-?\d+)(?::(?:topic|direct-topic):(\d+))?$/);
  if (!match) return undefined;
  return { nativeId: match[1], ...(match[2] ? { threadId: match[2] } : {}) };
}

function normalizeConversation(input: ConversationInput) {
  const raw = nonBlank(input.conversationId);
  if (!raw) return undefined;
  const parsed = parseConversation(raw);
  if (!parsed) return undefined;
  const kind: ConversationKind = input.kind ?? (parsed.nativeId.startsWith("-") ? "group" : "dm");
  const threadId = input.threadId != null ? String(input.threadId) : parsed.threadId;
  return buildConversationRef({
    channel: "telegram",
    accountId: nonBlank(input.accountId) ?? "default",
    nativeId: parsed.nativeId,
    kind,
    ...(threadId ? { threadId } : {}),
  }, parsed.nativeId.startsWith("-") ? `neg-${parsed.nativeId.slice(1)}` : `pos-${parsed.nativeId}`);
}

function normalizeActor(input: ActorInput): ActorRef | undefined {
  const rawNativeId = nonBlank(input.nativeId)?.replace(/^telegram:(?:user:)?/i, "");
  if (!rawNativeId || !/^\d+$/.test(rawNativeId)) return undefined;
  const displayName = nonBlank(input.displayName);
  const username = nonBlank(input.username);
  const actorKind = input.actorKind ?? "person";
  return {
    channel: "telegram",
    accountId: nonBlank(input.accountId) ?? "default",
    nativeId: rawNativeId,
    stableId: `telegram:user:${rawNativeId}`,
    ...(displayName ? { displayName } : {}),
    ...(username ? { username } : {}),
    actorKind,
    bindAsPerson: actorKind === "person",
    identitySource: "sender_id",
    rawNativeId,
  };
}

function normalizeInbound(event: Record<string, unknown>, context: Record<string, unknown>): NormalizedInboundMessage | undefined {
  const conversation = normalizeConversation({
    accountId: nonBlank(context.accountId) ?? nonBlank(event.accountId),
    conversationId: nonBlank(context.conversationId) ?? nonBlank(event.conversationId) ?? nonBlank(event.from),
    threadId: typeof event.threadId === "string" || typeof event.threadId === "number" ? event.threadId : undefined,
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
    actorKind: metadata?.senderKind === "bot" || metadata?.senderKind === "channel" || metadata?.senderKind === "unknown"
      ? metadata.senderKind
      : "person",
  }) : undefined;
  return {
    direction: "inbound",
    providerLabel: "Telegram",
    providerMessageId,
    conversation,
    content: event.content,
    referenceTime,
    originalTimestamp: event.timestamp as number,
    timestampSource: "provider",
    ...(sender ? { sender } : {}),
    ...(normalizedReply(event, context) ? { replyTo: normalizedReply(event, context) } : {}),
    ...(safeProvenance(event) ? { provenance: safeProvenance(event) } : {}),
  };
}

function normalizeOutbound(
  event: Record<string, unknown>,
  context: Record<string, unknown>,
  options?: { observedAt?: Date },
): NormalizedOutboundMessage | undefined {
  const conversation = normalizeConversation({
    accountId: nonBlank(context.accountId) ?? nonBlank(event.accountId),
    conversationId: nonBlank(context.conversationId) ?? nonBlank(event.to),
  });
  const providerMessageId = nonBlank(event.messageId);
  const content = typeof event.content === "string" ? event.content : undefined;
  const referenceTime = options?.observedAt ?? new Date();
  if (!conversation || event.success !== true || !providerMessageId || !content?.trim() || Number.isNaN(referenceTime.valueOf())) return undefined;
  const structuralAddressee = conversation.kind === "dm"
    ? normalizeActor({ accountId: conversation.accountId, nativeId: conversation.nativeId })
    : undefined;
  return {
    direction: "outbound",
    providerLabel: "Telegram",
    providerMessageId,
    conversation,
    content,
    referenceTime,
    timestampSource: "message_sent_observed",
    sender: { role: "filomena" },
    ...(nonBlank(event.runId) ? { runId: nonBlank(event.runId) } : {}),
    ...(structuralAddressee ? { structuralAddressee } : {}),
    ...(safeProvenance(event) ? { provenance: safeProvenance(event) } : {}),
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
  }) : undefined;
  return runId && conversation && providerMessageId && participant
    ? { runId, inboundProviderMessageId: providerMessageId, conversation, participant }
    : undefined;
}

export const telegramMemoryAdapter: ChannelMemoryAdapter = {
  channel: "telegram",
  normalizeConversation,
  normalizeActor,
  normalizeInbound,
  normalizeOutbound,
  normalizeTurnOrigin,
};
