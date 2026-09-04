import { createHash } from "node:crypto";

import type { BoundEntityInput } from "./graphiti.js";
import type { ActorRef, ConversationRef } from "./channel-adapters/types.js";

export const FILOMENA_EPISODE_NAMESPACE_V1 = "b7897b2d-9c72-5ea9-8f1f-19b097cd37f8";
export const FILOMENA_PARTICIPANT_NAMESPACE_V1 = "cc5bf3bb-68cb-5e0e-a63e-cf8e76cf5f8b";
export const FILOMENA_AGENT_NAMESPACE_V1 = "409fb013-87b0-5ac0-a152-e07b7e6074a0";
export const FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1 = "7e5bb18f-2c95-582d-9107-a809ddd3759c";

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

export function participantUuidForActor(conversation: ConversationRef, actor: ActorRef): string {
  return uuidV5(FILOMENA_PARTICIPANT_NAMESPACE_V1, `${conversation.namespace}\0${actor.stableId}`);
}

export function filomenaUuidForConversation(conversation: ConversationRef): string {
  return uuidV5(FILOMENA_AGENT_NAMESPACE_V1, `${conversation.namespace}\0agent:filomena`);
}

export function inboundEpisodeUuid(conversation: ConversationRef, providerMessageId: string): string {
  return uuidV5(FILOMENA_EPISODE_NAMESPACE_V1, `${conversation.channel}\0${conversation.nativeId}\0${providerMessageId}`);
}

export function outboundEpisodeUuid(conversation: ConversationRef, providerMessageId: string): string {
  return uuidV5(FILOMENA_OUTBOUND_EPISODE_NAMESPACE_V1, `${conversation.channel}\0${conversation.nativeId}\0${providerMessageId}`);
}

export function participantBoundEntity(
  conversation: ConversationRef,
  actor: ActorRef,
  role: "speaker" | "addressee",
): BoundEntityInput | undefined {
  if (!actor.bindAsPerson) return undefined;
  const aliases = actor.displayName ? [actor.displayName] : [];
  return {
    uuid: participantUuidForActor(conversation, actor),
    role,
    name: actor.stableId,
    entity_type: "Person",
    labels: ["Person"],
    attributes: {
      stable_id: actor.stableId,
      display_name_current: actor.displayName ?? "",
      aliases,
      identity_provider: actor.channel,
      identity_source: actor.identitySource ?? "sender_id",
    },
  };
}

export function filomenaBoundEntityForConversation(conversation: ConversationRef): BoundEntityInput {
  return {
    uuid: filomenaUuidForConversation(conversation),
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
