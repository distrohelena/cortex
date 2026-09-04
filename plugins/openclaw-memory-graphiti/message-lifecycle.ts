import type { BoundEntityInput } from "./graphiti.js";
import type { GraphitiClient } from "./graphiti.js";
import type { SpiceDbClient } from "./spicedb.js";
import type { Subject } from "./authorization.js";
import {
  canWriteToGroup,
  ensureGroupMembership,
  writeFragmentRelationships,
} from "./authorization.js";
import { extractAddressTerm, normalizeAddressTerm } from "./social-evidence.js";
import {
  filomenaBoundEntityForConversation,
  inboundEpisodeUuid,
  outboundEpisodeUuid,
  participantBoundEntity,
} from "./identity.js";
import type { NormalizedInboundMessage, NormalizedOutboundMessage } from "./channel-adapters/types.js";
import type { TurnCorrelation } from "./turn-correlation.js";

export type MessageCapture = {
  providerLabel: string;
  episodeUuid: string;
  episodeName: string;
  episodeBody: string;
  extractionBody: string;
  referenceTime: string;
  conversationNamespace: string;
  providerMessageId: string;
  boundEntities: BoundEntityInput[];
  runId?: string;
  exactAddressee: boolean;
};

function replyPayload(message: NormalizedInboundMessage | NormalizedOutboundMessage) {
  return message.replyTo ? {
    id: message.replyTo.providerMessageId,
    ...(message.replyTo.providerMessageIdFull ? { id_full: message.replyTo.providerMessageIdFull } : {}),
    ...(message.replyTo.content !== undefined ? { body: message.replyTo.content } : {}),
    ...(message.replyTo.senderNativeId ? { sender: message.replyTo.senderNativeId } : {}),
    ...(message.replyTo.isQuote !== undefined ? { is_quote: message.replyTo.isQuote } : {}),
  } : null;
}

export function buildInboundCapture(message: NormalizedInboundMessage): MessageCapture {
  const id = inboundEpisodeUuid(message.conversation, message.providerMessageId);
  const speaker = message.sender
    ? participantBoundEntity(message.conversation, message.sender, "speaker")
    : undefined;
  const episodeBody = JSON.stringify({
    schema: "filomena.openclaw.inbound.v1",
    channel: message.conversation.channel,
    account_id: message.conversation.accountId,
    conversation_jid: message.conversation.nativeId,
    conversation_kind: message.conversation.kind,
    ...(message.conversation.threadId ? { thread_id: message.conversation.threadId } : {}),
    provider_message_id: message.providerMessageId,
    sender: message.sender ? {
      raw_id: message.sender.rawNativeId ?? message.sender.nativeId,
      canonical_id: message.sender.nativeId,
      stable_id: message.sender.stableId,
      display_name: message.sender.displayName ?? null,
      identity_source: message.sender.identitySource ?? null,
      unresolved_alternate: message.sender.unresolvedAlternate ?? false,
    } : null,
    timestamp: { original: message.originalTimestamp, iso: message.referenceTime.toISOString() },
    reply: replyPayload(message),
    provider_update: message.provenance?.providerUpdateId || message.provenance?.providerUpdateKind ? {
      ...(message.provenance.providerUpdateId ? { id: message.provenance.providerUpdateId } : {}),
      ...(message.provenance.providerUpdateKind ? { kind: message.provenance.providerUpdateKind } : {}),
    } : null,
    provider_metadata: message.provenance?.raw ?? null,
    text: message.content,
  });
  return {
    providerLabel: message.providerLabel,
    episodeUuid: id,
    episodeName: `inbound_${id}`,
    episodeBody,
    extractionBody: message.content,
    referenceTime: message.referenceTime.toISOString(),
    conversationNamespace: message.conversation.namespace,
    providerMessageId: message.providerMessageId,
    boundEntities: speaker ? [speaker] : [],
    exactAddressee: false,
  };
}

export function buildOutboundCaptureFromNormalized(
  message: NormalizedOutboundMessage,
  correlation?: TurnCorrelation,
): MessageCapture {
  const id = outboundEpisodeUuid(message.conversation, message.providerMessageId);
  const exactCorrelation = correlation
    && message.runId === correlation.runId
    && message.conversation.namespace === correlation.conversation.namespace
    && message.conversation.nativeId === correlation.conversation.nativeId
    ? correlation
    : undefined;
  const addressee = exactCorrelation?.participant
    ?? (message.structuralAddressee
      ? participantBoundEntity(message.conversation, message.structuralAddressee, "addressee")
      : undefined);
  const boundEntities = [filomenaBoundEntityForConversation(message.conversation)];
  if (addressee) boundEntities.push(addressee);
  const episodeBody = JSON.stringify({
    schema: "filomena.openclaw.outbound.v1",
    channel: message.conversation.channel,
    account_id: message.conversation.accountId,
    conversation_jid: message.conversation.nativeId,
    conversation_kind: message.conversation.kind,
    ...(message.conversation.threadId ? { thread_id: message.conversation.threadId } : {}),
    provider_message_id: message.providerMessageId,
    timestamp: { iso: message.referenceTime.toISOString(), source: message.timestampSource },
    reply: replyPayload(message),
    originating: exactCorrelation ? {
      inbound_provider_message_id: exactCorrelation.inboundProviderMessageId,
      participant_uuid: exactCorrelation.participantUuid,
      run_id: exactCorrelation.runId,
    } : null,
    provider_metadata: message.provenance?.raw ?? null,
    text: message.content,
  });
  return {
    providerLabel: message.providerLabel,
    episodeUuid: id,
    episodeName: `outbound_${id}`,
    episodeBody,
    extractionBody: message.content,
    referenceTime: message.referenceTime.toISOString(),
    conversationNamespace: message.conversation.namespace,
    providerMessageId: message.providerMessageId,
    boundEntities,
    ...(message.runId ? { runId: message.runId } : {}),
    exactAddressee: Boolean(exactCorrelation || message.structuralAddressee),
  };
}

type LifecycleLogger = {
  info(message: string): void;
  warn(message: string): void;
};

export type MessageLifecycleRecorder = {
  recordInbound(message: NormalizedInboundMessage): Promise<MessageCapture | undefined>;
  recordOutbound(message: NormalizedOutboundMessage, correlation?: TurnCorrelation): Promise<MessageCapture | undefined>;
};

/**
 * Channel-independent persistence boundary. Adapters normalize provider events;
 * this recorder owns authorization, idempotency, Graphiti writes and social evidence.
 */
export function createMessageLifecycleRecorder(input: {
  graphiti: GraphitiClient;
  spicedb: SpiceDbClient;
  subject: Subject;
  extractionInstructions: string;
  logger: LifecycleLogger;
  getLastWriteToken(): string | undefined;
  setLastWriteToken(token: string): void;
  maxRememberedEpisodes?: number;
}): MessageLifecycleRecorder {
  const capturedEpisodes = new Map<string, Promise<void>>();
  const maxRememberedEpisodes = input.maxRememberedEpisodes ?? 4096;

  const captureOnce = async (episodeUuid: string, operation: () => Promise<void>) => {
    const existing = capturedEpisodes.get(episodeUuid);
    if (existing) {
      await existing;
      return;
    }
    const pending = operation();
    capturedEpisodes.set(episodeUuid, pending);
    try {
      await pending;
      while (capturedEpisodes.size > maxRememberedEpisodes) {
        const oldest = capturedEpisodes.keys().next().value as string | undefined;
        if (!oldest) break;
        capturedEpisodes.delete(oldest);
      }
    } catch (error) {
      capturedEpisodes.delete(episodeUuid);
      throw error;
    }
  };

  const persist = async (
    capture: MessageCapture,
    direction: "inbound" | "outbound",
  ): Promise<MessageCapture | undefined> => {
    const conversationNamespace = capture.conversationNamespace;
    const token = await ensureGroupMembership(input.spicedb, conversationNamespace, input.subject);
    if (token) input.setLastWriteToken(token);
    if (!await canWriteToGroup(
      input.spicedb,
      input.subject,
      conversationNamespace,
      input.getLastWriteToken(),
    )) {
      input.logger.warn(`openclaw-memory-graphiti: ${direction} capture denied for conversation ${conversationNamespace}`);
      return undefined;
    }

    await captureOnce(capture.episodeUuid, async () => {
      const result = await input.graphiti.addEpisode({
        name: capture.episodeName,
        episode_body: capture.episodeBody,
        source_description: `OpenClaw ${direction} ${capture.providerLabel} message`,
        group_id: conversationNamespace,
        source: "json",
        episode_uuid: capture.episodeUuid,
        reference_time: capture.referenceTime,
        bound_entities: capture.boundEntities,
        extraction_body: capture.extractionBody,
        custom_extraction_instructions: input.extractionInstructions,
      });

      if (direction === "outbound" && capture.exactAddressee && capture.runId && capture.boundEntities[1]) {
        const addressTerm = extractAddressTerm(capture.extractionBody);
        if (addressTerm) {
          await input.graphiti.recordAddressTerm({
            group_id: conversationNamespace,
            source_uuid: capture.boundEntities[0].uuid,
            target_uuid: capture.boundEntities[1].uuid,
            term: addressTerm,
            normalized_term: normalizeAddressTerm(addressTerm),
            run_id: capture.runId,
            episode_uuid: capture.episodeUuid,
            reference_time: capture.referenceTime,
          });
        }
      }

      result.resolvedUuid
        .then(async (realUuid) => {
          const writeToken = await writeFragmentRelationships(input.spicedb, {
            fragmentId: realUuid,
            groupId: conversationNamespace,
            sharedBy: input.subject,
          });
          if (writeToken) input.setLastWriteToken(writeToken);
        })
        .catch((error) => {
          input.logger.warn(`openclaw-memory-graphiti: deferred SpiceDB write (${direction} capture) failed: ${String(error)}`);
        });
    });

    input.logger.info(direction === "inbound"
      ? `openclaw-memory-graphiti: passively captured provider message ${capture.providerMessageId} to ${conversationNamespace}`
      : `openclaw-memory-graphiti: captured outbound provider message ${capture.providerMessageId} to ${conversationNamespace}`);
    return capture;
  };

  return {
    async recordInbound(message) {
      return persist(buildInboundCapture(message), "inbound");
    },
    async recordOutbound(message, correlation) {
      return persist(buildOutboundCaptureFromNormalized(message, correlation), "outbound");
    },
  };
}
