import test from "node:test";
import assert from "node:assert/strict";
import { prefixProtectedOutbound, shouldSuppressDispatch } from "./guard.js";

const config = {
  channel: "whatsapp",
  groupId: "5511981042361-1589823314@g.us",
  trigger: "@Filomena",
  responsePrefix: "Filomena: ",
};

const protectedContext = { conversationId: config.groupId };

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

test("leaves a direct WhatsApp message untouched", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: false, body: "@ai oi" },
      { conversationId: "5511981042361" },
      config,
    ),
    false,
  );
});

test("leaves another group and Telegram untouched", () => {
  assert.equal(
    shouldSuppressDispatch(
      { channel: "whatsapp", isGroup: true, body: "oi" },
      { conversationId: "another-group@g.us" },
      config,
    ),
    false,
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

test("prefixes outbound text only in the protected WhatsApp group", () => {
  assert.equal(
    prefixProtectedOutbound(
      { to: config.groupId, content: "Olá" },
      { channelId: "whatsapp", conversationId: config.groupId },
      config,
    ),
    "Filomena: Olá",
  );
  assert.equal(
    prefixProtectedOutbound(
      { to: config.groupId, content: "Filomena: Olá" },
      { channelId: "whatsapp", conversationId: config.groupId },
      config,
    ),
    "Filomena: Olá",
  );
});

test("does not prefix private chats, other groups, other channels, or empty text", () => {
  const cases = [
    {
      event: { to: "5511981042361", content: "Olá" },
      context: { channelId: "whatsapp", conversationId: "5511981042361" },
      expected: "Olá",
    },
    {
      event: { to: "another-group@g.us", content: "Olá" },
      context: { channelId: "whatsapp", conversationId: "another-group@g.us" },
      expected: "Olá",
    },
    {
      event: { to: config.groupId, content: "Olá" },
      context: { channelId: "telegram", conversationId: config.groupId },
      expected: "Olá",
    },
    {
      event: { to: config.groupId, content: "" },
      context: { channelId: "whatsapp", conversationId: config.groupId },
      expected: "",
    },
  ];

  for (const { event, context, expected } of cases) {
    assert.equal(prefixProtectedOutbound(event, context, config), expected);
  }
});
