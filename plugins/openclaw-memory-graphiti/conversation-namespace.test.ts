import { describe, expect, test } from "vitest";

import {
  canonicalConversationGroupId,
  canonicalConversationNamespace,
  conversationIdentity,
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

  test("exposes conversation terminology while retaining the Graphiti group_id alias", () => {
    const input = { channel: "whatsapp", conversationId: REAL_GROUP_JID };
    expect(canonicalConversationNamespace(input)).toBe(canonicalConversationGroupId(input));
  });

  test("normalizes device-qualified DM JIDs without using account or display metadata", () => {
    const first = canonicalConversationGroupId({
      channel: "whatsapp",
      conversationId: "5511999999999:12@s.whatsapp.net",
      accountId: "old-account",
    });
    const second = canonicalConversationGroupId({
      channel: "whatsapp",
      conversationId: "5511999999999@c.us",
      accountId: "new-business-account",
    });

    expect(first).toBe("whatsapp-dm-5511999999999-s-whatsapp-net");
    expect(second).toBe(first);
  });

  test("keeps distinct DMs and a group with the same participant isolated", () => {
    const dmA = canonicalConversationGroupId({ channel: "whatsapp", conversationId: "5511111111111@s.whatsapp.net" });
    const dmB = canonicalConversationGroupId({ channel: "whatsapp", conversationId: "5522222222222@s.whatsapp.net" });
    const group = canonicalConversationGroupId({ channel: "whatsapp", conversationId: REAL_GROUP_JID });

    expect(new Set([dmA, dmB, group]).size).toBe(3);
  });

  test("changing the linked account never changes an existing group namespace", () => {
    const oldAccount = canonicalConversationGroupId({
      channel: "whatsapp",
      conversationId: REAL_GROUP_JID,
      accountId: "default",
    });
    const businessAccount = canonicalConversationGroupId({
      channel: "whatsapp",
      conversationId: REAL_GROUP_JID,
      accountId: "filomena-business",
    });
    expect(businessAccount).toBe(oldAccount);
    expect(businessAccount).toBe("whatsapp-group-5511981042361-1589823314-g-us");
  });

  test("native conversation JID wins over a stale or collapsed session key", () => {
    expect(canonicalConversationNamespace({
      channel: "whatsapp",
      conversationId: "5511944558218@s.whatsapp.net",
      sessionKey: `agent:main:whatsapp:group:${REAL_GROUP_JID}`,
    })).toBe("whatsapp-dm-5511944558218-s-whatsapp-net");
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

describe("conversationIdentity", () => {
  test("returns a native, typed WhatsApp conversation identity", () => {
    expect(conversationIdentity({
      channel: "WhatsApp",
      conversationId: "5511999999999:7@c.us",
      accountId: "default",
    })).toEqual({
      channel: "whatsapp",
      conversationJid: "5511999999999@s.whatsapp.net",
      kind: "dm",
    });
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
