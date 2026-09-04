# Passive WhatsApp memory capture hook

## Verified OpenClaw contract

The installed OpenClaw version is 2026.8.1. Its local runtime describes
`message_received` as an observation hook emitted once for an accepted inbound
turn and independently of reply dispatch. The dispatcher emits it while
preparing the operation and only runs `before_dispatch` afterward. Therefore a
`whatsapp-filomena-guard` decision that suppresses agent execution does not
suppress this observation hook.

Relevant installed runtime sources:

- `dist/dispatch-from-config-D-R0cBMI.js`: `emitMessageReceivedHooks`, including
  the comment "independent of reply dispatch", and the call before dispatch.
- `dist/hook-types-BSxU_61y.d.ts`: `PluginHookMessageContext` and
  `PluginHookMessageReceivedEvent`.
- `dist/message-hook-mappers-BvcG8vBF.js`: mapping of sender/provider metadata.

The hook exposes:

- channel ID and native conversation ID in hook context;
- canonical session key and per-turn run ID;
- provider message ID;
- sender ID;
- sender name, username, and E.164 identity when supplied by WhatsApp, under
  `event.metadata`;
- original timestamp;
- reply target, quoted body, and quoted sender;
- text and staged media facts.

## Minimal implementation for the next round

Register one `message_received` handler in the memory plugin. For every accepted
WhatsApp text message:

1. Resolve the canonical group with the shared `channel + JID` helper.
2. Build one structured `source: message` episode containing the original
   message only, including speaker identity, timestamp, message ID, reply
   metadata, and text.
3. Derive episode name and UUID from
   `channel + conversation JID + provider message ID`.
4. Submit the episode to that conversation's Graphiti queue.
5. Associate the resolved fragment with the same SpiceDB group.

The deterministic provider identity makes replay idempotent and avoids sliding
windows. The existing `agent_end` batch capture must be disabled when this hook
is activated; the two mechanisms must never run together.

`inbound_claim` is not recommended: although it exposes similar metadata, it is
part of ownership/handling arbitration. `message_received` is purpose-built for
passive observation and cannot cause Filomena to reply.

`message_sent` may later capture successful Filomena output if bot-authored
history proves useful, but it is not required for remembering group events and
is intentionally outside this change.

