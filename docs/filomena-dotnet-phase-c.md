# Phase C: standalone replies

`Probe:Mode=phase-c` is now the application default. `phase-b` remains a dry run.
The existing loopback inbound bridge remains required. No gateway configuration,
Graphiti code/capture ownership, UUID scheme or namespace changed in this phase.

## Lifecycle

Authorized DM: bridge inbound → conversation burst → completeness arbiter →
Respond / adaptive Wait / explicit conversation closure. Respond then invokes
full text generation → one MCP `messages_send` call. The arbiter is mandatory;
it assesses completeness, not the social value of participating in a group.
A complete greeting such as "Hey Filooo" should select Respond/thought_complete.
Wait never becomes WaitLimitReached/Ignore in an authorized DM.
Groups/other chats still use the participation arbiter; outbound is separately
restricted by AllowedConversationNamespaces (currently Helena DM only).
Generation appends conversation-specific instructions to the shared core prompt:
`PhaseC:DirectMessageInstructions` versus `PhaseC:GroupInstructions`.
Each conversation awaits its own generation/send; other conversations can run.
Only a confirmed provider message ID updates last-Filomena-turn/history.
The full generation prompt receives recent conversation, not Graphiti recall yet.
No MCP tool discovery/chat-history output is used to guess sender identity.

## Safety / configuration

- `PhaseC:AllowedConversationNamespaces`: only Helena DM is currently enabled.
  Drift, Plex and all other conversations cannot send automatically. Passive
  observation remains unchanged. Restart Filomena.App to apply config changes.
- `PhaseC:SystemPrompt`: small configurable bootstrap prompt, no workspace files
  read or rewritten. Rich personality and Graphiti recall remain future work.
- `PhaseC:ContextCharacterLimit=6000`: total user prompt limit, prioritizing new
  inbound over earlier context. Per-turn message/window defaults: 30 / 15 minutes
  (`PhaseB:Turns:Generation*`, retained section for config compatibility).
- `PhaseC:MaxResponseCharacters=3500`: final text limit; oversize rejects.
  No automatic `Filomena: ` prefix; a leading model-generated label is stripped.
- `LmStudio:ArbitrationMaxTokens=8192`, `GenerationMaxTokens=8192`,
  `RequestTimeoutSeconds=180`, `GenerationTemperature=0.7`; same Model and
  Endpoint as arbitration. No model-load calls or second model.
  Output budgets include reasoning; they are separate from the model context
  window and do not increase the recent-chat prompt or sent-message limits.
- Ignore/Wait, unknown namespace, blank content and silent tokens cannot send.
- One shared long-lived stdio MCP connection, warmed during Phase-C host startup
  before inbound processing (20-second startup budget; failure leaves observation
  active). This starts only `mcp serve`, never the Gateway.
- Every send performs a read-only `tools/list` round trip (3-second health-check
  budget). A failed preflight retires the connection and permits one reconnect
  before any `messages_send` call. MCP liveness is not provider delivery readiness.
  Failure during the actual send is never replayed; future turns can reconnect.
- Console logs include local timestamps with milliseconds/timezone and monotonic
  `elapsedMs` for LM Studio arbitration/generation, MCP connect/preflight, and
  send confirmation. LM calls have per-call IDs. Timing logs omit prompts,
  reasoning and response text. Send confirmation is not a recipient read receipt.
  No automatic replay after a failed/uncertain send. Gateway/provider might have
  sent before a timeout, so failure is not proof of nondelivery.
- Bridge deduplication and turn state remain process-local. No durable outbox or
  exactly-once guarantee across restarts. Provider splitting may create chunks.
- No native reply/quote (phase D). Anchor informs generation only. Outbound
  Graphiti capture remains owned by the plugin; no fabricated runId/addressee.

## Runtime tuning

Edit `src/Filomena.App/appsettings.json`, then rebuild/restart Filomena.App.
No Gateway restart is required. Values are read at App startup.

| Setting | Current | Meaning |
| --- | --- | --- |
| `LmStudio:ArbitrationMaxTokens` | 8192 | Reasoning + arbiter JSON output budget |
| `LmStudio:GenerationMaxTokens` | 8192 | Reasoning + final text output budget |
| `LmStudio:RequestTimeoutSeconds` | 180 | Per-model-request timeout |
| `PhaseB:Turns:AllowlistedDirectMessageQuietWindow` | `00:00:03` | Silence required after most recent DM message |
| `PhaseB:Turns:AllowlistedDirectMessageMaxBurst` | `00:01:00` | Maximum collection time from burst start |
| `PhaseB:Turns:MaxBurstMessages` | 50 | Maximum messages collected per burst (all conversations) |
| `PhaseB:Turns:DmDefaultRecheckSeconds` | 15 | Fallback additional wait |
| `PhaseB:Turns:DmMinRecheckSeconds` | 5 | Minimum suggested wait |
| `PhaseB:Turns:DmMaxRecheckSeconds` | 120 | Maximum suggested wait |
| `PhaseB:Turns:DmFollowUpAfterSilenceSeconds` | 45 | Earliest a follow-up may be selected, not an automatic send |
| `PhaseB:Turns:DmMaxTimedReevaluations` | 3 | Automatic checks before parking pending context for inbound |

Ten DM messages two seconds apart stay together and are evaluated three seconds
after the last message. The first of silence, duration cap or message cap ends
collection; the arbiter can still decide Wait/Ignore/Respond. Excess queued
messages stay for the next burst. This is not typing-status detection. Context
message/character caps remain separate from the number buffered.

### DM adaptive wait and follow-up

The model returns `recheckAfterSeconds` on Wait and `responseKind=reply|follow_up`.
It receives process-observed silence, pending duration, remaining timer checks,
provider timestamps, current/pending markers and a structural `follow_up_allowed`
flag. Provider timestamps never determine the scheduling silence clock.
After the configured automatic checks, a remaining Wait logs `awaitingInbound=true`
and retains bounded pending context until a new message or restart. No polling loop
continues while parked. Restart discards ephemeral pending state.

Only explicit Respond/follow_up can generate a contextual clarification; it is
rejected before the silence threshold or outside an authorized DM. The generator
uses `PhaseC:FollowUpInstructions`, asks for missing context and must not infer
distress from silence alone. A timer never forces a "tá tudo bem?".
Any response attempt ends scheduling for that inbound block: no second follow-up
without new inbound, including after uncertain send failure.

New inbound during arbitration invalidates its old decision and restarts burst
collection with the combined bounded context. New inbound already queued after
generation suppresses the stale send. This is not an atomic provider-send/receive
transaction: a message arriving after the final pre-send check may still race
with the external send. No replay or synthetic causal IDs are introduced.

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

### Adaptive DM verification (2026-09-05)

- `dotnet test Filomena.slnx -c Release --no-restore`: 111 passed (Core 34,
  Llm 33, OpenClaw 44), using fake time and fake HTTP/MCP peers.
- `dotnet format Filomena.slnx --verify-no-changes --no-restore`: passed.
- New cases cover restoration of the DM arbiter, wait duration parsing/clamping,
  a single follow-up after sufficient silence, parking without Ignore, pending
  context preserved across an hour of simulated silence, new inbound during
  arbitration/generation, per-chat independence, cancellation, and DM-specific
  generation instructions. Group regressions and UUID golden tests still pass.
- No live LM Studio/WhatsApp behavior is claimed by these offline tests. Reopen
  Filomena.App to load the new code and configuration; Gateway is unchanged.
