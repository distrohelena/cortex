# WhatsApp Filomena Context Guard

## Goal

Make the configured WhatsApp group invoke the agent only when the current message begins with `@Filomena`, while giving each accepted turn a small amount of recent native WhatsApp context and allowing Graphiti to supply older context when needed. Keep all custom behavior outside the compiled WhatsApp distribution bundle.

## Scope

- Target only WhatsApp group `5511981042361-1589823314@g.us`.
- Preserve self-chat behavior and the existing LM Studio and Graphiti configuration.
- Do not implement an exact character or token cap. Use the native message-count limit as a small bounded approximation.
- Do not start the OpenClaw gateway; Helena starts it manually.

## Restore the Official WhatsApp Bundle

Replace the one modified compiled file, `dist/monitor-CySzv38g.js`, with the byte-identical file from the official `@openclaw/whatsapp@2026.8.1` package already downloaded for comparison. Verify its SHA-256 hash against the official copy after restoration.

The restored behavior is safe with the current DM configuration: `dmPolicy` is `allowlist`, so unknown direct-message senders are blocked rather than sent a pairing challenge. The self number remains allowlisted.

## Configuration

Keep the existing group activation settings:

- `requireMention: true` for the target group.
- Agent mention pattern `^@Filomena\\b`.
- Global implicit-mention options remain disabled as defense in depth, although the external guard is authoritative for this group.

Set `channels.whatsapp.historyLimit` to `8`. OpenClaw will retain up to eight ambient group messages and include them on the next accepted turn. This is a message-count bound, not a 500-character guarantee.

Add a group-specific system prompt with these rules:

1. Treat the supplied recent WhatsApp excerpt as immediate conversational context.
2. Answer the request after the leading `@Filomena` trigger.
3. If the request requires older or broader group history, use the available Graphiti memory search instead of guessing.
4. Do not claim to have found historical context when Graphiti returns no relevant result.

## External Guard Plugin

Create a small source-level plugin at `C:\Users\Helena\.openclaw\workspace\whatsapp-filomena-guard` and load it through `plugins.load.paths`.

Register a typed `before_dispatch` hook. The hook applies only when all of these are true:

- Channel is WhatsApp.
- The event is a group message.
- The conversation ID is the target group JID.

For that group, normalize leading whitespace and inspect only the current inbound message body. If it does not match `^@Filomena(?:\\s|$)` case-insensitively, return `{ handled: true }` without reply text. This suppresses model execution and outbound delivery, including implicit activation caused by replying to a previous bot message. If it matches, return no result and let OpenClaw dispatch normally.

If the body is missing or malformed, suppress the turn for the target group. Direct messages, self-chat, and other conversations are left untouched.

## Data Flow

1. Ordinary group messages fail the native explicit-mention check and are retained in OpenClaw's bounded group history.
2. A message beginning with `@Filomena` passes native activation and the external guard.
3. OpenClaw injects up to eight retained messages plus the current request.
4. The group system prompt tells the model to consult Graphiti only when recent context is insufficient.
5. A reply or other implicitly activated message without the trigger reaches the guard and is silently handled before model dispatch.

## Verification

- The installed compiled monitor file has the same SHA-256 hash as the official `2026.8.1` copy.
- OpenClaw configuration validation succeeds.
- The external plugin loads without warnings.
- `@Filomena oi` in the target group invokes the model and produces a reply to that message.
- Leading whitespace before `@Filomena` is accepted.
- `oi @Filomena`, a plain reply to the bot, and a reply beginning with text other than `@Filomena` produce no model run and no outbound message.
- A triggered request receives at most eight recent ambient messages in native context.
- A request for older context can use Graphiti.
- Self-chat behavior remains unchanged.
- An unknown DM remains silent under the allowlist policy.

## Rollback

Remove the guard plugin path from `plugins.load.paths`, remove its plugin entry, and remove the new `historyLimit` and group system prompt settings. The WhatsApp bundle remains the official unmodified package throughout.
