# Filomena .NET / OpenClaw MCP — Milestone A

Date: 2026-09-04

This milestone is observation-only. It does not send channel messages, call LM Studio, or read/write Graphiti.

## Runtime and package

- Installed SDK selected: `.NET SDK 10.0.400` (`net10.0`, pinned by `global.json`).
- Official MCP SDK package: `ModelContextProtocol` `2.2.0`.
- OpenClaw inspected: `2026.8.1 (ea80657)`.
- Stdio command: `openclaw.cmd mcp serve --claude-channel-mode off`.
- The C# process creates one MCP stdio child and keeps it for the lifetime of the probe.

## Installed MCP tools

The following tools and schemas were verified in the installed OpenClaw source at
`C:\Users\Helena\AppData\Roaming\npm\node_modules\openclaw\dist\channel-server-DvhWr6Rt.js`:

| Tool | Input schema |
| --- | --- |
| `conversations_list` | `limit?: 1..500`, `search?: string`, `channel?: string`, `includeDerivedTitles?: boolean`, `includeLastMessage?: boolean` |
| `conversation_get` | `session_key: string` |
| `messages_read` | `session_key: string`, `limit?: 1..200` |
| `attachments_fetch` | `session_key: string`, `message_id: string`, `limit?: 1..200` |
| `events_poll` | `after_cursor?: integer >= 0`, `session_key?: string`, `limit?: 1..200` |
| `events_wait` | `after_cursor?: integer >= 0`, `session_key?: string`, `timeout_ms?: 1..300000` |
| `messages_send` | `session_key: string`, `text: string` |
| `permissions_list_open` | no arguments |
| `permissions_respond` | `kind: exec|plugin`, `id: string`, `decision: allow-once|allow-always|deny` |

The milestone-A C# code invokes only `conversations_list`, `messages_read`, and `events_wait` through its read-only client surface. It does not expose or invoke `messages_send`.

## Conversation payload

`conversations_list` returns `structuredContent.conversations[]` entries with:

```text
sessionKey
channel
to
accountId?
threadId?
label?
displayName?
derivedTitle?
lastMessagePreview?
updatedAt?
```

`to` is the structural provider route available from this MCP surface. For WhatsApp, the mapper treats it as the native conversation JID and reproduces the existing conversation namespace. Display names and titles never participate in identity.

## Event payload

`events_wait` returns `structuredContent.event` and optionally `structuredContent.gap`.
The installed bridge constructs message events with:

```text
cursor
type = message
sessionKey
conversation? = { sessionKey, channel, to, accountId?, threadId?, ... }
messageId?
messageSeq?
role?
text?
raw = session.message payload
```

The projected `raw` payload is built from:

```text
sessionKey
senderIsOwner?
agentId?
message
messageId?
messageSeq?
session snapshot fields
runId?
```

Source inspection proves native conversation route and provider/session message ID are available. It does **not** prove that inbound native sender ID, sender display name, provider timestamp, or reply provenance are guaranteed. The mapper therefore leaves these values absent unless an explicit structural field exists; it never parses transcript text or display names to invent identity.

The diagnostic payload is recursively bounded and redacts credential-shaped properties (`token`, `password`, `secret`, `authorization`, `credential`, cookies, and API keys).

## Cursor and recovery semantics

- `events_wait` is a real long poll with cancellation and a maximum timeout of 300 seconds.
- The bridge queue is bounded to 1,000 events.
- The cursor starts at zero inside each `openclaw mcp serve` process; it is not durable across a child-process restart.
- The C# observer advances only from structural cursors, suppresses duplicate `(sessionKey, messageId)` events, detects queue gaps, and reads recent snapshots with `messages_read` before continuing.
- If an established MCP connection drops, the Generic Host reconnects with a configurable delay while retaining its in-memory message-ID registry; an initial unavailable Gateway still fails fast instead of trying to manage it.
- Because transcript snapshots may lack deterministic sender/reply metadata, they are recovery evidence only in milestone A and are not converted into canonical inbound events.

## Live probe status

After Helena started the user-managed Gateway, the runtime probe connected to OpenClaw `2026.8.1`, received the MCP capability advertisement, and listed all nine installed tools with schemas matching the source inspection above.

The conversation inventory discovered:

| Conversation | Native ID | Namespace |
| --- | --- | --- |
| `(tri)PLEX do Lula requests` | `120363318973251043@g.us` | `whatsapp-group-120363318973251043-g-us` |
| `Alunos de Drift do Fiuk™` | `5511981042361-1589823314@g.us` | `whatsapp-group-5511981042361-1589823314-g-us` |

The Helena DM route `5511944558218@s.whatsapp.net` was not present in `conversations_list`; a new inbound DM is needed for OpenClaw to materialize or refresh that session route.

An inbound event reached `events_wait`, but its conversation descriptor contained `updatedAt: null`. The first probe build incorrectly called `TryGetInt64` on that JSON null and failed before printing the sanitized event. A regression test now covers explicit null values in `updatedAt`, `messageSeq`, and provider timestamps, and the numeric mapper accepts only actual JSON numbers.

Still pending after rebuilding the probe:

- one successfully printed real inbound event;
- inspection of its actual sender ID, timestamp, and reply provenance;
- discovery of the Helena DM route.

Stop the old probe with `Ctrl+C`, then run from `C:\dev\filomena`:

```powershell
dotnet run --project src\Filomena.App\Filomena.App.csproj --no-build -- --Probe:Mode=milestone-a --Probe:WaitTimeoutSeconds=300
```

Then send one ordinary inbound WhatsApp message during the five-minute observation window. The probe will print the installed tool schemas, the three configured target routes it can find, and one sanitized normalized inbound event. It will not answer the message.

## Decision gate

MCP is sufficient for read-only conversation routing, history access, event long-polling, and standalone text sending (for a later phase). MCP 2026.8.1 does not expose `replyTo` in `messages_send`.

Whether MCP alone is sufficient for deterministic social identity remains intentionally undecided until the live event proves whether its `raw` payload contains native sender ID and reply provenance. If either is absent, the next design step is a metadata-only OpenClaw bridge; no transcript/name parsing is permitted.
