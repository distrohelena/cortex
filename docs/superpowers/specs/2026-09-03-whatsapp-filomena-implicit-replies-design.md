# WhatsApp Filomena Implicit Replies and Identity Design

## Goal

In the WhatsApp group `120363012345678901@g.us`, dispatch to Filomena only when either:

1. The current message starts with `@Filomena`; or
2. The current message directly replies to a WhatsApp message previously sent by OpenClaw/Filomena.

Every Filomena response in that group must begin with `Filomena: ` so participants can distinguish it from Helena's manual messages. Direct messages, including Helena's self-chat `@ai` behavior, and every other group or channel must remain unchanged.

## Constraint: Shared WhatsApp Identity

Filomena sends through Helena's linked WhatsApp account. WhatsApp sender identity therefore cannot distinguish a message manually written by Helena from one sent by OpenClaw. Reply metadata alone is insufficient: replying to any Helena message can look like a reply to the bot.

The plugin must instead maintain its own bounded registry of outbound WhatsApp message IDs observed through OpenClaw's successful `message_sent` hook. A reply is implicit only when its quoted message ID matches that registry.

## Plugin Behavior

Keep the guard as an external source plugin and leave the official compiled WhatsApp bundle untouched.

### Inbound admission

For the protected group, the `before_dispatch` hook allows a message when either:

- Its body, after leading whitespace, starts with the configured `@Filomena` trigger followed by whitespace or end of input, case-insensitively; or
- Its normalized `replyToId` or `replyToIdFull` matches a message ID recorded by the plugin as a successful Filomena outbound message in this same group.

All other messages in the protected group are silently handled and never reach the model. In particular, replying to a message Helena typed manually does not trigger Filomena.

### Outbound identity prefix

For outbound messages in the protected group, the `message_sending` hook prepends the configured `Filomena: ` prefix. If content already starts with that exact prefix, it is left unchanged to prevent duplication.

The prefix is scoped to the protected group inside the plugin. The global WhatsApp `responsePrefix` setting is not used because it would also alter private-chat replies.

### Outbound ID tracking

After a protected-group send succeeds, the `message_sent` hook records its non-empty `messageId`. Failed sends and sends without a message ID are ignored. The registry de-duplicates IDs and retains only the newest 512 by default.

The plugin persists the registry to a configured JSON state file under OpenClaw's state directory. It loads this file during registration so implicit replies keep working across gateway and PC restarts. A missing state file means an empty registry. A malformed or unreadable file produces a warning and an empty in-memory registry without preventing explicit `@Filomena` use.

Messages sent before this tracking version is installed are not automatically recognized as Filomena messages. Once Filomena sends a new response after the update, replies to that response are eligible.

## Prompt Behavior

The target group's system prompt defines the current request as follows:

- For an explicit trigger, the request is the text after the leading `@Filomena` trigger.
- For a verified implicit reply, the request is the entire current message.
- The quoted Filomena response and recent group messages are supporting context only. They must never replace or reinterpret the current request unless the user explicitly asks to analyze that context.
- Graphiti is used only when the current request genuinely needs older or broader history.

The prefix is enforced by the outbound plugin hook, not merely requested in the prompt.

## Configuration

Extend the existing plugin configuration with:

- `responsePrefix`, defaulting to `Filomena: `;
- `stateFile`, set to an absolute JSON path under `C:\Users\Helena\.openclaw`;
- `maxTrackedMessageIds`, defaulting to `512`.

Retain the existing WhatsApp channel, protected group ID, and explicit trigger settings.

## Testing

Add regression coverage proving that:

- The existing explicit `@Filomena` trigger remains allowed.
- A reply to a tracked Filomena outbound ID is allowed through either `replyToId` or `replyToIdFull`.
- A reply to an untracked ID, including a manually sent Helena message, is suppressed.
- Empty and whitespace-only reply IDs do not bypass the guard.
- A successful protected-group `message_sent` event records and persists its ID.
- Failed sends, missing IDs, other groups, direct messages, and other channels are not tracked.
- State reload restores tracked IDs after a simulated restart.
- Malformed state is handled safely while explicit triggers still work.
- Registry de-duplication and the 512-ID bound work as specified.
- `message_sending` adds `Filomena: ` only in the protected group and never duplicates it.
- Existing DM, self-chat, other-group, and other-channel behavior remains unchanged.

## Operational Changes

Update only the external plugin source, tests, metadata, the plugin configuration, and the target group's system prompt. Do not edit the compiled WhatsApp extension. Validate tests, OpenClaw configuration, and plugin discovery without starting or restarting the gateway; Helena will restart it manually when ready.
