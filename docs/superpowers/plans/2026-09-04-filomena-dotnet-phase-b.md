# Filomena .NET Phase B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add isolated conversation burst scheduling and a Gemma-backed typed turn arbiter without sending any channel messages.

**Architecture:** `Filomena.Core` owns provider-neutral scheduling contracts and one serialized queue per conversation. A small `Filomena.Llm` assembly owns the OpenAI-compatible LM Studio client, arbiter prompt, validation, retry, and fallback. `Filomena.App` selects a Phase-B worker that connects to the existing OpenClaw MCP transport, logs decisions, and never calls `messages_send`.

**Tech Stack:** .NET 10, C# 14, Generic Host, `System.Threading.Channels`, `TimeProvider`, `System.Text.Json`, `HttpClient`, xUnit v3, LM Studio OpenAI-compatible API.

---

### Task 1: Define and test the provider-neutral arbiter contract

**Files:**
- Create: `src/Filomena.Core/Arbitration/TurnDecision.cs`
- Create: `src/Filomena.Core/Arbitration/TurnArbitration.cs`
- Create: `tests/Filomena.Core.Tests/TurnArbitrationTests.cs`

- [ ] Write tests for valid decisions, invalid anchor/target references, and conservative DM/group fallback.
- [ ] Run the focused tests and verify they fail because the contracts do not exist.
- [ ] Implement the minimal records, enums, validation, and fallback policy.
- [ ] Run the focused tests and verify they pass.

### Task 2: Implement burst aggregation with per-conversation isolation

**Files:**
- Create: `src/Filomena.Core/Conversations/ConversationTurnOptions.cs`
- Create: `src/Filomena.Core/Conversations/ConversationTurnManager.cs`
- Create: `src/Filomena.Core/Conversations/ConversationTurnState.cs`
- Create: `tests/Filomena.Core.Tests/ConversationTurnManagerTests.cs`

- [ ] Write tests for DM/group debounce selection, max-burst enforcement, burst aggregation, concurrent conversation isolation, messages arriving during arbitration, `Wait` reevaluation, cancellation, and bounded recent context.
- [ ] Run the focused tests and verify they fail because the manager does not exist.
- [ ] Implement one channel/processor per conversation and configurable `TimeProvider`-based scheduling.
- [ ] Run the focused tests and verify they pass without long wall-clock sleeps.

### Task 3: Add the LM Studio arbitration boundary

**Files:**
- Create: `src/Filomena.Llm/Filomena.Llm.csproj`
- Create: `src/Filomena.Llm/LmStudio/LmStudioOptions.cs`
- Create: `src/Filomena.Llm/LmStudio/ILmStudioChatClient.cs`
- Create: `src/Filomena.Llm/LmStudio/LmStudioClient.cs`
- Create: `src/Filomena.Llm/Arbitration/ArbiterPrompt.cs`
- Create: `src/Filomena.Llm/Arbitration/GemmaTurnArbiter.cs`
- Create: `tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj`
- Create: `tests/Filomena.Llm.Tests/LmStudioClientTests.cs`
- Create: `tests/Filomena.Llm.Tests/GemmaTurnArbiterTests.cs`
- Modify: `Filomena.slnx`

- [ ] Write tests proving the configured model is used, one compact JSON response is requested, malformed output retries once, known references are enforced, group failure ignores, and clearly addressed allowlisted DM failure responds.
- [ ] Run the focused tests and verify they fail because the LLM assembly does not exist.
- [ ] Implement the minimal HTTP client, prompt formatter, parser, retry, and fallback.
- [ ] Run the focused tests and verify they pass.

### Task 4: Wire Phase B into the Generic Host without outbound transport

**Files:**
- Create: `src/Filomena.App/PhaseBOptions.cs`
- Create: `src/Filomena.App/FilomenaTurnWorker.cs`
- Modify: `src/Filomena.App/Program.cs`
- Modify: `src/Filomena.App/ProbeOptions.cs`
- Modify: `src/Filomena.App/appsettings.json`
- Modify: `src/Filomena.App/Filomena.App.csproj`
- Modify: `tests/Filomena.OpenClaw.Tests/ProbeOptionsTests.cs`
- Create: `tests/Filomena.OpenClaw.Tests/PhaseBCompositionTests.cs`

- [ ] Write tests proving `phase-b` is accepted, configuration validates, and the composition contains no outbound send call.
- [ ] Run the focused tests and verify they fail for the missing Phase-B mode/composition.
- [ ] Register the selected worker, manager, arbiter, typed options, `HttpClient`, and graceful shutdown behavior.
- [ ] Run the focused tests and verify they pass.

### Task 5: Verify and document the Phase-B checkpoint

**Files:**
- Create: `docs/filomena-dotnet-phase-b.md`

- [ ] Run `dotnet test Filomena.slnx`.
- [ ] Run `dotnet build Filomena.slnx --no-restore`.
- [ ] Run `git diff --check`.
- [ ] Search `src` for `messages_send`, Graphiti endpoints, and a second model configuration; verify Phase B adds none.
- [ ] Document how to run Phase B, expected decision logs, failure behavior, and the explicit no-send boundary.
- [ ] Commit the verified implementation locally without `workspace/DREAMS.md` and without pushing.

