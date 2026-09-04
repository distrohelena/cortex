# Filomena .NET MCP Milestone A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an observation-only .NET 10 executable that connects once to the installed OpenClaw MCP stdio server, inventories its real tools and conversations, and prints one sanitized normalized inbound event without sending, invoking Gemma, or touching Graphiti.

**Architecture:** A small provider-neutral core owns conversation/actor/message contracts and the byte-compatible identity codec. `Filomena.OpenClaw` is the only assembly that references the official MCP SDK and maps OpenClaw MCP JSON into those contracts. A Generic Host console worker runs an explicit `inspect`, `list`, or `wait` probe and never calls `messages_send`.

**Tech Stack:** .NET SDK 10.0.400, C# / `net10.0`, Generic Host, official `ModelContextProtocol` 2.2.0, System.Text.Json, xUnit.

---

## File map

- `global.json`: pins the installed stable .NET 10 SDK.
- `Filomena.slnx`: minimal solution manifest.
- `src/Filomena.Core`: provider-neutral models plus legacy-compatible WhatsApp identities and UUIDv5.
- `src/Filomena.OpenClaw`: MCP tool client, OpenClaw DTO mapping, sanitization, cursor/gap and dedup state.
- `src/Filomena.App`: Generic Host observation probe and configuration.
- `tests/Filomena.Core.Tests`: literal golden identity compatibility tests.
- `tests/Filomena.OpenClaw.Tests`: MCP mapping, deduplication, gap, cancellation, and secret-sanitization tests.
- `docs/openclaw-mcp-milestone-a.md`: installed MCP inventory and operational probe notes.

### Task 1: Freeze persistent identity compatibility

**Files:**
- Create: `global.json`
- Create: `Filomena.slnx`
- Create: `src/Filomena.Core/Filomena.Core.csproj`
- Create: `tests/Filomena.Core.Tests/Filomena.Core.Tests.csproj`
- Create: `tests/Filomena.Core.Tests/IdentityCompatibilityTests.cs`
- Create: `src/Filomena.Core/Identity/FilomenaIdentity.cs`
- Create: `src/Filomena.Core/Conversations/ConversationModels.cs`

- [ ] Write literal tests for the two WhatsApp namespaces, participant/Agent/episode fixtures, and Helena's three conversation-scoped participant UUIDs.
- [ ] Run the focused test and confirm RED because the identity API does not exist.
- [ ] Implement the legacy namespace codec and RFC 4122 UUIDv5 using the existing namespace constants and NUL-delimited keys.
- [ ] Run the focused test and confirm GREEN.

### Task 2: Specify and map the installed MCP contract

**Files:**
- Create: `src/Filomena.OpenClaw/Filomena.OpenClaw.csproj`
- Create: `src/Filomena.OpenClaw/Mcp/OpenClawMcpModels.cs`
- Create: `src/Filomena.OpenClaw/Mcp/OpenClawConversationMapper.cs`
- Create: `tests/Filomena.OpenClaw.Tests/Filomena.OpenClaw.Tests.csproj`
- Create: `tests/Filomena.OpenClaw.Tests/OpenClawConversationMapperTests.cs`

- [ ] Write fixture-driven tests proving route mapping uses structural `channel`, `to`, `accountId`, and `threadId`, and never parses identity from display text.
- [ ] Write inbound-event tests proving only explicit structural fields are surfaced and missing sender/reply IDs remain missing.
- [ ] Run the focused tests and confirm RED because the mapper is absent.
- [ ] Implement the immutable MCP DTOs, provider-neutral observation models, and strict mapper.
- [ ] Run the focused tests and confirm GREEN.

### Task 3: Add a long-lived official MCP client

**Files:**
- Create: `src/Filomena.OpenClaw/Mcp/IMcpToolClient.cs`
- Create: `src/Filomena.OpenClaw/Mcp/OpenClawMcpClient.cs`
- Create: `src/Filomena.OpenClaw/Mcp/OpenClawMcpTransport.cs`
- Create: `tests/Filomena.OpenClaw.Tests/OpenClawMcpTransportTests.cs`

- [ ] Write tests around an in-memory tool invoker for conversation listing, one-event-per-ID deduplication, cursor advancement, explicit gap reporting, timeout continuation, and cancellation.
- [ ] Run the focused tests and confirm RED because the transport is absent.
- [ ] Implement one disposable stdio MCP session using `StdioClientTransport` and `McpClient.CreateAsync`.
- [ ] Implement `conversations_list`, `messages_read`, `events_poll`, and `events_wait`; do not expose or call `messages_send` in milestone A.
- [ ] Run the focused tests and confirm GREEN.

### Task 4: Build the observation-only Generic Host probe

**Files:**
- Create: `src/Filomena.App/Filomena.App.csproj`
- Create: `src/Filomena.App/Program.cs`
- Create: `src/Filomena.App/OpenClawProbeWorker.cs`
- Create: `src/Filomena.App/appsettings.json`
- Create: `tests/Filomena.OpenClaw.Tests/SafeOutputTests.cs`

- [ ] Write tests that reject credential-shaped fields from normalized output and retain only bounded structural metadata.
- [ ] Run the focused tests and confirm RED.
- [ ] Implement typed options, DI, logging, graceful cancellation, and `inspect`, `list`, and `wait` modes.
- [ ] Ensure every mode is read-only and print normalized JSON with absent fields represented as unavailable rather than inferred.
- [ ] Run tests and build the solution.

### Task 5: Execute the real milestone-A canary

**Files:**
- Create: `docs/openclaw-mcp-milestone-a.md`

- [ ] Run `inspect` against the already-running user-managed Gateway and save the exact installed tool names/schemas without secrets.
- [ ] Run `list` and identify Alunos de Drift, `(tri)PLEX`, and Helena DM by structural route/JID.
- [ ] Run `wait`, receive one real inbound event, and record which structural identity/message/reply fields are actually present.
- [ ] If no event arrives, stop with the exact command Helena must run; do not start or stop the Gateway.
- [ ] Document whether MCP alone is sufficient or whether a metadata-only bridge is required.

### Task 6: Verify and commit locally

**Files:**
- Modify only files listed above.

- [ ] Run `dotnet test Filomena.slnx`.
- [ ] Run `dotnet build Filomena.slnx --no-restore`.
- [ ] Run `dotnet format Filomena.slnx --verify-no-changes --no-restore`.
- [ ] Run `git diff --check` and inspect `git status --short` to prove `workspace/DREAMS.md` was untouched.
- [ ] Confirm no source references Graphiti or an LM Studio/Gemma endpoint and no code invokes `messages_send`.
- [ ] Commit the milestone-A files locally; do not push.
