# Host-owned Observation Bootstrap V1 — isolated implementation

## Scope

Implementation only in isolated OpenClaw v2026.8.1 and Filomena worktrees. No
active deployment, Gateway restart/config changes, real capture arm, canary,
conversational listener, Graphiti, model, outbound, or live preflight.

## Reader versus capture

The native owner now opens the SQLite journal during explicit host composition,
without creating an epoch. Reader lifetime and process-local write authority are
independent. Healthy OFF advertises stable identity, durable cursor and replay
capabilities. `no_observation_epoch` is a known result, not `unavailable`.

Explicit arm validates health and creates a fresh UUID epoch. Operational disarm
removes the capture map entry before closing the epoch; it does not destroy the
reader or payload. Close detaches the journal reader and stops capture, leaving
the shared connection to its native database owner. Restart starts OFF and can
replay retained epochs. A new arm creates a distinct epoch. No implicit crossing
of epoch cursors, no historical backfill and no auto-resume of Shadow executions.

Native queue admission/observation remains one SQLite transaction. OFF admissions
use the legacy queue without observation projection. Atomic rollback, native retry
limits, tombstone-independent payload, native IDs, PN/LID mapping and corruption
checks are retained. Bootstrap arm additionally pins the native self account;
admission checks that account inside the transaction.

## Native composition and authority

The operational entrypoint is the real Gateway startup:

`prepareGatewayServerBootstrap` → pinned host document →
`finishGatewayStartup` → `startGatewayObservation` →
`startNativeObservationBootstrap` → existing lifetime-sidecar cleanup.

`OPENCLAW_OBSERVATION_HOST_FILE` is read before runtime config environment overlays.
No variable means no subsystem. The strict host document has one native DM/account,
the actual native state directory, loopback port, and separate reader/control
secrets. It contains no arm flag or graph-controlled target. No MCP/model control
tool is registered. Native code with the same OS privileges remains trusted; this
does not claim to sandbox a malicious native plugin.

The reader is connected to the same journal and queue namespace. Config metadata
comes from `readEffectiveRuntimeConfigSnapshot`; dispatch comes from the existing
native coordinator lease plus loaded registry inspection. Missing/opaque owner
stays unknown. No access-control probe, handler invocation, pairing or config write.

GET status/control-status/epochs/open/poll are read-only. POST arm/disarm require
the separate host control credential. Arm requires current revision, inactive
dispatch and native self identity. Disarm remains available if dispatch changes.
Open/poll HTTP retain the earlier revision/isolation checks; journal-local replay
does not depend on capture. No sender or conversation execution is composed.

## .NET preflight split

New phase:

```text
--shadow-preflight <host-config> --observation-provisioning-read-only --json
```

This checks subsystem/read/control/config/dispatch reachability with capture OFF,
without opening a run or arming. JSON separates `provisioningReady` from the
embedded live preflight. `transportReady` stays false in this phase.

The existing `--transport-read-only` now additionally requires native bootstrap
evidence plus capture ARMED and an active epoch. Missing evidence fails closed.
Neither command invokes Graphiti or starts continuous ingestion.

## Evidence and limits

Tests use temporary SQLite and local HTTP fixtures. A minimal real Gateway startup
was also exercised with channels/providers disabled, temporary state and no account.
It exposed reader OFF and the real config snapshot; shutdown closed its endpoint.
This is startup composition proof, not live identity/dispatch proof.

Focused .NET verification: 15 native reader tests, 6 preflight command tests and
64 Shadow/Outbound tests passed. OpenClaw focused verification includes 24
journal/reader/bootstrap tests, 2 startup-loader tests, 3 minimal-Gateway tests,
19 configuration snapshot tests and 28 WhatsApp identity/dispatch/admission tests.

The complete OpenClaw build passed (including SDK export and startup import
guards). The final runtime-only artifact refresh also passed. Core, extensions
and messaging-test TypeScript checks passed; formatter and `git diff --check`
passed. Build emitted dependency eval/browser-externalization warnings, not errors.
The new empty-journal schema test also proves an incompatible records table
cannot be labeled healthy or armed just because there are no epochs yet.

Provisioning and rollback instructions are in the isolated OpenClaw checkout:
`observation-bootstrap-provisioning.md`. No script in this slice deploys or arms.

Live gates remain unexecuted: provisioning/arm, exact active-account identity,
effective dispatch isolation, transport probes, then separately authorized
Graphiti preflight. Shadow Run and DM Live remain unauthorized.

No commit or push performed. Historical uncommitted experimental work preserved.

## Readiness at handoff

- Controlled deployment: READY at code/local-proof level; not performed.
- Transport Live Preflight: NOT READY on the active installation; deployment,
  host-owned binding/credentials, startup OFF proof, native dispatch `inactive`
  and explicit arm are still required. The command is implemented, not executed.
- Graphiti live and first Shadow Run: not executed, not authorized by this slice.
- Zero real outbound/model/Graphiti/canary calls and zero live agent dispatch
  caused by observation APIs. Temporary test admission/control operations are
  fixtures and are not counted as real-account evidence.
