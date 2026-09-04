import { describe, expect, test } from "vitest";

import { telegramMemoryAdapter } from "./channel-adapters/telegram.js";
import {
  filomenaUuidForConversation,
  inboundEpisodeUuid,
  outboundEpisodeUuid,
  participantUuidForActor,
} from "./identity.js";

describe("Telegram literal identity fixtures", () => {
  test("group namespace and identities are stable literals", () => {
    const conversation = telegramMemoryAdapter.normalizeConversation({
      accountId: "filomena",
      conversationId: "telegram:-1001234567890:topic:42",
    })!;
    const actor = telegramMemoryAdapter.normalizeActor({
      accountId: "filomena",
      nativeId: "8734062810",
      displayName: "Gabs",
      username: "mutable",
    })!;

    expect(conversation.namespace).toBe("telegram-group-neg-1001234567890");
    expect(conversation.nativeId).toBe("-1001234567890");
    expect(conversation.threadId).toBe("42");
    expect(actor.stableId).toBe("telegram:user:8734062810");
    expect(participantUuidForActor(conversation, actor)).toBe("8856171c-76e1-507e-90b2-51a4380c430e");
    expect(filomenaUuidForConversation(conversation)).toBe("5c7c2ad9-f287-5378-b2e0-09bfb2522a22");
    expect(inboundEpisodeUuid(conversation, "77")).toBe("eb451775-4393-53e9-ac97-c1c54ccc6f65");
    expect(outboundEpisodeUuid(conversation, "92")).toBe("66ec9d5d-6bdd-5055-b266-56698e68e994");
  });

  test("DM identities ignore account and mutable presentation metadata", () => {
    const first = telegramMemoryAdapter.normalizeConversation({ accountId: "old", conversationId: "8734062810" })!;
    const second = telegramMemoryAdapter.normalizeConversation({ accountId: "renamed", conversationId: "telegram:8734062810" })!;
    const actorA = telegramMemoryAdapter.normalizeActor({ nativeId: "8734062810", displayName: "Gabriel" })!;
    const actorB = telegramMemoryAdapter.normalizeActor({ nativeId: "8734062810", displayName: "Gabs", username: "new_name" })!;

    expect(first.namespace).toBe("telegram-dm-pos-8734062810");
    expect(second.namespace).toBe(first.namespace);
    expect(participantUuidForActor(first, actorA)).toBe("fdf5f22d-1466-5b36-aa9a-e7a9354c0f22");
    expect(participantUuidForActor(second, actorB)).toBe("fdf5f22d-1466-5b36-aa9a-e7a9354c0f22");
    expect(filomenaUuidForConversation(first)).toBe("8aaf668c-a0cc-59ab-852d-598d13e5515c");
    expect(filomenaUuidForConversation(second)).toBe("8aaf668c-a0cc-59ab-852d-598d13e5515c");
  });
});
