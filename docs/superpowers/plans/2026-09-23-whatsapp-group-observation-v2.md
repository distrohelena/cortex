# WhatsApp Group Observation V2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry exact-group native observations to the trusted .NET shadow inbound boundary without relaxing the DM V1 contract.

**Architecture:** The existing SQLite journal remains one atomic admission store. A group-scoped host advertises V2 cursors and an exact set of opaque trusted participant refs; the .NET adapter validates that set and maps each actor to a host-owned canonical participant. No project or model supplies account, group, participant, or authority.

**Tech Stack:** TypeScript, Vitest, SQLite, C#, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-whatsapp-group-shadow-boundary-design.md`

## Global Constraints

- Preserve `inbound-observation:v1` for DMs and reject cross-version cursors.
- Group is SHADOW-only; no deploy, live account, model, Graphiti write, or outbound in this plan.
- Reader cannot mutate native queue or journal admission state.
- Brain v3 and existing DM runtime remain unchanged.

## Review Focus

- A V1 cursor presented to a V2 group must fail, even if epoch and session match.
- A status actor absent from trusted participant config must fail preflight.
- A returned actor outside the approved participant set must fail closed.
- PN and LID aliases for the same person must map to one canonical actor.
- A changed participant set during revalidation must invalidate the approved receiver scope.

---

### Task 1: Native journal version fence

**Files:** `src/channels/message/native-observation.ts`; `src/channels/message/native-observation.test.ts` in OpenClaw.

**Interfaces:** `createNativeObservationHost({sessionId})` emits `inbound-observation:v2` for exact `@g.us` scope, V1 otherwise; `ObservationCursor.version` admits only the active host version.

- [ ] Add a failing group journal test: arm a group host, admit one participant record, assert V2 cursor, V1 cursor rejection, and V1 DM regression.
- [ ] Run the focused Vitest file and confirm the expected V2 assertion fails.
- [ ] Add the version derivation at host construction and use it in `read` and `describe`, with no schema or admission changes.
- [ ] Run the focused Vitest file; verify all tests pass.

### Task 2: Native HTTP and host binding

**Files:** `src/channels/message/native-observation-reader.ts`, `native-observation-bootstrap.ts` and their adjacent tests in OpenClaw.

**Interfaces:** Group bootstrap accepts one exact `@g.us` session plus nonempty trusted participant JIDs; status V2 returns `kind=group` and sorted `participantActorRefs` derived from the journal scope. DM retains `peerActorRef` and V1 routes. Open/Poll require the matching cursor version.

- [ ] Add failing HTTP tests for group V2 status, opaque participant refs, V1 route/cursor rejection, no-epoch state, and read-only Open/Poll.
- [ ] Run focused Vitest and confirm expected failures.
- [ ] Add strict group configuration validation and versioned route/status handling; do not loosen Dispatch Inspection or arm conditions.
- [ ] Run focused native suites; verify DM V1 behavior remains unchanged.

### Task 3: Trusted .NET group adapter

**Files:** `src/Filomena.OpenClaw/Observation/NativeObservationReader.cs`, `NativeObservationReader.Revalidation.cs`, `tests/Filomena.OpenClaw.Tests/NativeObservationReaderTests.cs` in Filomena.

**Interfaces:** Optional host-owned mapping from native participant JID to canonical actor is converted to expected opaque actor refs. Group preflight requires exact advertised ref set; group events map to `SessionEntryKind.Group` and the existing `ShadowSessionBinding`. DM constructor semantics remain intact.

- [ ] Add failing xUnit tests for group status/route/cursor, multiple actors, PN/LID alias mapping, foreign actor, changed participant set, and Group canonical inbound.
- [ ] Run focused xUnit tests and confirm expected failures.
- [ ] Implement V2-specific validation/mapping and receiver-scope fingerprint; keep DM V1 branches unchanged.
- [ ] Run the .NET observation and core suites; verify no transport send is reachable.

### Task 4: Cross-boundary verification

**Files:** Adjacent native and .NET tests only, as needed for the compiled contract smoke.

**Interfaces:** Native V2 status/cursor/event shape matches the .NET reader exactly; unknown versions fail closed.

- [ ] Add a compiled-shape fixture from the real native V2 server to the .NET contract tests, including actor and self refs.
- [ ] Run native journal/reader/bootstrap/dispatch tests and .NET reader/runtime tests.
- [ ] Run typecheck, lint, format check, and build on touched projects; inspect the diff for DM changes and any outbound path.
