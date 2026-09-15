# Effective Configuration + Observation Reader V1 Implementation Plan

> Execute inline with executing-plans and TDD. No delegated agents, live probes,
> capture arming in the active installation, models, Graphiti or outbound.

**Goal:** Bind the existing native SQLite reader to a revision-bound read-only
transport preflight, using the native config owner rather than a file probe.

**Architecture:** Extend the config runtime snapshot owner with a coherent detached
projection and process/revision identity. The native WhatsApp coordinator supplies
inspection of effective plus retained configuration through a lifecycle-bound
reader registration. A host-created, authenticated loopback HTTP surface reads the
journal and inspection; it cannot arm capture. The .NET adapter validates every
field and binds opaque source identities to host session/actors. No listener loop.

**Tech stack:** TypeScript, existing SQLite/Kysely, Node HTTP, .NET HttpClient/xUnit.

## 1. Owner snapshot (TDD)

- [x] Add `src/config/effective-runtime-snapshot.test.ts`; verify stable revision,
  detached projection, revision on publication/mutation/reset, absent owner and no
  refresh/listener calls. Run with `node scripts/run-vitest.mjs` and observe RED.
- [x] Extend `src/config/runtime-snapshot.ts` with synchronous
  `readEffectiveRuntimeConfigSnapshot(project)` returning `{version,revision,capturedAt,value}`.
  Project only on a detached clone; no loader; verify owner unchanged before return.
  Expose through existing `runtime-config-snapshot` SDK entrypoint; rerun GREEN.

## 2. Native inspection + reader (TDD)

- [x] Add tests for effective/pinned exclusion, unresolved config, opaque plugins,
  revision changes and owner closure. Extend WhatsApp inspection and the actual
  monitor/coordinator wiring; never execute callbacks except the explicit pure snapshot reader.
- [x] Add a small lifecycle-bound inspection registry and host HTTP reader factory
  in `src/channels/message/native-observation-reader.ts`. Explicit GET-only health,
  open and poll operations; exact opaque session; bearer auth; required revision
  on open/poll; synchronous recheck and read; never arm capture or listen at import.
- [x] Test against real temporary native journal: snapshot/C0, append/E1, repeated
  poll, restart identity, stale revision, wrong session, health and no mutation.

## 3. .NET adapter + preflight (TDD)

- [x] Add `NativeObservationReader` in Filomena.OpenClaw: strict V1 and DTO parsing,
  host binding, health mapping, contiguous sequence and exact actor/self/session
  checks. Native payload retained privately; only supported text projected to runtime.
- [x] Add fake HTTP contract tests plus durable inbox/handoff tests for no snapshot
  execution, duplicate/restart, self suppression, wrong session and stale revision.
- [x] Update ShadowPreflightCommand with explicit transport-only read-only mode.
  Static never connects; transport never constructs Graphiti. Persist/report revision
  as audit data, not project authority. Future open requires fresh server revision check.

## 4. Verification / delivery

- [x] Run focused upstream/.NET regressions, typechecks, formatting and diff checks.
- [x] Document exact host enablement path, no installed changes, local vs live proof,
  remaining operational prerequisites, and three separate readiness outcomes.
- [x] No commit unless verified/coherent; no push or live action under any outcome.
