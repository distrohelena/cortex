# Continuous exact-group operation

Helena authorized removing the experimental interaction-count cap. Scope remains the existing trusted Drift group/account/participant bindings, not arbitrary chats or an expanded roster.

## Code

- `--authorize-continuous-group` requires `continuousGroup=true` in both the host live document and its SHA-256-bound capability evidence. Exact group delivery remains required. Defaults remain bounded.
- `ObservedLiveRun` rejects continuous authority for DM or a route without exact-group authorization. Only the interaction/attempt count fence is removed; pre-send checks, binding, dedupe, self suppression, durable outbox, uncertain-send stop and context persistence remain unchanged.
- An optional matching `continueAfter` in the host document and hashed proof permits explicit handoff within the current v2 session/epoch. It cannot point into a different stream or beyond the opened cursor. This is not automatic restart or recovery of an uncertain execution.
- Runtime failure still stops the process. No model, prompt, FSM or memory-semantic changes.

## Verification

Full solution on isolated build output: 1,239 passed, 50 skipped, zero failed (Core 603, OpenClaw 128, LLM 508). Includes seven sequential group interactions, bounded five-turn contrast, repeated native-event dedupe, explicit CLI/host/proof authority, and continuation session/epoch/version/sequence rejection.

The initial external artifacts directory broke fixture-root discovery; rerunning with separate artifacts under this worktree restored the complete suite. Production DLLs in use were not replaced. `git diff --check` passed; PowerShell and Node provisioning-script syntax checked. Frozen operational project SHA-256 remains `A3B066C5B05B3D28E1B429CE0D900C38C8FF82D892429C3D2E42A4E2B15AAF97`.

## Operational handoff

Private evidence/configuration: `C:\Users\Helena\.config\openclaw-observation-ops-20260924-group02\drift-continuous`.

Old .NET runtime PID 38032 was verified to have zero admissions, zero send attempts and completed cursor sequence zero in this window before stopping. Capture/isolation stayed armed. The new runtime uses the same journal/context directory and explicit sequence-zero cursor in the same epoch; queued native observations are not skipped by a fresh snapshot.

Verified after launch:

- transport preflight passed; dispatch inactive;
- 180 Graphiti checks passed, none failed; read-only;
- `run_started`, `continuousGroup=true`, `maxInteractions=null`;
- three history entries restored;
- eleven controlled polls completed without abort at the initial verification point.

No synthetic messages were sent to test activation. This proves continuous operation is armed/running, not seven real group conversations; seven-turn coverage above is local/fake transport.

## Stop and rollback

The launcher checks a host-local `stop.request` file in this private directory. It sends `/quit` only at an idle poll boundary, after any in-flight interaction completes. Verify `run_closed`, outbound disabled, child exit, and journal reconciliation state before disarming capture/isolation through the existing maintenance controller. Do not release legacy isolation during an in-flight send.

Full Gateway rollback remains the existing group02 deploy-control procedure. The maintenance scheduler pause and Graphiti automatic-startup suppression remain in effect; no automatic service restart/rearm has been added. Restart requires explicit operator preflight and checked continuation/reconciliation.

Known scope: only the five currently trusted canonical participants (ten PN/LID aliases) are provisioned. This is not proof of a fully dynamic group-membership roster.
