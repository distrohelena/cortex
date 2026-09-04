import { describe, expect, test } from "vitest";

import { canonicalConversationNamespace } from "./conversation-namespace.mjs";
import { buildOutboundCapture, filomenaUuid } from "./outbound-capture.js";
import { buildPassiveCapture, normalizeWhatsAppSenderId, participantUuid } from "./passive-capture.js";

const GROUP_JID = "5511981042361-1589823314@g.us";
const GROUP_NAMESPACE = "whatsapp-group-5511981042361-1589823314-g-us";
const DM_JID = "5511999999999@s.whatsapp.net";
const DM_NAMESPACE = "whatsapp-dm-5511999999999-s-whatsapp-net";
const PARTICIPANT_STABLE_ID = "whatsapp:5511999999999@s.whatsapp.net";

describe("WhatsApp persistent identity golden compatibility", () => {
  test("freezes legacy namespaces and stable actor IDs byte-for-byte", () => {
    expect(canonicalConversationNamespace({ channel: "whatsapp", conversationId: GROUP_JID }))
      .toBe(GROUP_NAMESPACE);
    expect(canonicalConversationNamespace({ channel: "whatsapp", conversationId: DM_JID }))
      .toBe(DM_NAMESPACE);
    expect(normalizeWhatsAppSenderId("5511999999999:4@c.us")?.canonicalId)
      .toBe("5511999999999@s.whatsapp.net");
    expect(`whatsapp:${normalizeWhatsAppSenderId("5511999999999:4@c.us")?.canonicalId}`)
      .toBe(PARTICIPANT_STABLE_ID);
  });

  test("freezes canonical participant and Filomena Agent UUIDs", () => {
    expect(participantUuid(GROUP_NAMESPACE, "5511999999999@s.whatsapp.net"))
      .toBe("a20b8f6b-d0c7-5f01-a766-3773f806ae6b");
    expect(filomenaUuid(GROUP_NAMESPACE))
      .toBe("7da4d33e-c475-5446-bd7a-0c0f0bd25178");
  });

  test("freezes inbound and outbound episode UUIDs", () => {
    const inbound = buildPassiveCapture({
      event: {
        content: "eu gosto de Shadow of Mordor",
        timestamp: 1_787_920_245,
        messageId: "3EB0ABC",
        senderId: "5511999999999:4@s.whatsapp.net",
        metadata: { senderName: "Gabriel", senderE164: "+55 11 99999-9999" },
      },
      context: {
        channelId: "whatsapp",
        accountId: "default",
        conversationId: GROUP_JID,
      },
      groupId: GROUP_NAMESPACE,
    });
    const outbound = buildOutboundCapture({
      event: {
        to: GROUP_JID,
        content: "Filomena: Professor, a Marina comentou isso.",
        success: true,
        messageId: "outbound-provider-1",
        runId: "run-gabriel",
      },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_NAMESPACE,
      observedAt: new Date("2026-09-04T18:30:00.000Z"),
    });

    expect(inbound?.episodeUuid).toBe("827e98fb-1456-524d-ab68-19a8b61a8049");
    expect(outbound?.episodeUuid).toBe("8770f1d3-045e-520a-b43e-23f9792edf61");
    expect(inbound?.boundEntity.name).toBe(PARTICIPANT_STABLE_ID);
    expect(outbound?.boundEntities[0].name).toBe("agent:filomena");
  });

  test("keeps accountId out of persistent identity", () => {
    const oldAccount = canonicalConversationNamespace({
      channel: "whatsapp",
      accountId: "old",
      conversationId: GROUP_JID,
    });
    const renamedAccount = canonicalConversationNamespace({
      channel: "whatsapp",
      accountId: "filomena-business",
      conversationId: GROUP_JID,
    });

    expect(oldAccount).toBe(GROUP_NAMESPACE);
    expect(renamedAccount).toBe(GROUP_NAMESPACE);
    expect(filomenaUuid(oldAccount)).toBe(filomenaUuid(renamedAccount));
  });
});
