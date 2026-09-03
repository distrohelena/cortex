import test from "node:test";
import assert from "node:assert/strict";
import { shouldSuppressDispatch } from "./guard.js";

const config = {
  channel: "whatsapp",
  groupId: "5511981042361-1589823314@g.us",
  trigger: "@Filomena",
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
