import assert from "node:assert/strict";
import test from "node:test";

import plugin from "./index.ts";

type HookHandler = (event: Record<string, unknown>, context: Record<string, unknown>) => unknown;

function registerGuard(pluginConfig: Record<string, unknown>) {
  const registrations: Array<{
    name: string;
    handler: HookHandler;
    options?: Record<string, unknown>;
  }> = [];
  const logs: string[] = [];

  plugin.register({
    pluginConfig,
    logger: {
      debug(message: string) {
        logs.push(message);
      },
      info() {},
      warn() {},
      error() {},
    },
    on(name: string, handler: HookHandler, options?: Record<string, unknown>) {
      registrations.push({ name, handler, options });
    },
  } as never);

  assert.equal(registrations.length, 1);
  assert.equal(registrations[0]?.name, "before_dispatch");
  assert.equal(registrations[0]?.options?.priority, 100);
  return { handler: registrations[0]!.handler, logs };
}

test("registers one before_dispatch hook that silently claims non-trigger replies", () => {
  const { handler, logs } = registerGuard({
    groupId: "5511981042361-1589823314@g.us",
  });
  const result = handler(
    { channel: "whatsapp", isGroup: true, body: "resposta sem gatilho" },
    { conversationId: "5511981042361-1589823314@g.us" },
  );
  assert.deepEqual(result, { handled: true });
  assert.equal(logs.length, 1);
});

test("lets explicit Filomena messages continue to the model", () => {
  const { handler } = registerGuard({
    groupId: "5511981042361-1589823314@g.us",
  });
  const result = handler(
    { channel: "whatsapp", isGroup: true, body: "@Filomena oi" },
    { conversationId: "5511981042361-1589823314@g.us" },
  );
  assert.equal(result, undefined);
});

test("rejects missing or blank group configuration during registration", () => {
  assert.throws(() => registerGuard({}), /groupId must be configured/);
  assert.throws(() => registerGuard({ groupId: "   " }), /groupId must be configured/);
});
