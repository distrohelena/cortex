# WhatsApp Filomena Implicit Replies Design

## Goal

In the WhatsApp group `5511981042361-1589823314@g.us`, dispatch to Filomena when either the current message starts with `@Filomena` or the current message is a direct WhatsApp reply to a prior Filomena response. Continue suppressing every other non-trigger message. Preserve direct-message and other-channel behavior.

## Design

Keep the guard as an external source plugin and leave the official WhatsApp bundle untouched. The WhatsApp channel performs its built-in group mention admission before `before_dispatch`. A reply to another participant without a Filomena mention is rejected before the guard runs; an implicit reply admitted to `before_dispatch` carries `replyToId` or `replyToIdFull`.

The pure policy will therefore allow a protected-group dispatch when either:

1. The current body, after leading whitespace, starts with the configured trigger followed by whitespace or end of input, case-insensitively; or
2. The hook event contains a non-empty `replyToId` or `replyToIdFull`.

Messages with neither signal remain silently handled by the plugin. WhatsApp direct messages, including self-chat `@ai`, and all other groups and channels remain outside this policy.

## Prompt Behavior

The group system prompt will define the current request as follows:

- For an explicit trigger, the request is the text after `@Filomena`.
- For an implicit reply, the request is the entire current message.
- Recent group messages and the quoted Filomena response are supporting context only. They must not replace or reinterpret the current request unless the user explicitly asks for analysis of that context.
- Graphiti remains reserved for requests that genuinely need older or broader history.

## Testing

Add regression coverage proving that:

- A protected-group event with no explicit trigger but with `replyToId` is allowed.
- `replyToIdFull` also allows the reply when the short ID is unavailable.
- Whitespace-only reply IDs do not bypass the guard.
- A non-trigger message with no reply metadata remains suppressed.
- Existing explicit-trigger, DM, other-group, and other-channel cases continue to pass.
- The registered `before_dispatch` hook returns `undefined` for an admitted implicit reply and `{ handled: true }` for an unrelated message.

## Operational Changes

Update only the external plugin source/tests/metadata and the target group's system prompt. Validate the OpenClaw configuration and plugin discovery without starting or restarting the gateway. The already-clean group session remains clean; no private or Graphiti session is removed.
