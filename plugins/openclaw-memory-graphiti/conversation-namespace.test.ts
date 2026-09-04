import { describe, expect, test } from "vitest";

import {
  canonicalConversationGroupId,
  conversationIdentityFromSessionKey,
} from "./conversation-namespace.mjs";

const REAL_GROUP_JID = "5511981042361-1589823314@g.us";

describe("canonicalConversationGroupId", () => {
  test("derives the stable namespace for the real WhatsApp group JID", () => {
    expect(
      canonicalConversationGroupId({
        channel: "WhatsApp",
        conversationId: REAL_GROUP_JID,
      }),
    ).toBe("whatsapp-group-5511981042361-1589823314-g-us");
  });

  test("distinguishes direct conversations from groups", () => {
    expect(
      canonicalConversationGroupId({
        channel: "whatsapp",
        conversationId: "5511981042361@s.whatsapp.net",
      }),
    ).toBe("whatsapp-dm-5511981042361-s-whatsapp-net");
  });

  test("is deterministic and normalizes case", () => {
    const first = canonicalConversationGroupId({
      channel: "WHATSAPP",
      conversationId: "5511981042361-1589823314@G.US",
    });
    const second = canonicalConversationGroupId({
      channel: "whatsapp",
      conversationId: REAL_GROUP_JID,
    });
    expect(first).toBe(second);
  });

  test("falls back to the canonical OpenClaw session key", () => {
    expect(
      canonicalConversationGroupId({
        sessionKey: `agent:main:whatsapp:group:${REAL_GROUP_JID}`,
      }),
    ).toBe("whatsapp-group-5511981042361-1589823314-g-us");
  });

  test("fails closed without channel and conversation identity", () => {
    expect(() => canonicalConversationGroupId({ sessionKey: "agent:main:main" })).toThrow(
      /conversation/i,
    );
  });
});

describe("conversationIdentityFromSessionKey", () => {
  test("extracts channel, kind, and the full JID", () => {
    expect(
      conversationIdentityFromSessionKey(`agent:main:whatsapp:group:${REAL_GROUP_JID}`),
    ).toEqual({
      channel: "whatsapp",
      conversationId: REAL_GROUP_JID,
      kind: "group",
    });
  });
});

