# Phase C: standalone replies

`Probe:Mode=phase-c` is now the application default. `phase-b` remains a dry run.
The existing loopback inbound bridge remains required. No gateway configuration,
Graphiti code/capture ownership, UUID scheme or namespace changed in this phase.

## Lifecycle

Bridge inbound → conversation burst → Gemma arbiter → Respond → full text
generation (same configured Gemma) → one MCP `messages_send` call.
Each conversation awaits its own generation/send; other conversations can run.
Only a confirmed provider message ID updates last-Filomena-turn/history.
The full generation prompt receives recent conversation, not Graphiti recall yet.
No MCP tool discovery/chat-history output is used to guess sender identity.

## Safety / configuration

- `PhaseC:AllowedConversationNamespaces`: Drift, Plex and Helena DM initially.
  All other conversations remain observation-only for outbound purposes.
- `PhaseC:SystemPrompt`: small configurable bootstrap prompt, no workspace files
  read or rewritten. Rich personality and Graphiti recall remain future work.
- `PhaseC:ContextCharacterLimit=6000`: total user prompt limit, prioritizing new
  inbound over earlier context. Per-turn message/window defaults: 30 / 15 minutes
  (`PhaseB:Turns:Generation*`, retained section for config compatibility).
- `PhaseC:MaxResponseCharacters=3500`: including `Filomena: `; oversize rejects.
- `LmStudio:GenerationMaxTokens=512`, `GenerationTemperature=0.7`; same Model and
  Endpoint as arbitration. No model-load calls or second model.
- Ignore/Wait, unknown namespace, blank content and silent tokens cannot send.
- One lazy long-lived stdio MCP connection; failure retires it for future turns.
  No automatic replay after a failed/uncertain send. Gateway/provider might have
  sent before a timeout, so failure is not proof of nondelivery.
- Bridge deduplication and turn state remain process-local. No durable outbox or
  exactly-once guarantee across restarts. Provider splitting may create chunks.
- No native reply/quote (phase D). Anchor informs generation only. Outbound
  Graphiti capture remains owned by the plugin; no fabricated runId/addressee.

## Installed MCP surface verified from source

`channel-server-DvhWr6Rt.js` registers `messages_send({session_key,text})`, looks
up the session route, calls Gateway `send` and returns `{result: ...}`.
`send-CcjRWFvK.js` returns a delivery payload with `messageId`, `channel`, `runId`.
The MCP server creates a fresh random idempotency key for each invocation.
No caller-supplied idempotency key or reply target is exposed by this tool.

## Canary (Helena controls processes)

1. Stop the old Filomena.App terminal with Ctrl+C (not the Gateway).
2. Enable LM Studio's server with the existing `google/gemma-4-e4b` loaded.
3. Keep the already configured inbound bridge and transport-only OpenClaw guard.
4. From `C:\dev\filomena`:

```powershell
dotnet run --project src\Filomena.App\Filomena.App.csproj -c Release
```

Startup must say `Filomena Phase C started`. Send a NEW message in Helena's DM
first. Expect `Observed inbound bridge event`, then a turn decision. Respond
with successful generation/send logs a nonempty `outboundMessageId`; Ignore
still legitimately means no message. An unavailable LM Studio cannot respond.
The agent does not call memory tools and cannot answer historical Graphiti queries
in phase C. Do not interpret this as a memory regression.

Dry run rollback without changing OpenClaw:

```powershell
dotnet run --project src\Filomena.App\Filomena.App.csproj -c Release -- --Probe:Mode=phase-b
```

No live send is executed by the offline tests. A real receipt/canary remains to
be observed with Helena after starting the new App.

## Offline verification (2026-09-04)

- `dotnet test Filomena.slnx -c Release --no-restore`: 85 passed (Core 23,
  Llm 20, OpenClaw 42), including existing identity golden tests.
- `dotnet build Filomena.slnx -c Release --no-restore`: zero warnings/errors.
- `dotnet format Filomena.slnx --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Pipeline integration uses the actual receiver, turn manager, Gemma arbiter,
  LM Studio client and MCP transport, with fake HTTP/MCP peers; no live send.
- A context-budget regression test failed before the fix, then passed: prior
  outbound cannot displace the current inbound from the generation prompt.
- Debug build was blocked by the already running Filomena.App; Release was
  verified without stopping it. Gateway, Graphiti and DREAMS.md were untouched.
