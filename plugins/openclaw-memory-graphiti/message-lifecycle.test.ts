import { describe, expect, test } from "vitest";

import { getChannelAdapter } from "./channel-adapters/registry.js";
import { buildInboundCapture, buildOutboundCaptureFromNormalized } from "./message-lifecycle.js";

describe("channel-independent message lifecycle", () => {
  test("normalizes one Telegram inbound provider message and binds its structural sender", () => {
    const adapter = getChannelAdapter("telegram")!;
    const normalized = adapter.normalizeInbound({
      content: "comecei Arkham Knight",
      timestamp: 1_788_000_000,
      messageId: "77",
      senderId: "8734062810",
      replyToId: "76",
      replyToBody: "qual jogo?",
      metadata: {
        senderName: "Gabs",
        senderUsername: "gabs_mutable",
        provider: "telegram",
        botToken: "must-not-leak",
        authorization: "must-not-leak",
      },
      providerUpdate: { id: "9001", kind: "message", secret: "must-not-leak" },
    }, {
      channelId: "telegram",
      accountId: "filomena",
      conversationId: "telegram:-1001234567890:topic:42",
      senderId: "8734062810",
    });

    expect(normalized).toMatchObject({
      direction: "inbound",
      providerMessageId: "77",
      content: "comecei Arkham Knight",
      conversation: {
        channel: "telegram",
        accountId: "filomena",
        nativeId: "-1001234567890",
        kind: "group",
        threadId: "42",
        namespace: "telegram-group-neg-1001234567890",
      },
      sender: {
        nativeId: "8734062810",
        stableId: "telegram:user:8734062810",
        displayName: "Gabs",
      },
      replyTo: { providerMessageId: "76", content: "qual jogo?" },
    });

    const capture = buildInboundCapture(normalized!);
    expect(capture.boundEntities).toHaveLength(1);
    expect(capture.boundEntities[0]).toMatchObject({
      role: "speaker",
      name: "telegram:user:8734062810",
      entity_type: "Person",
    });
    expect(capture.episodeName).toBe(`inbound_${capture.episodeUuid}`);
    expect(capture.referenceTime).toBe("2026-08-29T10:40:00.000Z");
    expect(capture.episodeBody).not.toContain("must-not-leak");
    expect(JSON.parse(capture.episodeBody)).toMatchObject({
      account_id: "filomena",
      conversation_jid: "-1001234567890",
      conversation_kind: "group",
      thread_id: "42",
      provider_message_id: "77",
    });
  });

  test("preserves an inbound episode without probabilistic Person binding when sender is unresolved", () => {
    const adapter = getChannelAdapter("telegram")!;
    const normalized = adapter.normalizeInbound({
      content: "channel post",
      timestamp: 1_788_000_000_000,
      messageId: "88",
      senderId: "anonymous-channel",
      metadata: { senderName: "Not identity" },
    }, {
      channelId: "telegram",
      accountId: "default",
      conversationId: "-1001234567890",
    });

    expect(normalized?.sender).toBeUndefined();
    expect(buildInboundCapture(normalized!).boundEntities).toEqual([]);
  });

  test("Telegram outbound DM uses only the structural peer as addressee", () => {
    const adapter = getChannelAdapter("telegram")!;
    const normalized = adapter.normalizeOutbound({
      to: "telegram:8734062810",
      content: "Helena comentou sobre Gabriel.",
      success: true,
      messageId: "91",
    }, {
      channelId: "telegram",
      accountId: "filomena",
      conversationId: "telegram:8734062810",
    }, { observedAt: new Date("2026-09-04T20:00:00.000Z") });

    expect(normalized?.structuralAddressee).toMatchObject({
      stableId: "telegram:user:8734062810",
    });
    const capture = buildOutboundCaptureFromNormalized(normalized!);
    expect(capture.boundEntities.map((entity) => entity.name)).toEqual([
      "agent:filomena",
      "telegram:user:8734062810",
    ]);
    expect(capture.boundEntities.some((entity) => entity.name === "Gabriel")).toBe(false);
  });

  test("Telegram outbound group without exact run correlation has no addressee", () => {
    const adapter = getChannelAdapter("telegram")!;
    const normalized = adapter.normalizeOutbound({
      to: "telegram:-1001234567890",
      content: "Professor, Helena comentou sobre Gabriel.",
      success: true,
      messageId: "92",
    }, {
      channelId: "telegram",
      accountId: "filomena",
      conversationId: "telegram:-1001234567890",
    });

    expect(normalized?.structuralAddressee).toBeUndefined();
    expect(buildOutboundCaptureFromNormalized(normalized!).boundEntities).toHaveLength(1);
  });

  test("adapters reject failed outbound deliveries and missing provider identity", () => {
    const adapter = getChannelAdapter("telegram")!;
    const context = { channelId: "telegram", conversationId: "8734062810" };
    expect(adapter.normalizeOutbound({ content: "oi", success: false, messageId: "1" }, context)).toBeUndefined();
    expect(adapter.normalizeOutbound({ content: "oi", success: true }, context)).toBeUndefined();
    expect(adapter.normalizeInbound({ content: "oi", timestamp: 1_788_000_000 }, context)).toBeUndefined();
  });
});
