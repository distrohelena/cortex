import { describe, expect, test } from "vitest";

import {
  buildPassiveCapture,
  normalizeWhatsAppSenderId,
  participantUuid,
} from "./passive-capture.js";

const GROUP_ID = "whatsapp-group-5511981042361-1589823314-g-us";
const GROUP_JID = "5511981042361-1589823314@g.us";

describe("passive WhatsApp capture", () => {
  test("builds one deterministic episode with complete provenance", () => {
    const input = {
      event: {
        content: "eu gosto de Shadow of Mordor",
        timestamp: 1_787_920_245,
        messageId: "3EB0ABC",
        senderId: "5511999999999:4@s.whatsapp.net",
        replyToId: "quoted-short",
        replyToIdFull: "quoted-full",
        replyToBody: "qual jogo?",
        replyToSender: "5511888888888@s.whatsapp.net",
        replyToIsQuote: true,
        metadata: {
          senderName: "Gabriel",
          senderE164: "+55 11 99999-9999",
          provider: "whatsapp",
        },
      },
      context: {
        channelId: "whatsapp",
        conversationId: GROUP_JID,
        messageId: "3EB0ABC",
        senderId: "5511999999999:4@s.whatsapp.net",
      },
      groupId: GROUP_ID,
    };

    const first = buildPassiveCapture(input);
    const second = buildPassiveCapture(input);

    expect(first).toEqual(second);
    expect(first?.episodeUuid).toBe("827e98fb-1456-524d-ab68-19a8b61a8049");
    expect(first?.referenceTime).toBe("2026-08-28T12:30:45.000Z");
    expect(first?.boundEntity.uuid).toBe("a20b8f6b-d0c7-5f01-a766-3773f806ae6b");
    expect(first?.boundEntity.uuid).toBe(participantUuid(GROUP_ID, "5511999999999@s.whatsapp.net"));
    expect(first?.boundEntity.name).toBe("whatsapp:5511999999999@s.whatsapp.net");
    expect(first?.boundEntity.attributes).toMatchObject({
      stable_id: "whatsapp:5511999999999@s.whatsapp.net",
      display_name_current: "Gabriel",
      aliases: ["Gabriel"],
    });

    const body = JSON.parse(first!.episodeBody);
    expect(body.text).toBe("eu gosto de Shadow of Mordor");
    expect(body.timestamp.original).toBe(1_787_920_245);
    expect(body.conversation_jid).toBe(GROUP_JID);
    expect(body.provider_message_id).toBe("3EB0ABC");
    expect(body.sender.raw_id).toBe("5511999999999:4@s.whatsapp.net");
    expect(body.sender.canonical_id).toBe("5511999999999@s.whatsapp.net");
    expect(body.reply).toEqual({
      id: "quoted-short",
      id_full: "quoted-full",
      body: "qual jogo?",
      sender: "5511888888888@s.whatsapp.net",
      is_quote: true,
    });
  });

  test("normalizes documented PN/device forms but never guesses LID from a name", () => {
    expect(normalizeWhatsAppSenderId("5511999999999:12@s.whatsapp.net")).toEqual({
      canonicalId: "5511999999999@s.whatsapp.net",
      source: "sender_id",
      unresolvedAlternate: false,
    });
    expect(normalizeWhatsAppSenderId("5511999999999@c.us")).toMatchObject({
      canonicalId: "5511999999999@s.whatsapp.net",
    });
    expect(normalizeWhatsAppSenderId("123456789@lid")).toEqual({
      canonicalId: "123456789@lid",
      source: "sender_id",
      unresolvedAlternate: true,
    });
    expect(normalizeWhatsAppSenderId("123456789@lid", "+5511999999999")).toEqual({
      canonicalId: "5511999999999@s.whatsapp.net",
      source: "sender_e164",
      unresolvedAlternate: false,
    });
  });

  test("participant identity ignores display names and is scoped by group", () => {
    const sender = "5511999999999@s.whatsapp.net";
    const uuid = participantUuid(GROUP_ID, sender);

    expect(participantUuid(GROUP_ID, sender)).toBe(uuid);
    expect(participantUuid("whatsapp-group-other-g-us", sender)).not.toBe(uuid);
    expect(participantUuid(GROUP_ID, "5511888888888@s.whatsapp.net")).not.toBe(uuid);
  });

  test("fails closed when identity, provider ID, timestamp, or text is missing", () => {
    const base = {
      event: { content: "oi", timestamp: 1_787_920_245, messageId: "m1", senderId: "1@s.whatsapp.net" },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_ID,
    };

    expect(buildPassiveCapture({ ...base, event: { ...base.event, messageId: undefined } })).toBeUndefined();
    expect(buildPassiveCapture({ ...base, event: { ...base.event, timestamp: undefined } })).toBeUndefined();
    expect(buildPassiveCapture({ ...base, event: { ...base.event, senderId: undefined } })).toBeUndefined();
    expect(buildPassiveCapture({ ...base, event: { ...base.event, content: "" } })).toBeUndefined();
  });
});
