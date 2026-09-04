import { describe, expect, test } from "vitest";

import { getChannelAdapter, supportedMemoryChannels } from "./channel-adapters/registry.js";
import { filomenaUuidForConversation, participantUuidForActor } from "./identity.js";

const WHATSAPP_GROUP_JID = "5511981042361-1589823314@g.us";
const WHATSAPP_GROUP_NAMESPACE = "whatsapp-group-5511981042361-1589823314-g-us";

describe("channel memory adapters", () => {
  test("registry is the central channel selector", () => {
    expect(supportedMemoryChannels()).toEqual(["telegram", "whatsapp"]);
    expect(getChannelAdapter("WHATSAPP")?.channel).toBe("whatsapp");
    expect(getChannelAdapter("telegram")?.channel).toBe("telegram");
    expect(getChannelAdapter("discord")).toBeUndefined();
  });

  test("WhatsApp adapter preserves the legacy conversation identity", () => {
    const conversation = getChannelAdapter("whatsapp")!.normalizeConversation({
      accountId: "renamed-account",
      conversationId: WHATSAPP_GROUP_JID,
    });

    expect(conversation).toEqual({
      channel: "whatsapp",
      accountId: "renamed-account",
      nativeId: WHATSAPP_GROUP_JID,
      kind: "group",
      namespace: WHATSAPP_GROUP_NAMESPACE,
    });
  });

  test("Telegram encodes signed chat IDs without collisions and folds topics into the base chat", () => {
    const adapter = getChannelAdapter("telegram")!;
    const group = adapter.normalizeConversation({
      accountId: "default",
      conversationId: "telegram:-1001234567890:topic:42",
      kind: "group",
    });
    const positiveGroup = adapter.normalizeConversation({
      accountId: "default",
      conversationId: "1001234567890",
      kind: "group",
    });
    const dm = adapter.normalizeConversation({
      accountId: "default",
      conversationId: "telegram:8734062810",
      kind: "dm",
    });

    expect(group).toMatchObject({
      nativeId: "-1001234567890",
      kind: "group",
      threadId: "42",
      namespace: "telegram-group-neg-1001234567890",
    });
    expect(positiveGroup?.namespace).toBe("telegram-group-pos-1001234567890");
    expect(positiveGroup?.namespace).not.toBe(group?.namespace);
    expect(dm?.namespace).toBe("telegram-dm-pos-8734062810");
    expect(adapter.normalizeConversation({
      accountId: "another-operational-name",
      conversationId: "8734062810:direct-topic:7",
      kind: "dm",
    })?.namespace).toBe(dm?.namespace);
  });

  test("display and account names never affect actor or UUID identity", () => {
    const adapter = getChannelAdapter("telegram")!;
    const conversation = adapter.normalizeConversation({
      accountId: "first",
      conversationId: "-1001234567890",
      kind: "group",
    })!;
    const renamedConversation = adapter.normalizeConversation({
      accountId: "renamed",
      conversationId: "-1001234567890",
      kind: "group",
    })!;
    const actor = adapter.normalizeActor({ nativeId: "8734062810", displayName: "Gabriel", username: "gabs" })!;
    const renamed = adapter.normalizeActor({ nativeId: "8734062810", displayName: "Gabs", username: "new_handle" })!;

    expect(actor.stableId).toBe("telegram:user:8734062810");
    expect(renamed.stableId).toBe(actor.stableId);
    expect(participantUuidForActor(conversation, actor)).toBe(participantUuidForActor(renamedConversation, renamed));
    expect(filomenaUuidForConversation(conversation)).toBe(filomenaUuidForConversation(renamedConversation));
  });

  test("participant UUIDs remain conversation scoped and channel safe", () => {
    const telegram = getChannelAdapter("telegram")!;
    const whatsapp = getChannelAdapter("whatsapp")!;
    const user = telegram.normalizeActor({ nativeId: "8734062810", displayName: "Gabriel" })!;
    const telegramDm = telegram.normalizeConversation({ accountId: "default", conversationId: "8734062810", kind: "dm" })!;
    const telegramGroupA = telegram.normalizeConversation({ accountId: "default", conversationId: "-1001", kind: "group" })!;
    const telegramGroupB = telegram.normalizeConversation({ accountId: "default", conversationId: "-1002", kind: "group" })!;
    const whatsappDm = whatsapp.normalizeConversation({ accountId: "default", conversationId: "8734062810@s.whatsapp.net", kind: "dm" })!;
    const whatsappUser = whatsapp.normalizeActor({ nativeId: "8734062810@s.whatsapp.net" })!;

    const values = [
      participantUuidForActor(telegramDm, user),
      participantUuidForActor(telegramGroupA, user),
      participantUuidForActor(telegramGroupB, user),
      participantUuidForActor(whatsappDm, whatsappUser),
    ];
    expect(new Set(values).size).toBe(values.length);
  });
});
