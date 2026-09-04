import type { BoundEntityInput } from "./graphiti.js";
import { conversationIdentity } from "./conversation-namespace.mjs";
import { normalizeWhatsAppSenderId, participantUuid, uuidV5 } from "./passive-capture.js";
import type { TurnCorrelation } from "./turn-correlation.js";

export const FILOMENA_AGENT_NAMESPACE_V1 = "409fb013-87b0-5ac0-a152-e07b7e6074a0";
export const FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1 = "7e5bb18f-2c95-582d-9107-a809ddd3759c";

type MessageSentEvent = {
  to?: string;
  content?: string;
  success?: boolean;
  messageId?: string;
  runId?: string;
};

type MessageSentContext = {
  channelId?: string;
  accountId?: string;
  conversationId?: string;
};

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

function nonBlank(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

export function filomenaUuid(groupId: string): string {
  return uuidV5(FILOMENA_AGENT_NAMESPACE_V1, `${groupId}\0agent:filomena`);
}

export function filomenaBoundEntity(groupId: string): BoundEntityInput {
  return {
    uuid: filomenaUuid(groupId),
    role: "speaker",
    name: "agent:filomena",
    entity_type: "Agent",
    labels: ["Agent"],
    attributes: {
      stable_id: "agent:filomena",
      display_name_current: "Filomena",
      aliases: ["Filomena"],
      actor_kind: "assistant",
    },
  };
}

function outboundEpisodeUuid(channel: string, conversationJid: string, providerMessageId: string): string {
  return uuidV5(
    FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1,
    `${channel}\0${conversationJid}\0${providerMessageId}`,
  );
}

export function buildOutboundCapture(input: {
  event: MessageSentEvent;
  context: MessageSentContext;
  groupId: string;
  observedAt?: Date;
  correlation?: TurnCorrelation;
  selfActorIds?: string[];
}): OutboundCapture | undefined {
  const { event, context, groupId } = input;
  const channel = nonBlank(context.channelId)?.toLowerCase();
  const rawConversation = nonBlank(context.conversationId) ?? nonBlank(event.to);
  let identity: ReturnType<typeof conversationIdentity> | undefined;
  try {
    identity = conversationIdentity({ channel, conversationId: rawConversation });
  } catch {
    return undefined;
  }
  const conversationJid = identity.conversationJid;
  const providerMessageId = nonBlank(event.messageId);
  const content = typeof event.content === "string" ? event.content : undefined;
  if (channel !== "whatsapp" || !conversationJid || event.success !== true || !providerMessageId || !content?.trim()) {
    return undefined;
  }

  const observedAt = input.observedAt ?? new Date();
  if (Number.isNaN(observedAt.valueOf())) return undefined;
  const referenceTime = observedAt.toISOString();
  const id = outboundEpisodeUuid(channel, conversationJid, providerMessageId);
  const exactCorrelation = input.correlation
    && nonBlank(event.runId) === input.correlation.runId
    && input.correlation.groupId === groupId
    && input.correlation.conversationJid === conversationJid
    ? input.correlation
    : undefined;
  const boundEntities = [filomenaBoundEntity(groupId)];
  if (exactCorrelation) {
    boundEntities.push(exactCorrelation.participant);
  } else if (identity.kind === "dm") {
    const peer = normalizeWhatsAppSenderId(conversationJid);
    const selfIds = new Set((input.selfActorIds ?? [])
      .map((value) => normalizeWhatsAppSenderId(value)?.canonicalId)
      .filter((value): value is string => Boolean(value)));
    if (peer && !selfIds.has(peer.canonicalId)) {
      const stableId = `whatsapp:${peer.canonicalId}`;
      boundEntities.push({
        uuid: participantUuid(groupId, peer.canonicalId),
        role: "addressee",
        name: stableId,
        entity_type: "Person",
        labels: ["Person"],
        attributes: {
          stable_id: stableId,
          display_name_current: "",
          aliases: [],
          identity_provider: "whatsapp",
          identity_source: peer.source,
        },
      });
    }
  }

  const episodeBody = JSON.stringify({
    schema: "filomena.openclaw.outbound.v1",
    channel,
    account_id: nonBlank(context.accountId) ?? null,
    conversation_jid: conversationJid,
    conversation_kind: identity.kind,
    provider_message_id: providerMessageId,
    timestamp: { iso: referenceTime, source: "message_sent_observed" },
    reply: null,
    originating: exactCorrelation
      ? {
          inbound_provider_message_id: exactCorrelation.inboundProviderMessageId,
          participant_uuid: exactCorrelation.participantUuid,
          run_id: exactCorrelation.runId,
        }
      : null,
    text: content,
  });

  return {
    episodeUuid: id,
    episodeName: `outbound_${id}`,
    episodeBody,
    extractionBody: content,
    referenceTime,
    conversationJid,
    providerMessageId,
    runId: nonBlank(event.runId),
    boundEntities,
  };
}
