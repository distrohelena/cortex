import type { BoundEntityInput } from "./graphiti.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import {
  FILOMENA_AGENT_NAMESPACE_V1,
  FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1,
  filomenaBoundEntityForConversation,
  filomenaUuidForConversation,
} from "./identity.js";
import { buildOutboundCaptureFromNormalized } from "./message-lifecycle.js";
import type { TurnCorrelation } from "./turn-correlation.js";

export { FILOMENA_AGENT_NAMESPACE_V1, FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1 };

export type OutboundCapture = {
  episodeUuid: string;
  episodeName: string;
  episodeBody: string;
  extractionBody: string;
  referenceTime: string;
  conversationJid: string;
  providerMessageId: string;
  runId?: string;
  boundEntities: BoundEntityInput[];
};

function legacyConversation(conversationNamespace: string) {
  const adapter = getChannelAdapter("whatsapp")!;
  const placeholder = adapter.normalizeConversation({ conversationId: "1@s.whatsapp.net", kind: "dm" });
  if (!placeholder) throw new Error("WhatsApp adapter unavailable");
  return { ...placeholder, namespace: conversationNamespace };
}

/** Backward-compatible facade preserving the Phase 3 Agent UUID contract. */
export function filomenaUuid(conversationNamespace: string): string {
  return filomenaUuidForConversation(legacyConversation(conversationNamespace));
}

export function filomenaBoundEntity(conversationNamespace: string): BoundEntityInput {
  return filomenaBoundEntityForConversation(legacyConversation(conversationNamespace));
}

/** Backward-compatible WhatsApp entry point over the common outbound lifecycle. */
export function buildOutboundCapture(input: {
  event: Record<string, unknown>;
  context: Record<string, unknown>;
  groupId: string;
  observedAt?: Date;
  correlation?: TurnCorrelation;
  selfActorIds?: string[];
}): OutboundCapture | undefined {
  const adapter = getChannelAdapter("whatsapp")!;
  const normalized = adapter.normalizeOutbound(input.event, input.context, {
    observedAt: input.observedAt,
    selfActorIds: input.selfActorIds,
  });
  if (!normalized) return undefined;
  const legacyNormalized = {
    ...normalized,
    conversation: { ...normalized.conversation, namespace: input.groupId },
  };
  const capture = buildOutboundCaptureFromNormalized(legacyNormalized, input.correlation);
  return {
    episodeUuid: capture.episodeUuid,
    episodeName: capture.episodeName,
    episodeBody: capture.episodeBody,
    extractionBody: capture.extractionBody,
    referenceTime: capture.referenceTime,
    conversationJid: normalized.conversation.nativeId,
    providerMessageId: capture.providerMessageId,
    ...(capture.runId ? { runId: capture.runId } : {}),
    boundEntities: capture.boundEntities,
  };
}
