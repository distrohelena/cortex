import assert from "node:assert/strict";
import test from "node:test";

import { shouldSuppressTelegramDispatch } from "./guard.js";

const config = {
  channel: "telegram",
  respond: {
    direct: { mode: "allow", chats: [] },
    groups: { "-1001234567890": { mode: "mention-or-reply" } },
  },
};

test("uses Telegram structural mention signal, never mutable username text", () => {
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: true, wasMentioned: true, body: "oi" },
    { conversationId: "-1001234567890" }, config), false);
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: true, wasMentioned: false, body: "@filomena oi" },
    { conversationId: "-1001234567890" }, config), true);
});

test("admits a structural reply only when its provider ID belongs to Filomena", () => {
  const registry = { has: (scope, id) => scope === "telegram:-1001234567890" && id === "bot-1" };
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: true, replyToId: "bot-1" },
    { conversationId: "-1001234567890" }, config, registry), false);
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: true, replyToId: "someone-else" },
    { conversationId: "-1001234567890" }, config, registry), true);
});

test("allows configured DMs and suppresses unknown groups", () => {
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: false }, { conversationId: "8734062810" }, config), false);
  assert.equal(shouldSuppressTelegramDispatch(
    { channel: "telegram", isGroup: true, wasMentioned: true }, { conversationId: "-999" }, config), true);
});
