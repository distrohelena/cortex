import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import plugin from "./index.ts";

type HookHandler = (
  event: Record<string, unknown>,
  context: Record<string, unknown>,
) => unknown;

type HookRegistration = {
  name: string;
  handler: HookHandler;
  options?: Record<string, unknown>;
};

function registerGuard(pluginConfig: Record<string, unknown>) {
  const registrations: HookRegistration[] = [];
  const logs = { debug: [] as string[], warn: [] as string[] };

  plugin.register({
    pluginConfig,
    resolvePath(input: string) {
      return input;
    },
    logger: {
      debug(message: string) {
        logs.debug.push(message);
      },
      info() {},
      warn(message: string) {
        logs.warn.push(message);
      },
      error() {},
    },
    on(name: string, handler: HookHandler, options?: Record<string, unknown>) {
      registrations.push({ name, handler, options });
    },
  } as never);

  const handlers = Object.fromEntries(
    registrations.map((registration) => [registration.name, registration.handler]),
  ) as Record<string, HookHandler>;
  return { handlers, logs, registrations };
}

function withTemporaryPlugin(
  run: (params: {
    config: Record<string, unknown>;
    filePath: string;
    groupId: string;
  }) => void,
) {
  const directory = mkdtempSync(join(tmpdir(), "filomena-plugin-"));
  const filePath = join(directory, "state.json");
  const groupId = "5511981042361-1589823314@g.us";
  try {
    run({
      filePath,
      groupId,
      config: {
        channel: "whatsapp",
        groupId,
        trigger: "@Filomena",
        responsePrefix: "Filomena: ",
        stateFile: filePath,
        maxTrackedMessageIds: 512,
      },
    });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test("registers dispatch, outbound rewrite, and sent-observer hooks", () => {
  withTemporaryPlugin(({ config }) => {
    const { registrations } = registerGuard(config);
    assert.deepEqual(
      registrations.map((entry) => entry.name),
      ["before_dispatch", "message_sending", "message_sent"],
    );
    assert.equal(registrations[0]?.options?.priority, 100);
    assert.equal(registrations[1]?.options?.priority, -100);
  });
});

test("tracks a successful Filomena send and admits replies across restart", () => {
  withTemporaryPlugin(({ config, filePath, groupId }) => {
    const first = registerGuard(config);
    const inbound = {
      channel: "whatsapp",
      isGroup: true,
      body: "continua",
      replyToId: "bot-1",
    };
    const inboundContext = { conversationId: groupId };

    assert.deepEqual(
      first.handlers.before_dispatch!(inbound, inboundContext),
      { handled: true },
    );

    first.handlers.message_sent!(
      {
        to: groupId,
        content: "Filomena: Olá",
        success: true,
        messageId: "bot-1",
      },
      { channelId: "whatsapp", conversationId: groupId },
    );

    assert.equal(first.handlers.before_dispatch!(inbound, inboundContext), undefined);
    assert.match(readFileSync(filePath, "utf8"), /bot-1/);

    const restarted = registerGuard(config);
    assert.equal(restarted.handlers.before_dispatch!(inbound, inboundContext), undefined);
  });
});

test("prefixes only protected-group outbound text", () => {
  withTemporaryPlugin(({ config, groupId }) => {
    const { handlers } = registerGuard(config);
    assert.deepEqual(
      handlers.message_sending!(
        { to: groupId, content: "Olá" },
        { channelId: "whatsapp", conversationId: groupId },
      ),
      { content: "Filomena: Olá" },
    );
    assert.equal(
      handlers.message_sending!(
        { to: "5511981042361", content: "Olá" },
        { channelId: "whatsapp", conversationId: "5511981042361" },
      ),
      undefined,
    );
  });
});

test("does not track failed sends or successful sends to other targets", () => {
  withTemporaryPlugin(({ config, groupId }) => {
    const { handlers } = registerGuard(config);
    const context = { channelId: "whatsapp", conversationId: groupId };

    handlers.message_sent!(
      { to: groupId, content: "Falhou", success: false, messageId: "failed-1" },
      context,
    );
    handlers.message_sent!(
      { to: "other@g.us", content: "Outro", success: true, messageId: "other-1" },
      { channelId: "whatsapp", conversationId: "other@g.us" },
    );

    for (const replyToId of ["failed-1", "other-1"]) {
      assert.deepEqual(
        handlers.before_dispatch!(
          { channel: "whatsapp", isGroup: true, body: "continua", replyToId },
          { conversationId: groupId },
        ),
        { handled: true },
      );
    }
  });
});

test("lets explicit Filomena messages continue to the model", () => {
  withTemporaryPlugin(({ config, groupId }) => {
    const { handlers } = registerGuard(config);
    const result = handlers.before_dispatch!(
      { channel: "whatsapp", isGroup: true, body: "@Filomena oi" },
      { conversationId: groupId },
    );
    assert.equal(result, undefined);
  });
});

test("rejects invalid plugin configuration during registration", () => {
  assert.throws(() => registerGuard({}), /groupId must be configured/);
  assert.throws(() => registerGuard({ groupId: "   " }), /groupId must be configured/);
  assert.throws(
    () => registerGuard({ groupId: "group@g.us", responsePrefix: "" }),
    /responsePrefix must not be blank/,
  );
  assert.throws(
    () => registerGuard({ groupId: "group@g.us", stateFile: "   " }),
    /stateFile must not be blank/,
  );
  assert.throws(
    () => registerGuard({ groupId: "group@g.us", maxTrackedMessageIds: 0 }),
    /maxTrackedMessageIds must be a positive integer/,
  );
});
