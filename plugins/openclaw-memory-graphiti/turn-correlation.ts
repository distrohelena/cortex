import type { BoundEntityInput } from "./graphiti.js";
import {
  normalizeWhatsAppSenderId,
  participantUuid,
} from "./passive-capture.js";

type FinalizedInboundContext = {
  Provider?: string;
  Surface?: string;
  OriginatingChannel?: string;
  OriginatingTo?: string;
  To?: string;
  From?: string;
  MessageSidFull?: string;
  MessageSid?: string;
  MessageSidFirst?: string;
  MessageSidLast?: string;
  SenderId?: string;
  SenderE164?: string;
  SenderName?: string;
  SenderUsername?: string;
};

type ReplyDispatchObservation = {
  runId?: string;
  sessionKey?: string;
  ctx: FinalizedInboundContext;
};

export type TurnCorrelation = {
  runId: string;
  groupId: string;
  conversationJid: string;
  inboundProviderMessageId: string;
  participantUuid: string;
  participant: BoundEntityInput;
};

function nonBlank(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

export function normalizeConversationJid(value: string): string | undefined {
  const normalized = value.trim().toLowerCase().replace(/^whatsapp:/, "");
  return normalized || undefined;
}

function sameCorrelation(left: TurnCorrelation, right: TurnCorrelation): boolean {
  return left.groupId === right.groupId
    && left.conversationJid === right.conversationJid
    && left.inboundProviderMessageId === right.inboundProviderMessageId
    && left.participantUuid === right.participantUuid;
}

/** Exact run-bound provenance captured from OpenClaw's public reply_dispatch hook. */
export class TurnCorrelationRegistry {
  private readonly entries = new Map<string, TurnCorrelation>();
  private readonly conflictedRuns = new Set<string>();

  constructor(private readonly maxEntries = 1024) {}

  get size(): number {
    return this.entries.size;
  }

  observeReplyDispatch(input: {
    groupId: string;
    event: ReplyDispatchObservation;
  }): TurnCorrelation | undefined {
    const { groupId, event } = input;
    const provider = nonBlank(
      event.ctx.OriginatingChannel ?? event.ctx.Surface ?? event.ctx.Provider,
    )?.toLowerCase();
    const runId = nonBlank(event.runId);
    const providerMessageId = nonBlank(
      event.ctx.MessageSidFull
      ?? event.ctx.MessageSid
      ?? event.ctx.MessageSidFirst
      ?? event.ctx.MessageSidLast,
    );
    const rawSenderId = nonBlank(event.ctx.SenderId);
    const conversationJid = nonBlank(
      event.ctx.OriginatingTo ?? event.ctx.To ?? event.ctx.From,
    );
    const normalizedConversation = conversationJid
      ? normalizeConversationJid(conversationJid)
      : undefined;
    if (provider !== "whatsapp" || !runId || !providerMessageId || !rawSenderId || !normalizedConversation) {
      return undefined;
    }

    const sender = normalizeWhatsAppSenderId(rawSenderId, nonBlank(event.ctx.SenderE164));
    if (!sender) return undefined;
    const stableId = `whatsapp:${sender.canonicalId}`;
    const displayName = nonBlank(event.ctx.SenderName) ?? nonBlank(event.ctx.SenderUsername);
    const canonicalParticipantUuid = participantUuid(groupId, sender.canonicalId);
    const correlation: TurnCorrelation = {
      runId,
      groupId,
      conversationJid: normalizedConversation,
      inboundProviderMessageId: providerMessageId,
      participantUuid: canonicalParticipantUuid,
      participant: {
        uuid: canonicalParticipantUuid,
        role: "addressee",
        name: stableId,
        entity_type: "Person",
        labels: ["Person"],
        attributes: {
          stable_id: stableId,
          display_name_current: displayName ?? "",
          aliases: displayName ? [displayName] : [],
          identity_provider: "whatsapp",
          identity_source: sender.source,
        },
      },
    };

    if (this.conflictedRuns.has(runId)) return undefined;
    const existing = this.entries.get(runId);
    if (existing && !sameCorrelation(existing, correlation)) {
      this.entries.delete(runId);
      this.conflictedRuns.add(runId);
      return undefined;
    }
    this.entries.set(runId, correlation);
    while (this.entries.size > this.maxEntries) {
      const oldest = this.entries.keys().next().value as string | undefined;
      if (!oldest) break;
      this.entries.delete(oldest);
    }
    return correlation;
  }

  get(runId: string | undefined, groupId: string, conversationJid: string): TurnCorrelation | undefined {
    const normalizedRunId = nonBlank(runId);
    const normalizedConversation = normalizeConversationJid(conversationJid);
    if (!normalizedRunId || !normalizedConversation || this.conflictedRuns.has(normalizedRunId)) {
      return undefined;
    }
    const correlation = this.entries.get(normalizedRunId);
    return correlation
      && correlation.groupId === groupId
      && correlation.conversationJid === normalizedConversation
      ? correlation
      : undefined;
  }
}
