# Group pilot: Alunos de Drift do Fiuk

## Scope and decisions

User selected this group as the sole initial pilot. Name is discovery-only: local native
session metadata returned one matching WhatsApp group, including an exact group ID and
external delivery route. The group uses the historical numeric-numeric `@g.us` format.
No messages were read or sent for this lookup. Identifiers stay in private host config.

Continue the existing group Shadow design/plan. First wire the existing V2 reader into
the operational .NET command, then prove native isolation coverage. Do not infer
coverage from merely being able to arm a gate. Group live outbound stays blocked until
the separate group send safety boundary and real Shadow pilot are validated.

Ruling: keep explicit, bounded host-owned participant aliases for the first pilot.
Every canonical participant needs both existing Graphiti isolation controls in the group
namespace. Unknown actors fail closed, not silently mapped to Helena. Cost: roster changes
require a new preflight; this is not unrestricted participation by arbitrary group members.

Ruling: reuse the existing command and reader, adding optional GroupParticipants only to
the native host document. No project authority, new runtime, personality or prompt changes.

## Local progress

- Added NativeObservationHostBinding and wired NativeShadowCommand to group V2 with
  exact native/logical group equality, canonical PN identities and explicit PN/LID aliases.
- Group mode remains rejected when a live sender is requested.
- Native host tests: 17 passing, including the real command reaching authenticated V2
  status on a local test server and rejecting invalid native proof before model creation.
- Full .NET suites: Core 595 pass/10 skipped, LLM 474 pass/39 skipped,
  OpenClaw 117 pass. Solution build: zero warnings/errors.
- Located and reproduced historical-group-ID bugs in native journal version selection,
  HTTP reader, bootstrap config, isolation arm/recovery and effective dispatch producer.
  Tests failed first for the historical ID while modern IDs continued passing. Narrow
  regex fixes retain numeric-only DM PN/LID semantics. The real receiver test also
  reproduced a missed historical group check in participant projection; it now passes.
- Added independently enumerated `native-group-isolation:v1` coverage. It does not
  inherit future DM paths. Reducer requires the correct conversation-kind version,
  live proof, exact scope, approved/raw/current revisions and native composition receipts.
- Placement evidence: actual WhatsApp receiver normal/replay tests, recovery helper
  durable-quarantine/restart tests, imperative host-origin admission tests. Counterproofs
  without isolation execute the same callbacks; protected cases execute none.
- Restart-recovery suite: 200 pass. Receiver/identity: 29 pass. Real owner composition:
  15 pass. Reducer/recovery/origin matrix: 46 pass, including group stale snapshot/raw,
  wrong account/group, disarmed, invalid snapshot and future uncovered path.
- Core and extensions typechecks pass. Full native build passes, including SDK exports
  and built plugin control-plane loading. Focused lint passes after local style fixes.
- Final reducer/composed-owner rerun: 34 pass. Compiled full-host smoke: 3/3 startup and
  authenticated status pass, capture OFF, status 4.17/4.41/4.64 ms. This smoke uses a
  synthetic DM and disabled channels/plugins: it proves host health, NOT live group behavior.
- Compiled historical-group V2 host smoke also passes 3/3 with two synthetic participants,
  capture OFF and status 4.70/4.15/4.07 ms. No transport account or real group was used.

## Operational project wiring gap found and corrected

The frozen DM-live artifact still had bare group response exits. A new integration test
with the unmodified artifact reproduced two state transitions and no Send boundary.
Created `manual-group-shadow-project-v3-gpt6-luna.json`, adding only three group Send
nodes/data routes per state. State-switch exits remain reachable only from `sent`, never
from WOULD_SEND. Inverse-transformation equality proves all brain fields and DM wiring
unchanged. SHA256: `A3B066C5B05B3D28E1B429CE0D900C38C8FF82D892429C3D2E42A4E2B15AAF97`.

The operational command now rejects absent/misbound group Send wiring before contacting
Observation. Existing graph compilation also rejects control-dominance-breaking bypasses.
The real ObservedShadowRun with the operational artifact and fake external services proves:
two canonical participants, two successful executions/WOULD_SEND, duplicate and self events
do not execute, only inbound history, no speech-state transition, direct-rule bypass, and
all recall queries bound to the group namespace. This is not live model/memory evidence.

## Placement scope

Normal ingress and durable replay return through `message-delivery` and its captured
exact-session gate before approval/question handling and legacy callbacks. Restart
recovery uses the persisted-origin pre-mutation quarantine barrier. Imperative runtime
admission uses the host-origin capability and rechecks before actual settlement; old
owners retain denial after release. Runtime exclusions additionally require the native
composition receipt, not merely a path name. Broad `runtime_registry`, future surfaces
and custom unproven recovery implementations remain uncovered/fail-closed.

These are isolated execution proofs, not a live group preflight or a guarantee that
arbitrary plugins are safe. An actual live registry must pass the same receipt checks.

## Still required before live activation

1. Resolve the canonical identity proof blockers recorded in
   `group-memory-preflight-2026-09-24.md`. Host bindings and ten read-only probes are
   prepared; full group preflight failed closed (one participant proven, two existing
   nodes without exported identity, two expected canonical UUIDs not found).
2. Revalidate the bounded participant bindings at the operational window. The local
   observed-participant inventory is not a complete/current WhatsApp membership roster.
3. Controlled group Shadow preflight/canary with no sender, then separately validate
   exact-group outbound safety before any actual group response.

No deploy, capture arm, Graphiti write, model call or outbound was performed in this slice.

## Native V2 cross-language contract closed

Added an isolated source entry and esbuild fixture builder in OpenClaw. It compiles
the real reader, queue, journal, native registry composition and isolation reducer;
it does not modify the production build or SDK exports. Config evidence and accounts
are explicitly synthetic. No plugin startup or transport is invoked.

The captured HTTP artifact in `tests/Filomena.OpenClaw.Tests/Fixtures/native-group-v2.json`
contains OFF status, ARMED status, empty Open/C0, four real queue admissions, Poll,
replay, disarmed status and closed-epoch replay. The real C# adapter accepts it,
preserves native IDs/sequences/timestamps, maps two PN/LID aliases to the same Alice,
keeps Bob distinct and resolves self correctly. A recreated reader consumes the
preserved cursor with the actual disarmed status. Tampered version, actor, session,
epoch, sequence and revision all fail closed.

Fresh adapter suite: **124 passed / 0 failed**; group host/integration suite rerun:
**17 passed / 0 failed**, including the two-participant runtime/WOULD_SEND scenario.
New fixture scripts pass focused
lint and format. No native production source changed in this continuation, so the
previous full production build/smoke was not rerun or claimed as fresh evidence.
The test bundle required both native SQLite schema assets; the generator now copies
them explicitly. Fixture README records regeneration and provenance limitations.

## Pilot metadata validation (read-only, no message content)

Native session metadata resolves the approved group to WhatsApp account `default`
with consistent group route/context/origin. Five participants were previously
observed. Each is a structural PN, and all five have reciprocal persisted PN→LID→PN
mappings with distinct LIDs in that account's native auth store. No display names
or message text were used to infer identity. Raw participant IDs remain out of this
report. This establishes a bounded candidate allowlist, not current full membership
or Graphiti canonical-person existence.

Existing project evidence identifies DRIFT42 as this group's positive canary, with
PRIVATE91 and PLEX73 as foreign controls. These are inputs for a new read-only
group preflight, **not a fresh PASS**. Every selected canonical participant still
needs namespace-specific participant/recall validation before live execution.

Active services, DM operation, credentials, brain, Graphiti and outbound were not
modified. No live capture was armed. Local synthetic capture was disarmed and its
temporary SQLite store removed by the fixture.

## Group memory gate closed (2026-09-24)

After explicit authorization, the exact Graphiti API was corrected to expose identity
independently of facts, and two missing canonical identities were provisioned after
legacy/alias checks. Five participants passed all ten read-only preflight probes
(180 checks; DRIFT42 / PRIVATE91 / PLEX73 controls). Zero executions or outbound.
Boundary recall remains genuinely empty. See `group-memory-preflight-2026-09-24.md`
for tests, the narrow Graphiti deployment, private evidence and rollback. Group live
activation remains separate; no capture/sender was started by this work.

## Exact-group live delivery continuation (2026-09-24)

The operator's current milestone explicitly includes real conversations in the selected
group and test data, superseding the earlier shadow-only delivery scope. No other group
is authorized. Brain/model/personality assignments remain unchanged.

Local implementation now separates `whatsapp-bound-group-text:v1` from the unchanged
DM contract. The host must explicitly enable exact-group delivery and supply matching
hashed capability evidence; project data cannot grant it. Native submission retains
exact account/recipient, single-part limit, attempt identity, synchronous authority
fence, and no fallback/retry. Default group delivery remains blocked.

The existing runtime batcher handles multiple approved participants, rejects a batch
above the remaining operational budget before execution, and suppresses duplicate/self
inputs. A confirmed group receipt recovered after restart retains group context without
resending. Tests use fake external services; these results do not establish live delivery.

Verification so far: Core 597 passed / 10 skipped before the additional receipt-recovery
test; focused recovery suite 6 passed; LLM 489 passed / 40 skipped; adapter 125 passed;
native MCP 28 passed. Core/extensions typecheck passed. Native lint/build and compiled
smoke are pending. No active deployment/capture/model/outbound in this continuation yet.
