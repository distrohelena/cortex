import assert from "node:assert/strict";
import test from "node:test";

import { shouldRespond } from "./policy.js";

test("evaluates every common response mode from normalized signals", () => {
  assert.equal(shouldRespond("disabled", {}), false);
  assert.equal(shouldRespond("allow", {}), true);
  assert.equal(shouldRespond("mention", { mentioned: true }), true);
  assert.equal(shouldRespond("mention", { replyToFilomena: true }), false);
  assert.equal(shouldRespond("reply-to-filomena", { replyToFilomena: true }), true);
  assert.equal(shouldRespond("reply-to-filomena", { mentioned: true }), false);
  assert.equal(shouldRespond("mention-or-reply", { mentioned: true }), true);
  assert.equal(shouldRespond("mention-or-reply", { replyToFilomena: true }), true);
  assert.equal(shouldRespond("mention-or-reply", {}), false);
});

test("unknown policy modes fail closed", () => {
  assert.equal(shouldRespond("anything-else", { mentioned: true, replyToFilomena: true }), false);
});
