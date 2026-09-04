import type { BoundEntityInput } from "./graphiti.js";
import { getChannelAdapter } from "./channel-adapters/registry.js";
import { normalizeWhatsAppId } from "./channel-adapters/whatsapp.js";
import {
  FILOMENA_EPISODE_NAMESPACE_V1,
  FILOMENA_PARTICIPANT_NAMESPACE_V1,
  participantUuidForActor,
  uuidV5,
} from "./identity.js";
import { buildInboundCapture } from "./message-lifecycle.js";

export { FILOMENA_EPISODE_NAMESPACE_V1, FILOMENA_PARTICIPANT_NAMESPACE_V1, uuidV5 };

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

/** Backward-compatible facade. New channel code uses ChannelMemoryAdapter directly. */
export function normalizeWhatsAppSenderId(senderId: string, senderE164?: string): NormalizedSenderId | undefined {
  return normalizeWhatsAppId(senderId, senderE164);
}

/** Backward-compatible facade preserving all Phase 2 UUIDs. */
export function participantUuid(conversationNamespace: string, canonicalSenderId: string): string {
  const adapter = getChannelAdapter("whatsapp")!;
  const conversation = adapter.normalizeConversation({ conversationId: canonicalSenderId, kind: "dm" });
  const actor = adapter.normalizeActor({ nativeId: canonicalSenderId });
  if (!conversation || !actor) throw new Error("Invalid canonical WhatsApp sender ID");
  return participantUuidForActor({ ...conversation, namespace: conversationNamespace }, actor);
}

/** Backward-compatible WhatsApp entry point over the common inbound lifecycle. */
export function buildPassiveCapture(input: {
  event: Record<string, unknown>;
  context: Record<string, unknown>;
  groupId: string;
}): PassiveCapture | undefined {
  const adapter = getChannelAdapter("whatsapp")!;
  const normalized = adapter.normalizeInbound(input.event, input.context);
  if (!normalized || !normalized.sender) return undefined;
  const legacyNormalized = {
    ...normalized,
    conversation: { ...normalized.conversation, namespace: input.groupId },
  };
  const capture = buildInboundCapture(legacyNormalized);
  const boundEntity = capture.boundEntities[0];
  if (!boundEntity) return undefined;
  return {
    episodeUuid: capture.episodeUuid,
    episodeName: capture.episodeName,
    episodeBody: capture.episodeBody,
    extractionBody: capture.extractionBody,
    referenceTime: capture.referenceTime,
    conversationJid: normalized.conversation.nativeId,
    providerMessageId: capture.providerMessageId,
    sender: {
      canonicalId: normalized.sender.nativeId,
      source: normalized.sender.identitySource === "sender_e164" ? "sender_e164" : "sender_id",
      unresolvedAlternate: normalized.sender.unresolvedAlternate ?? false,
    },
    boundEntity,
  };
}
