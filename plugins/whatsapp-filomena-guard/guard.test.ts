import test from "node:test";
import assert from "node:assert/strict";
import { prefixProtectedOutbound, shouldSuppressDispatch } from "./guard.js";

const config = {
  channel: "whatsapp",
  responsePrefix: "Filomena: ",
  respond: {
    direct: {
      mode: "allowlist",
      allowFrom: ["5511944558218@s.whatsapp.net"],
    },
    groups: {
      "120363012345678901@g.us": {
        trigger: "@Filomena",
        triggerAliases: ["@101211743244331"],
        allowRepliesToFilomena: true,
        prefix: true,
      },
    },
  },
};

const groupId = "120363012345678901@g.us";
const protectedContext = { conversationId: groupId };

test("allows a leading trigger", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "@Filomena oi" },
      protectedContext,
      config,
    ),
    false,
  );
});

test("allows leading whitespace and case-insensitive trigger", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "  @filomena\nme ajuda" },
      protectedContext,
      config,
    ),
    false,
  );
});

test("suppresses a trigger that is not leading", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "oi @Filomena" },
      protectedContext,
      config,
    ),
    true,
  );
});

test("suppresses an implicit reply without a trigger", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "continua a resposta" },
      protectedContext,
      config,
    ),
    true,
  );
});

test("allows an implicit reply whose replyToId belongs to Filomena", () => {
  assert.equal(
    shouldSuppressDispatch(
      {
        channel: "whatsapp",
        isGroup: true,
        body: "continua a resposta",
        replyToId: "bot-1",
      },
      protectedContext,
      config,
      { has: (id: unknown) => id === "bot-1" },
    ),
    false,
  );
});

test("allows replyToIdFull and reply metadata supplied on hook context", () => {
  const registry = { has: (id: unknown) => id === "bot-full" };
  assert.equal(
    shouldSuppressDispatch(
      {
        channel: "whatsapp",
        isGroup: true,
        body: "continua",
        replyToIdFull: "bot-full",
      },
      protectedContext,
      config,
      registry,
    ),
    false,
  );
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "continua" },
      { ...protectedContext, replyToId: "bot-full" },
      config,
      registry,
    ),
    false,
  );
});

test("suppresses replies to untracked or blank IDs", () => {
  const registry = { has: (id: unknown) => id === "bot-1" };
  for (const replyToId of ["manual-helena-1", "", "   "]) {
    assert.equal(
      shouldSuppressDispatch(
        { channel: "whatsapp", isGroup: true, body: "continua", replyToId },
        protectedContext,
        config,
        registry,
      ),
      true,
    );
  }
});

test("suppresses a missing body in the protected group", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true },
      protectedContext,
      config,
    ),
    true,
  );
});

test("suppresses a present non-string body in the protected group", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: 42 },
      protectedContext,
      config,
    ),
    true,
  );
});

test("suppresses a trigger with trailing letters", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "@Filomenaaa oi" },
      protectedContext,
      config,
    ),
    true,
  );
});

test("allows an allowlisted DM by canonical peer identity", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: false, body: "oi" },
      { conversationId: "5511944558218:9@c.us", senderId: "5511944558218@s.whatsapp.net" },
      config,
    ),
    false,
  );
});

test("allows a configured leading provider-native trigger alias", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "@101211743244331 oi amiga" },
      protectedContext,
      config,
    ),
    false,
  );
});

test("matches the raw command content when the agent body contains context", () => {
  assert.equal(
    shouldSuppressDispatch(
      {
        channel: "whatsapp",
        isGroup: true,
        content: "@101211743244331 helloo",
        body: "[Recent chat context]\nCarlos: outra mensagem\n[/Recent chat context]\n\nHelena: @101211743244331 helloo",
      },
      protectedContext,
      config,
    ),
    false,
  );
});

test("suppresses unknown DMs and groups while leaving Telegram untouched", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: false, body: "oi" },
      { conversationId: "5511888888888@s.whatsapp.net", senderId: "5511888888888@s.whatsapp.net" },
      config,
    ),
    true,
  );
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "@Filomena oi" },
      { conversationId: "another-group@g.us" },
      config,
    ),
    true,
  );
  assert.equal(
    shouldSuppressDispatch(
      { channel: "telegram", isGroup: true, body: "oi" },
      protectedContext,
      config,
    ),
    false,
  );
});

test("prefixes outbound text only in a configured group that requests it", () => {
  assert.equal(
    prefixProtectedOutbound(
      { to: groupId, content: "Olá" },
      { channelId: "whatsapp", conversationId: groupId },
      config,
    ),
    "Filomena: Olá",
  );
  assert.equal(
    prefixProtectedOutbound(
      { to: groupId, content: "Filomena: Olá" },
      { channelId: "whatsapp", conversationId: groupId },
      config,
    ),
    "Filomena: Olá",
  );
});

test("does not prefix DMs, unknown groups, other channels, or empty text", () => {
  const cases = [
    {
      event: { to: "15551234567", content: "Olá" },
      context: { channelId: "whatsapp", conversationId: "15551234567" },
      expected: "Olá",
    },
    {
      event: { to: "another-group@g.us", content: "Olá" },
      context: { channelId: "whatsapp", conversationId: "another-group@g.us" },
      expected: "Olá",
    },
    {
      event: { to: groupId, content: "Olá" },
      context: { channelId: "telegram", conversationId: groupId },
      expected: "Olá",
    },
    {
      event: { to: groupId, content: "" },
      context: { channelId: "whatsapp", conversationId: groupId },
      expected: "",
    },
  ];

  for (const { event, context, expected } of cases) {
    assert.equal(prefixProtectedOutbound(event, context, config), expected);
  }
});
