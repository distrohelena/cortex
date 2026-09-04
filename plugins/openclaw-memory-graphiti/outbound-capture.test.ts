import { describe, expect, test } from "vitest";

import { buildOutboundCapture, filomenaUuid } from "./outbound-capture.js";
import { TurnCorrelationRegistry } from "./turn-correlation.js";

const GROUP_ID = "whatsapp-group-5511981042361-1589823314-g-us";
const GROUP_JID = "5511981042361-1589823314@g.us";

function correlation() {
  const registry = new TurnCorrelationRegistry();
  return registry.observeReplyDispatch({
    groupId: GROUP_ID,
    event: {
      runId: "run-gabriel",
      ctx: {
        Provider: "whatsapp",
        OriginatingTo: GROUP_JID,
        MessageSidFull: "inbound-provider-1",
        SenderId: "5511999999999@s.whatsapp.net",
        SenderName: "Gabriel",
      },
    },
  });
}

describe("outbound WhatsApp capture", () => {
  test("successful provider send builds one deterministic raw episode", () => {
    const input = {
      event: {
        to: GROUP_JID,
        content: "Filomena: Professor, a Marina comentou isso.",
        success: true,
        messageId: "outbound-provider-1",
        runId: "run-gabriel",
      },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_ID,
      observedAt: new Date("2026-09-04T18:30:00.000Z"),
      correlation: correlation(),
    };

    const first = buildOutboundCapture(input);
    const second = buildOutboundCapture(input);

    expect(first).toEqual(second);
    expect(first?.referenceTime).toBe("2026-09-04T18:30:00.000Z");
    expect(first?.boundEntities[0]).toMatchObject({
      uuid: filomenaUuid(GROUP_ID),
      role: "speaker",
      name: "agent:filomena",
      entity_type: "Agent",
    });
    expect(first?.boundEntities[1]).toMatchObject({
      role: "addressee",
      name: "whatsapp:5511999999999@s.whatsapp.net",
    });

    const body = JSON.parse(first!.episodeBody);
    expect(body.text).toBe(input.event.content);
    expect(body.timestamp).toEqual({
      iso: "2026-09-04T18:30:00.000Z",
      source: "message_sent_observed",
    });
    expect(body.reply).toBeNull();
    expect(body.originating).toEqual({
      inbound_provider_message_id: "inbound-provider-1",
      participant_uuid: first!.boundEntities[1].uuid,
      run_id: "run-gabriel",
    });
    expect(first?.episodeBody).not.toContain("Extraction Instructions");
  });

  test("failed or unidentified sends do not produce social episodes", () => {
    const base = {
      event: { to: GROUP_JID, content: "Filomena: oi", success: true, messageId: "out-1" },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_ID,
      observedAt: new Date("2026-09-04T18:30:00.000Z"),
    };

    expect(buildOutboundCapture({ ...base, event: { ...base.event, success: false } })).toBeUndefined();
    expect(buildOutboundCapture({ ...base, event: { ...base.event, messageId: undefined } })).toBeUndefined();
    expect(buildOutboundCapture({ ...base, event: { ...base.event, content: "" } })).toBeUndefined();
  });

  test("missing or mismatched run correlation preserves episode without addressee", () => {
    const capture = buildOutboundCapture({
      event: {
        to: GROUP_JID,
        content: "Filomena: oi",
        success: true,
        messageId: "out-no-correlation",
        runId: "unknown-run",
      },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_ID,
      observedAt: new Date("2026-09-04T18:30:00.000Z"),
      correlation: correlation(),
    });

    expect(capture?.boundEntities).toHaveLength(1);
    expect(capture?.boundEntities[0].role).toBe("speaker");
    expect(JSON.parse(capture!.episodeBody).originating).toBeNull();
  });

  test("third person text never changes the structurally correlated addressee", () => {
    const capture = buildOutboundCapture({
      event: {
        to: GROUP_JID,
        content: "Filomena: Gabriel, Helena disse que Marina terminou o jogo.",
        success: true,
        messageId: "out-third-person",
        runId: "run-gabriel",
      },
      context: { channelId: "whatsapp", conversationId: GROUP_JID },
      groupId: GROUP_ID,
      observedAt: new Date("2026-09-04T18:30:00.000Z"),
      correlation: correlation(),
    });

    expect(capture?.boundEntities).toHaveLength(2);
    expect(capture?.boundEntities[1].attributes.display_name_current).toBe("Gabriel");
    expect(capture?.boundEntities.some((entity) => entity.name === "Marina")).toBe(false);
  });
});
