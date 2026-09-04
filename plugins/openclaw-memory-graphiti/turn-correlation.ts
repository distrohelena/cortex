import type { BoundEntityInput } from "./graphiti.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import type { ConversationRef } from "./channel-adapters/types.js";
import { participantBoundEntity, participantUuidForActor } from "./identity.js";

type FinalizedInboundContext = Record<string, unknown>;

type ReplyDispatchObservation = {
  runId?: string;
  ctx: FinalizedInboundContext;
};

export type TurnCorrelation = {
  runId: string;
  conversation: ConversationRef;
  conversationNamespace: string;
  inboundProviderMessageId: string;
  participantUuid: string;
  participant: BoundEntityInput;
  /** Phase 3 compatibility aliases. */
  groupId: string;
  conversationJid: string;
};

function nonBlank(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

/** Phase 3 compatibility facade for WhatsApp callers. */
export function normalizeConversationJid(value: string): string | undefined {
  return getChannelAdapter("whatsapp")?.normalizeConversation({ conversationId: value })?.nativeId;
}

function sameCorrelation(left: TurnCorrelation, right: TurnCorrelation): boolean {
  return left.conversationNamespace === right.conversationNamespace
    && left.conversation.channel === right.conversation.channel
    && left.conversation.nativeId === right.conversation.nativeId
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
    conversationNamespace?: string;
    /** Phase 3 compatibility alias. */
    groupId?: string;
    event: ReplyDispatchObservation;
  }): TurnCorrelation | undefined {
    const provider = nonBlank(
      input.event.ctx.OriginatingChannel
      ?? input.event.ctx.Surface
      ?? input.event.ctx.Provider,
    )?.toLowerCase();
    const adapter = getChannelAdapter(provider);
    const origin = adapter?.normalizeTurnOrigin(input.event as unknown as Record<string, unknown>);
    const expectedNamespace = input.conversationNamespace ?? input.groupId;
    if (!origin || !expectedNamespace) return undefined;
    const legacyExplicitNamespace = !input.conversationNamespace && Boolean(input.groupId);
    if (!legacyExplicitNamespace && origin.conversation.namespace !== expectedNamespace) return undefined;
    const conversation = legacyExplicitNamespace
      ? { ...origin.conversation, namespace: expectedNamespace }
      : origin.conversation;

    const participant = participantBoundEntity(conversation, origin.participant, "addressee");
    if (!participant) return undefined;
    const participantUuid = participantUuidForActor(conversation, origin.participant);
    const correlation: TurnCorrelation = {
      runId: origin.runId,
      conversation,
      conversationNamespace: expectedNamespace,
      inboundProviderMessageId: origin.inboundProviderMessageId,
      participantUuid,
      participant,
      groupId: expectedNamespace,
      conversationJid: conversation.nativeId,
    };

    if (this.conflictedRuns.has(origin.runId)) return undefined;
    const existing = this.entries.get(origin.runId);
    if (existing && !sameCorrelation(existing, correlation)) {
      this.entries.delete(origin.runId);
      this.conflictedRuns.add(origin.runId);
      return undefined;
    }
    this.entries.set(origin.runId, correlation);
    while (this.entries.size > this.maxEntries) {
      const oldest = this.entries.keys().next().value as string | undefined;
      if (!oldest) break;
      this.entries.delete(oldest);
    }
    return correlation;
  }

  get(runId: string | undefined, conversationNamespace: string, conversationNativeId: string): TurnCorrelation | undefined {
    const normalizedRunId = nonBlank(runId);
    if (!normalizedRunId || this.conflictedRuns.has(normalizedRunId)) return undefined;
    const correlation = this.entries.get(normalizedRunId);
    if (!correlation || correlation.conversationNamespace !== conversationNamespace) return undefined;
    const adapter = getChannelAdapter(correlation.conversation.channel);
    const normalizedConversation = adapter?.normalizeConversation({
      accountId: correlation.conversation.accountId,
      conversationId: conversationNativeId,
      kind: correlation.conversation.kind,
    });
    return normalizedConversation?.nativeId === correlation.conversation.nativeId ? correlation : undefined;
  }
}
