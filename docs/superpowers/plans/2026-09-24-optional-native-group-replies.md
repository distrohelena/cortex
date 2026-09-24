# Optional Native Group Replies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Filomena optionally send a genuine WhatsApp group reply to a trusted contextual message, without forced replies or unsafe retries.

**Architecture:** Generation selects an opaque execution-local reference. The runtime resolves and journals it alongside the authorized text; a versioned native sender resolves the original quote metadata under exact account/group binding. Existing plain sends stay on their current contract.

**Tech Stack:** .NET 10, xUnit, TypeScript, Vitest, TypeBox/Zod, Baileys.

**Spec:** `docs/superpowers/specs/2026-09-24-optional-native-group-replies-design.md`

## Global Constraints

- Filomena stays running on the existing build during development and tests.
- No artificial live sends or edits to previously delivered messages.
- Preserve existing personality, participation, Boundary enforcement, name/received-reply fixes and no-retry send guarantees.
- Plain messages remain valid. Do not force the latest message as the target.
- Never resolve a target by name, message text, another group's cache or model-created native ID.
- No Graphiti data writes, new dependency, new checker or additional model call.
- Native contract: `whatsapp-bound-group-reply:v1`; payload field: `replyToMessageId`.
- Pause at idle only for migration; preserve history, inbox/outbox and cursor.
- No subagents or independent review without the user's separate opt-in; no push.

## Review Focus

1. Isolated ingress never reaches the legacy quote cache today: register native metadata before the barrier without invoking normalization/access-control or dispatch.
2. A long generation can outlive quote retention: target lookup failure blocks submission, never silently quotes another message.
3. Regeneration must clear a prior selection when its own reply field is null.
4. Identical provider IDs across groups/accounts must not alias.
5. Restart after provider acceptance must preserve reply metadata and never resend.

## Task 1: Native exact quote capability

**Files, OpenClaw worktree:**
- `extensions/whatsapp/src/inbound/bound-text-send.ts` and `.test.ts`
- `extensions/whatsapp/src/inbound/socket-session.ts`
- `extensions/whatsapp/src/inbound/message-delivery.ts` and its tests
- Existing `extensions/whatsapp/src/quoted-message.ts` exact-key cache; no new store.

**Interfaces:** sender owner gains `resolveQuotedMessage?: (jid: string, id: string) => WAMessage | undefined`. Reply request adds required `replyToMessageId` under the new contract only. Receipt echoes the validated target ID. The callback is host-owned, never supplied over RPC.

- [ ] Write RED cases using existing `setup()` and a fake socket: valid quote, null/plain legacy behavior, wrong group/account, missing/expired target, changed socket, repeated attempt. Assert actual `sendMessage` options, not just callback presence.

```ts
expect(sendMessage).toHaveBeenCalledWith(group, { text, linkPreview: null },
  expect.objectContaining({ quoted: original, messageId: expect.any(String) }));
await expect(send(missingTargetRequest, authorize)).rejects.toThrow();
expect(sendMessage).not.toHaveBeenCalled();
```

- [ ] Run the existing bound sender test file with the repository Vitest command; confirm RED before implementation.
- [ ] Extend the strict schema with the explicit group-reply contract. Copy and validate target fields before awaiting readiness; resolve quote only with exact recipient/account, require ID equality, fromMe boolean and known participant for received group messages. Recheck socket and authority immediately before a single native call. Plain requests must have no extra quote field.
- [ ] In socket composition, use `lookupInboundMessageMeta(accountId, jid, id)`, never the alias/search lookup. Build a quote from the original retained body/participant/fromMe. Missing body/metadata means unavailable, not synthesized content.
- [ ] Populate this existing metadata cache from the native message before returning at the isolation barrier. Use provider key and actual extracted text, not normalized legacy callbacks. Add the paired test: isolated inbound populates quote metadata while access-control/agent callbacks remain zero.
- [ ] Run sender, message-delivery/isolation and quoted-message suites; preserve failure diagnostics.

## Task 2: Preserve the new contract through bridge layers

**Files, OpenClaw worktree:** `src/mcp/channel-tools.ts`, `src/mcp/channel-bridge.ts`, `src/gateway/server-methods/send-bound-text.ts`, `packages/gateway-protocol/src/schema/agent.ts`, `src/mcp/channel-bound-send.test.ts`, `src/mcp/channel-server.test.ts`, relevant gateway send tests.

**Files, Filomena worktree:** `src/Filomena.OpenClaw/Mcp/OpenClawBoundMcpTransport.cs`, `tests/Filomena.OpenClaw.Tests/OpenClawBoundMcpTransportTests.cs`, `src/Filomena.Core/Outbound/ShadowOutboundHost.cs`.

**Interfaces:** `BoundSendRequest` gains nullable `ReplyToMessageId` with default null. New native contract requires a nonempty target; old contracts reject it. The .NET receipt validation compares echoed target to requested target. No fallback to legacy send.

- [ ] Add RED contract tests with target `original-message-7`; verify every hop preserves it and the route. Wrong/missing receipt target must fail as unverified, with no retry.

```csharp
var request = new BoundSendRequest(attempt, route, "whatsapp", account, group, text, 4000)
    { ReplyToMessageId = "original-message-7" };
await transport.SendBoundTextAsync(request, token);
// Assert the captured tool arguments use the new contract and this exact target.
```

- [ ] Implement strict forwarding through TypeBox/Zod/bridge/gateway. Continue exact route checks and reject unrecognized fields/versions.
- [ ] Select the new contract in .NET only for a non-null reply target and an approved group. Validate receipt; plain sends keep the existing contract exactly.
- [ ] Run native MCP/gateway tests and the complete Filomena.OpenClaw.Tests project.

## Task 3: Candidate-bound selection and durable history

**Files, Filomena worktree:** `src/Filomena.Core/Flows/FlowDomain.cs`, `src/Filomena.Core/Flows/TypedFlowRunner.cs`, `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Execution.cs`, `src/Filomena.Core/Outbound/ShadowOutboundHost.cs`, `src/Filomena.Core/Outbound/OutboundJournal.cs`, `src/Filomena.Core/ShadowBootstrap.cs`, associated Core tests.

**Interfaces:** execution-local message reference map keyed by actual provider ID. `OutboundBindings` gains an optional node-to-reply-output-port map; default absent leaves old projects unchanged. Generator reply output is nullable string, named `replyTo`. `ConfirmedSend` and recoverable journal state gain optional `ReplyToMessageId`.

- [ ] Write RED tests for choosing an earlier message, null/plain, fabricated ref, a ref belonging to a person/quote rather than an actual contextual message, foreign session, regeneration clearing its selection, and restart after sent-before-history-commit.
- [ ] When projecting actual runtime batch/history envelopes, expose opaque message refs without exposing native IDs. Keep the allowlist in the execution owner, derived from envelopes belonging to that exact session. A quoted body alone is not a selectable original message.
- [ ] Resolve `replyTo` after validating the candidate's structured output. Reject any ref outside that execution's message map before journal reservation. Selection is attached to the candidate, not mutable global state.
- [ ] Add reply ID to durable reservation, confirmed receipt and speech history. Recovery may append proven speech once with the same target but may not resend it. Shadow records the selected target diagnostically and calls no sender.
- [ ] Run the full .NET solution tests, preserving existing invalid-provenance, duplicate and crash-recovery cases.

## Task 4: Operational project, local smoke and controlled migration

**Files:** create `docs/manual-group-live-project-v3-optional-reply-v1.json` from the currently pinned punctuation project; extend `tests/Filomena.Llm.Tests/LiveProjectArtifactTests.cs`. Prepare private operational backups and checkpoint migration only after tests pass.

- [ ] Add tests that compile the new artifact and show generation/regeneration accept nullable `replyTo`, while the rest of the graph/model/FSM stays unchanged.
- [ ] Add a second output port to those six prompt nodes and the narrowly scoped instruction:

```text
Use replyTo = null normalmente. Quando citar uma mensagem esclarecer a quem ou
a que você está respondendo, escolha somente a referência de uma mensagem real
fornecida no contexto. Não escolha automaticamente a última mensagem. Retorne
texto e replyTo; não invente referências.
```

- [ ] Wire the approved reply ports in host-owned outbound bindings. Run compiled local smoke with fake transport: earlier target quoted, null target plain, invalid target blocked, zero live sends.
- [ ] Run build/typecheck/lint applicable to changed files, .NET solution tests and focused OpenClaw suites. Review the five risks above and the final diff; do not bundle unrelated dirty work.
- [ ] Keep the active runtime untouched until every local gate passes. At migration, request graceful idle stop, record pending send state, back up current code/config, and deploy only required artifacts under the already-approved rollback procedure.
- [ ] Preserve checkpoint state and processed IDs under the new reviewed project pin. Revalidate native capability, isolation and revision before resuming. Do not automatically roll journals/cursors back after new sends.
- [ ] Confirm healthy resumed operation; live behavior is assessed only from authorized manual/natural group messages. Do not manufacture a test message.

## Execution and review record

The user approved the design and requires keeping the old version online until
migration. Implementation is intended inline in this session, with local tests;
independent review requires separate authorization. No functional changes or
active interruption have been made while preparing this plan.
