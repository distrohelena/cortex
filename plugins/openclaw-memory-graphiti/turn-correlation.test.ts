import { describe, expect, test } from "vitest";

import { TurnCorrelationRegistry } from "./turn-correlation.js";
import { participantUuid } from "./passive-capture.js";

const GROUP_ID = "whatsapp-group-5511981042361-1589823314-g-us";
const GROUP_JID = "5511981042361-1589823314@g.us";

describe("reply_dispatch turn correlation", () => {
  test("binds one real run id to exact inbound provider identity and participant", () => {
    const registry = new TurnCorrelationRegistry();

    const correlation = registry.observeReplyDispatch({
      groupId: GROUP_ID,
      event: {
        runId: "run-gabriel",
        ctx: {
          Provider: "whatsapp",
          OriginatingTo: GROUP_JID,
          MessageSidFull: "inbound-provider-1",
          SenderId: "5511999999999:7@s.whatsapp.net",
          SenderE164: "+5511999999999",
          SenderName: "Gabriel",
        },
      },
    });

    expect(correlation).toMatchObject({
      runId: "run-gabriel",
      groupId: GROUP_ID,
      conversationJid: GROUP_JID,
      inboundProviderMessageId: "inbound-provider-1",
      participantUuid: participantUuid(GROUP_ID, "5511999999999@s.whatsapp.net"),
    });
    expect(registry.get("run-gabriel", GROUP_ID, GROUP_JID)).toEqual(correlation);
  });

  test("fails closed unless run id, provider message, sender and conversation coexist", () => {
    const registry = new TurnCorrelationRegistry();
    const base = {
      runId: "run-1",
      ctx: {
        Provider: "whatsapp",
        OriginatingTo: GROUP_JID,
        MessageSid: "inbound-1",
        SenderId: "5511999999999@s.whatsapp.net",
      },
    };

    expect(registry.observeReplyDispatch({ groupId: GROUP_ID, event: { ...base, runId: undefined } })).toBeUndefined();
    expect(registry.observeReplyDispatch({ groupId: GROUP_ID, event: { ...base, ctx: { ...base.ctx, MessageSid: undefined } } })).toBeUndefined();
    expect(registry.observeReplyDispatch({ groupId: GROUP_ID, event: { ...base, ctx: { ...base.ctx, SenderId: undefined } } })).toBeUndefined();
    expect(registry.observeReplyDispatch({ groupId: GROUP_ID, event: { ...base, ctx: { ...base.ctx, OriginatingTo: undefined } } })).toBeUndefined();
    expect(registry.size).toBe(0);
  });

  test("concurrent runs in the same session never exchange participants", () => {
    const registry = new TurnCorrelationRegistry();
    const observe = (runId: string, senderId: string, messageId: string) =>
      registry.observeReplyDispatch({
        groupId: GROUP_ID,
        event: {
          runId,
          sessionKey: "same-session",
          ctx: {
            Provider: "whatsapp",
            OriginatingTo: GROUP_JID,
            MessageSidFull: messageId,
            SenderId: senderId,
          },
        },
      });

    const gabriel = observe("run-a", "5511999999999@s.whatsapp.net", "msg-a");
    const marina = observe("run-b", "5511888888888@s.whatsapp.net", "msg-b");

    expect(registry.get("run-a", GROUP_ID, GROUP_JID)?.participantUuid).toBe(gabriel?.participantUuid);
    expect(registry.get("run-b", GROUP_ID, GROUP_JID)?.participantUuid).toBe(marina?.participantUuid);
    expect(registry.get("same-session", GROUP_ID, GROUP_JID)).toBeUndefined();
  });
});
