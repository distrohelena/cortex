# Bounded DM continuity window

Scope: up to five manually supplied inbound messages, one per controlled `/poll`,
in one non-resumable runtime session. Real replies are authorized for this window.
Graphiti remains read-only. No automatic listener or send retry.

## Frozen brain and operational artifact

The original `manual-shadow-project-v3.json` remains byte-exact (SHA256
`47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`).
`manual-live-project-v3-sent.json` changes only nine DM send control edges from
`shadowed` to `sent`, retaining all existing transition targets. This permits the
existing FSM transitions only after confirmed real speech. No prompt, model,
personality, memory, detector, FSM target or group edge is modified.

Derived artifact SHA256:
`B598029E0BD03B6F14F0156EAB001C28F04FB8F2D7BD817CD5F43A20F507E3A8`.
The artifact regression reverses those nine ports and requires structural equality
with frozen v3, then compiles the real pinned graph.

## Authority and limits

Default remains one interaction. `--authorize-one-response` cannot authorize five.
Five requires `--authorize-five-interactions`, host `maxInteractions=5`, the same
budget in hashed capability evidence, and the same native reader config budget.
The core accepts 1..5, counts admitted peer interactions, and separately caps
physical send attempts. Echo, snapshot and duplicates do not spend the budget.
Multiple new peer messages in one poll remain rejected before execution.
Failed preflight, runtime failure or uncertain delivery closes the window; no retry.

History and sent-state are committed by the existing runtime, not by this wrapper.
Restart starts a new run/default state; durable inbound/outbound journals are
preserved across operation directories for dedupe and reconciliation.

Models: Ler Situação and Decidir Presença use local Gemma 4 E4B through LM Studio;
generation/regeneration and semantic detectors use Luna medium. Direct involvement
continues to bypass Decidir Presença. This slice changes none of those settings.

## Verification and operation

Local full regression: 1,021 passing / 45 explicitly skipped live tests, zero
failures. Additional artifact/auth focused run: 12 passing. Build: zero warnings,
zero errors. No live model or outbound calls made by these tests.

Operation D is a fresh private ACL-restricted staging directory. Use the previously
approved deploy/rollback, scheduler pause and Graphiti startup suppression.
Require startup OFF, healthy journal, exact-DM isolation/inactive dispatch and
native + read-only Graphiti preflight before asking for any message. Preserve C's
inbound/outbound journal bytes before constructing D's runtime. At the end close
runtime, disarm capture/isolation safely, rollback, verify hashes and health.

Live results are not yet claimed; record them after manual messages arrive.

## Operation D ready for manual input

Private directory: `C:\Users\Helena\.config\openclaw-observation-ops-20260921-live-d`.
Rollback staged and verified; version/base matched. Deploy completed, Gateway PID
36800. Initial readiness request preceded port binding (connection refused); the
real lifecycle readiness gate then passed. Startup capture OFF, healthy journal,
authenticated status 11.59 ms. All ten legacy paths excluded; two old recovery
items quarantined, zero active/uncovered owners. Exact PN/LID mapping verified.

Capture epoch `2304cad5-0b07-4aee-8375-2cdc5d1a812e` is armed for the controlled
window. Runtime `326e6c50a3324eb29e25ff260dfa042e` passed native and both Graphiti
read-only preflights: namespace/canaries valid, social memory present, Boundaries
valid empty. Anchored at sequence 0; zero snapshot executions and zero sends.
Prior C journals copied byte-exact. Five-interaction capability/host budgets match.

Operational sessions: runtime **24749** awaiting `/poll`; maintenance **73108**
awaiting newline for final poll/disarm. Do not send that newline until runtime
has closed. At completion, close runtime, finish maintenance, run approved
Rollback against operation D, then verify original hashes/health/ready/capture OFF.
No automatic polling or send has started. Ask Helena for one manual DM at a time.

## Operation D closed after two confirmed turns

Turn 1: exactly one admitted inbound (sequence 1), one provider-accepted send,
speech committed and Inativa -> Engajada applied. Total 12,355 ms; local Gemma
6,637 ms, Luna 2,947 ms, send 1,052 ms. Luna usage 1,528 input + 25 output.
Social recall returned one fact; Boundary recall valid empty. Naming issue:
the trusted adapter payload supplied display name `Pessoa`, which the response
echoed. This is not evidence of a wrong canonical session/namespace.

Turn 2: sequence 2, exactly one admitted execution from Engajada, one confirmed
send, speech committed, remained Engajada. Total 21,062 ms; local situation
9,526 ms, presence 6,623 ms, Luna 3,063 ms, send 224 ms. Luna usage
1,626 input + 136 output. Social recall one fact; Boundary valid empty. Response
checked against the actual inbound and preserved its account of changed deadlines.

Turn 3 `/poll` aborted at the initial preflight with `preflight_stale`, before
reader poll/runtime admission/model/send. Total window send attempts remained 2.
Both D attempts durably end in `sent -> history_committed`; the generic CLI
`reconciliationRequired=true` is based on cumulative attempts, not evidence of
a third uncertain send. No resend or reconciliation mutation was performed.

Cause: native WhatsApp connection closed at 17:56:29 (status 428) and scheduled
reconnect. The effective snapshot, retained config hash, registry generation and
isolation gate instance stayed unchanged; the dispatch producer lifetime changed
from `1f2ab61d-aa20-4392-80f9-8fa77ce2a3c4` to
`c6ba218c-2487-4a2b-96d9-abc55b64fdcc`. The inspector is created per receiver and
its random ownerLifetime is intentionally part of the composite preflight revision.
The old run cannot silently accept this replacement proof. No safety fence weakened.

Runtime 24749 exited. Maintenance 73108 reported capture OFF/journal healthy and
cleanupFailure=null, then was terminated after cleanup. Rollback completed;
10,298 original hashes and byte-exact configs restored, OpenClaw 2026.8.1.
Fresh health/ready checks returned healthy and ready=true; control returned OFF,
healthy journal, activeEpoch=null. A subsequent check confirmed eventLoop.degraded=false.

Result: 2/5 live turns confirmed; continuity window interrupted by reconnect.
No third execution, no automatic retry, no Graphiti writes or brain changes.
Do not reuse these sessions or automatically replay the third message. A new
window requires fresh preflight and a newly requested manual inbound.

## Continuity fix — local verification complete

Approved operational artifact: `manual-live-project-v3-continuity.json`, SHA256
`1B1E2DC571D50F77ACE0AC1E6B060BB6749988996E6324BFFF24FD6C4756AFD5`.
Relative to the sent variant, only the three DM history `maxAgeMinutes` values
changed from 15 to null. Selection still caps at 20 messages and 5,000 characters.
The frozen v3 SHA256 remains `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

Live windows now checkpoint idle session context (history, state, seen IDs) to
`observation-live-context.json`. Exact host binding/project pin and fingerprints
of BOTH durable journals are checked before resuming. Missing, changed, corrupt
or uncheckpointed data fails closed; no empty-context fallback or replay. A new
epoch/window still needs fresh operational preflight/authorization. Hydration
does not call ReceiveAsync, execute a graph, perform a transition or send.
Shadow remains non-resumable; this checkpoint belongs only to bounded Live.

Offline legacy import was run against a COPY into an operator/SYSTEM-only private
directory (`filomena-live-context-20260921` under the host's private config root).
The original D journals/audit stayed unchanged. Imported 7 history entries:
4 peer inputs and 3 provider-confirmed, history-committed replies. The explicitly
reconciled not-sent attempt was not imported as speech. Latest state restored:
Engajada, checked against D's executed transition nodes and audit. Zero executions,
zero sends, no sender dependency. No Graphiti/model call or active deployment.

Legacy limitations are explicit in the private import manifest: old journals did
not persist send timestamps or state-entry time. Send history uses the associated
inbound time as a lower-bound context anchor, NOT claimed provider time; legacy
state-entry time is unknown (`MinValue`). The current runtime does not use
EnteredAt for decisions; future checkpoints retain actual runtime timestamps.
The third message was not admitted to the .NET inbox before the stale preflight;
this import does NOT claim it recovered that message or the whole WhatsApp chat.
Do not execute/re-send backlog automatically.

Verification: 1,040 tests passed, 45 opt-in tests skipped, zero failures; solution
build zero warnings/errors; diff check clean. Coverage includes reopening a new
epoch, durable dedupe, preserved confirmed speech/state, corrupt/checkpoint/journal
mismatch, uncertain sends, wrong binding/project, forged legacy transition and
receipt/audit checks. A runtime test with yesterday's input proves the approved
no-age policy selects it after reopen while the unchanged 15-minute baseline does
not. Artifact comparison proves group/prompt/FSM/model content unchanged.

Next controlled window: use the freshly built .NET runtime and the continuity
artifact. Copy the TWO journals AND `observation-live-context.json` byte-exact
from the private migrated directory, never just the old D pair. Preserve the import
manifest alongside them. Native operator generator's five-interaction branch is
pinned to this artifact; one-response branch unchanged. Do not run the generator
as a test (it performs operational read-only probes). Syntax and artifact pin
were checked offline. Fresh native/Graphiti preflights remain required. Startup
reports `restoredHistoryCount`; expect 7 before any new inbound execution.

Offline import utility (target must already be empty and ACL-restricted):

```text
dotnet run --project tools/ImportLiveContext -- <old-native-config> <new-private-directory> <old-audit> <continuity-project> <project-sha256> <inbox-sha256> <outbox-sha256> <audit-sha256>
```

No listener/capture/window was opened by this fix. Live continuity confirmation
still requires the next controlled DM interaction; do not call local tests live proof.

## Operation E — controlled continuity verification (in progress)

User authorized reopening the bounded window after the local continuity fix.
Private operation: `openclaw-observation-ops-20260921-live-e`. Reuse the approved
deployment/rollback procedure and tested native build; no new native/core changes.
Read-only initial checks: active Gateway health 200, local Graphiti health 200,
LM Studio model list includes the configured Gemma. Staging/rollback preparation
started before any active mutation. No generation, send or memory write yet.

Execution order: fresh private backup/hashes; native snapshot/maintenance config
and PN/LID validation; reviewed deployment; actual readiness/OFF proof; isolation
then explicit capture arm; prepare five-interaction Live binding; byte-exact copy
of imported journals/checkpoint; native + Graphiti preflights; verify restored
history count 7; request one new manual DM. No automatic backlog execution.
Cleanup: close .NET run, disarm maintenance, rollback, verify originals/health.

### Operation E ready — awaiting first new manual DM

Staging verified 10,298 original files and the same native build as D (12,039 core
and 68 WhatsApp staged files). Version/base compatible; three cron jobs idle;
private rollback complete. Deploy succeeded, Gateway PID **30412**. Actual
startupComplete=ready, health 200, ready=true, eventLoop.degraded=false. Initial
authenticated Observation status: capture OFF, healthy journal, 9.07 ms.
All ten legacy paths effectively excluded at the same approved snapshot revision;
two previous recovery items quarantined, no active/uncovered owners.

Maintenance session **62243** now holds the controlled capture/isolation window.
Epoch `a04b9994-c975-4a4f-ac4a-83431d6135ed`. Do NOT send newline to maintenance
until .NET runtime has finished; newline starts final Poll/disarm cleanup.

Runtime session **3044** is awaiting `/poll` (no automatic polling). Run
`848df4d062074d979a3a2648f7183fd9`, project revision
`1B1E2DC571D50F77ACE0AC1E6B060BB6749988996E6324BFFF24FD6C4756AFD5`.
Native preflight PASS; both Graphiti read-only preflights PASS (canonical subject,
namespace/isolation, social memory; Boundary valid empty). Snapshot anchor sequence
0, zero snapshot executions. `restoredHistoryCount=7` confirmed by real bootstrap.
Budget five manual interactions, exact bound route, no auto-retry. No new message,
generation or send has occurred yet. Ask Helena for ONE new continuation in DM,
then `/poll` only after her confirmation. Do not replay the unadmitted old third DM.
Private audit: `runtime-journal/native-live-92cabacfae404d5ea1e5d2a79d29e9da.jsonl`.

After the bounded window: `/quit` runtime if still running; newline maintenance;
verify disarm result; run Rollback against E and verify hashes/health/readiness.

### Operation E closed — stale receiver proof before first poll

On Helena's confirmation, `/poll` in runtime 3044 returned `ABORT/preflight_stale`
before reader Poll/admission/generation/send. `sendBoundaryAttempts=0`,
`reconciliationRequired=false`; runtime exited. This is NOT a failed generation,
memory recall or continuity test, and no delivery reconciliation/resend is needed.

Maintenance 62243 confirmed the only changed component of the approved composite
revision was producer lifetime: `12dce325-2de6-496f-a24a-358297b3d81c` became
`c45849d2-9be4-4c27-a91d-e0f792e2fd04`. Base effective config, retained hash,
registry generation and isolation instance stayed unchanged. Thus the refreshed
receiver invalidated the old operational proof; it must not be silently ignored.
Capture disarm succeeded: OFF, journal healthy, cleanupFailure=null. Maintenance
was terminated only after cleanup. Seven restored history entries remain durable;
the newly reported message was NOT admitted to the .NET runtime by this poll.

Rollback controller completed successfully and requested original Gateway start.
Final verification restored all 10,298 hashes and byte-exact configuration/launcher,
version 2026.8.1/base expected. Verifier 12259 completed; its first health/control
requests overlapped startup and timed out, so they were not treated as proof.
Fresh subsequent checks returned health HTTP 200/ok, ready HTTP 200/true,
eventLoop.degraded=false, authenticated control HTTP 200/capture OFF/journal healthy,
no active epoch. Original service restored; no pending cleanup.
No experimental runtime remains, no new .NET execution/model/send occurred.
Do not ask for an automatic resend or claim the live continuity test passed.

### Bounded receiver revalidation (2026-09-22)

Operation E established a receiver-lifetime change, not a conversation reset or
failed send. The .NET reader now mints a fresh reader-local approval only from
authenticated native status with the SAME valid base snapshot, gate instance,
gate coverage/placement, registry generation, exact route/identities, epoch,
capabilities, effective paths and quarantined legacy-work inventory. Unknown,
missing or changed scope fails closed. The composite revision is additionally
checked against its existing v1 shape; only the explicit producer component may
change. Revision text never substitutes for the valid structured snapshot proof.

Host opt-in `revalidateReceiverWithinWindow` defaults false and must match the
hashed host capability-evidence document. Five-interaction provisioning enables
it explicitly. Renewal happens ONLY while idle, before `/poll`: fresh native
proof, provisioning checks and bound-route description. The same cursor, run,
context, inbound registry and remaining interaction/send budget are retained.
No Open/rearm, automatic polling, execution replay or send retry occurs.
After Poll and before send, the renewed revision must remain exact; scope/epoch
changes are checked again and abort. A reconnect during execution is not retried.

Local verification: 1,059 passed / 45 skipped; build zero warnings/errors;
targeted diff whitespace check clean; provisioning helper syntax check passed.
An existing recent-context test failed once under the full suite, then passed
in isolation and on two complete reruns; no brain/context-rule edit was made.
New tests cover cursor-preserving receiver renewal, scope changes, invalid
bootstrap/snapshot/dispatch, registry changes, recovery changes, foreign reader
authority, wrong epoch, missing proof and host/evidence opt-in mismatch.
Brain v3 and approved continuity-project SHA256 values remain unchanged.

Operation F staging: private directory
`C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-f`.
Active deployment/live continuity have NOT yet been validated for this fix.

### Operation F closed before arm

Native artifacts were byte-identical to E. Host readiness passed; authenticated
control responded in 4.98 ms, capture OFF, healthy journal. Effective snapshot
was `unknown/native_owner_missing_ambiguous_or_unavailable` and the dispatch
inspector was not available yet. Procedure stopped before isolation/capture arm,
Open, .NET runtime, models or sends. Cleanup OFF/healthy; rollback restored all
10,298 hashes and byte-exact configs. Fresh health/ready/control all HTTP 200,
ready=true, eventLoop.degraded=false, capture OFF. No pending cleanup.

The provisioning helper previously returned success on host readiness and a
readable journal even when native dispatch-owner proof was still unavailable.
Added an explicit `requireNativeDispatch` option for the real maintenance path:
wait for valid native owner proof with repeated read-only checks and process
lifetime cancellation, reporting the pending diagnostic once. No elapsed-time
cutoff or blind sleep is authority; unsupported/invalid snapshot still fails.
Local reproduction failed before the fix; 19 focused provisioning/control tests
passed afterwards. This changes operator procedure only, not the native build.
Operation G uses a fresh private rollback directory and the same native artifacts.

### Operation G ready for one manual continuation

Directory: `C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-g`.
Native artifacts identical to F/E; Gateway PID **51980**. The new procedure
reported pending native-owner proof once, then obtained a valid proof without
loosening checks. Host provisioning PASS; capture initially OFF; status 17 ms.
Isolation has all ten effective paths excluded, aggregate INACTIVE, two prior
legacy recovery items quarantined, zero active/uncovered owners.

Maintenance TTY **39015** holds capture/isolation. Do NOT send newline until
the .NET interaction window ends; newline begins final Poll/disarm cleanup.
Epoch `4b3d6b13-b584-4dcb-ad04-2a0d8b4bf8bf`.

Runtime TTY **10018** is ready and awaiting `/poll`, not continuously polling.
Run `69a6afa7f6cd4c27bbb53e07ea344a7b`; private audit
`runtime-journal/native-live-956dd08f9a1d84f2a82c6ac21774c33ba.jsonl`.
Native preflight PASS; both Graphiti read-only probes PASS, including canonical
identity, namespace/canary exclusion, social memory; Boundaries valid empty.
Snapshot sequence 0, zero snapshot executions, `restoredHistoryCount=7`, budget
five manual interactions. No new model/generation/send has happened yet.

Review against the real v1 producer shape added a retained-config hash fence:
producer is receiver UUID plus retained SHA256; ONLY UUID may change. Regression
first reproduced accidental hash acceptance, then passed after the fingerprint
retained it. Final .NET suites: **1,060 passed / 45 skipped**, build zero warnings
or errors. Procedure tests 19 passed, lint and formatting clean. Brain/project
hashes unchanged. Receiver-renewal behavior is locally proven, not yet exercised
by a real reconnect during this new live window.

Next: ask Helena for ONE new continuation in the DM, then send `/poll\n` to
10018 after her confirmation. Do not replay E's unadmitted message or old sends.
On completion/abort: `/quit` runtime if running, newline maintenance, verify
capture OFF/journal healthy, then Rollback G and verify hashes/health/ready.
Maintenance may report its old canary revision stale after a legitimate receiver
renewal; its finally still disarms. Do not treat that as an automatic resend.

### G interaction 1 and approved punctuation variant

After Helena confirmed her manual message, one `/poll` admitted sequence 1 once.
Execution succeeded in 14.105 s, direct involvement, state Engajada. Generation
correctly recalled the prior deadline change and resulting rush from restored
conversation context. One provider-accepted send and one speech commit were
reported. Graphiti social recall returned one fact; Boundary recall valid empty.
No automatic resend. Runtime 10018 and maintenance 39015 remain open; budget
used 1/5. A later private checkpoint/audit verification was interrupted after
an incorrect audit filename lookup, so do not claim that extra check passed.

Helena explicitly approved a small punctuation preference after this response:
short sentences, commas and natural line breaks; em dashes only when apt, not
as the default pause, and no systematic semicolon replacement. Prepared ONLY
`manual-live-project-v3-conversational.json`, SHA256
`E01D0012A223EABAACEE5D741EFBC19098E5185790543E1594481AFB4832A792`.
It appends the same short clause to DM generation/regeneration in all three
states (six prompts). Everything else is structurally identical to continuity
v3, including group paths, models, grounding, fidelity, FSM and enforcement.
Frozen v3 and active continuity hashes unchanged. Tests: 1,061 passed, 45 skipped.
The first test build hit the running App.exe lock; tests were then built/run with
BuildProjectReferences=false against existing dependencies, leaving the run up.

This variant is NOT active in G and the provisioning helper still pins continuity
v3. Activate only through a new explicit project pin and reviewed context carryover,
not hot reload or silently bypassing the context checkpoint's revision binding.
No new model call or send was made for the punctuation change; its linguistic
effect has not been measured live. No new checker or output text rewriting.

### H: approved conversational revision activation, 2026-09-22

G runtime 10018 closed normally (one execution/send attempt); maintenance 39015
confirmed capture OFF and journal healthy before exiting. Native Gateway PID
51980 was retained without redeploy. **Native rollback owner remains G**, not H.

H private window: `C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-h`.
Inherited G's restricted ACL; copied only host/baseline/scheduler/MCP operational
inputs. No old inbound was replayed. The updated preparation helper pins the
approved conversational SHA above. An explicit offline checkpoint transfer
validates the approved source/target revision pair, session binding, source
hashes, terminal journals and runtime context schema. It preserves exact
timestamps, seen IDs and participation state; no Receive, model or sender call.
Regression covers valid transfer, wrong hash/session/project and corrupt checkpoint.

Verification: 1,066 tests passed, 45 skipped; build zero warnings/errors.
Transfer result: nine history entries, Engajada, zero executions and sends.
Transport and Graphiti read-only preflights passed; social memory present,
Boundary valid empty, namespace/canaries passed. Legacy dispatch inactive.
Fresh H epoch: `67f2a6a1-6639-493d-acc6-1f7ef1742205`; snapshot sequence 0,
zero snapshot executions. Maintenance session 12959; runtime session 53734.
No manual inbound polled yet; punctuation effect still awaits a real reply.

Cleanup: `/quit` runtime first; newline maintenance 12959 and verify capture
OFF/journal healthy, then native Rollback **G** and hash/health/ready verification.
H does not own native displaced artifacts. Preserve both windows' private journals.

#### H interaction 1

After Helena confirmed a manual DM, one explicit `/poll` admitted native sequence
1 (fromMe=false), producing exactly one successful execution and one
provider-accepted send with speech commit. Total 20.390 s; situation 7.932 s,
presence 5.192 s (local Gemma), Luna generation 4.951 s, send 0.664 s.
Engajada retained. Graphiti social recall one fact, Boundaries valid empty.
Reply used natural sentence/paragraph punctuation: zero em dashes and zero
semicolons. This is one observed sample, not a universal style guarantee.
No full message content copied into this report. Window remains open at 1/5;
no automatic polling or resend. Human delivery confirmation remains separate
from provider acceptance.

### I: conversational care posture, 2026-09-22

Helena confirmed H's reply arrived, but rejected unsolicited medical triage for
a literal symptom in a vent. Explicit approval: brief social care, optional light
question about having seen a doctor, not a checklist; preserve clear immediate
danger response and requested guidance. Changed only six DM generation/regeneration
prompts in `manual-live-project-v3-care.json`; clause in
`conversational-care-clause.txt`. Final SHA256:
`E6542C60C32F2C8FE04DC5FB9928794FB9B2CA8AC375BE861D790B40E03B0BE1`.
Original v3, situation/presence, FSM, models, grounding/fidelity, Boundary and
Graphiti unchanged. No new checker. Exact approved checkpoint-transfer pair added.

Generator-only synthetic evaluation: three iterations of six Luna medium cases
(18 calls, zero transport, no memory). Final six used current personality and
Engajada guidance; outputs preserved as `conversational-care-probe-*.jsonl`.
Operator review: three vents had no emergency checklist; explicit request received
advice; both concrete-danger cases prompted immediate help, including refusal of
advice. Earlier iteration incorrectly offered an ungrounded emergency number;
final clause forbids numbers without explicit location, and final danger samples
used local emergency service without guessing numbers. Not a clinical validation.
Remaining style issues: case 2 still says "sem precisar virar consulta"; case 1
is slightly admonitory; requested-advice sample remains long. Do not claim perfect
naturalness or full medical correctness. No further tuning in this slice.

Final local regressions: 1,068 passed, 46 skipped; build zero warnings/errors.
Structural test verifies all non-prompt configuration unchanged and real project
compilation; model outputs reviewed separately, not inferred from unit tests.

H runtime 53734 closed normally (one send); maintenance 12959 confirmed capture
OFF, journal healthy, cleanupFailure null. I uses a fresh restricted private
window at `C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-i`.
No native redeploy. **Rollback remains owned by G**.
Exact checkpoint transferred H to I: 11 history entries, Engajada, timestamps
and dedupe preserved, zero replay/executions/sends. Transport and Graphiti
read-only preflights passed, Boundary valid empty. Legacy dispatch inactive.

I maintenance session 16876; runtime session 27462;
run `6dc140506b5a4518b7d1ea765d961005`;
epoch `798f774f-7dd2-419e-bad0-30df18c9aeb7`, initial cursor 0.
Awaiting manual message and confirmation; no automatic polling, 0/5 executions.
Cleanup: `/quit` runtime, newline maintenance, verify capture OFF/healthy,
then native rollback using G and verify original hashes/health/ready.

### J: window re-established after I process exit

Helena confirmed a new message, but writing `/poll` to I session 27462 returned
exit 1 with no output. No Filomena.App process remained. I audit ended at
run_started; inbound journal contained only the new run header, no admission or
execution. Cause of process exit is not established. Capture was still armed and
healthy. A fresh process correctly refused stale initial approval (zero sends):
native producer instance changed, retained config hash/registry unchanged.
These are separate observations, not proof that reconnect killed the process.

Maintenance 16876 then disarmed I, journal healthy, cleanupFailure null. No old
event was replayed. J re-provisioned same deployed build against current revision,
dispatch inactive, new gate and epoch `44fde02e-d611-4edc-b41b-dc2f83acb864`.
J private root: `C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-j`.
Exact I checkpoint transferred: 11 history entries, zero executions/sends.
Same approved care project SHA; both preflights passed. Runtime launched directly
from built DLL (no dotnet-run build wrapper), session 38915, run
`73bad14e82e745c792336d7fa55acf29`; maintenance session 13556.
Cursor 0, snapshot zero records, no executions. Await a NEW manual message;
the previous message has not been processed by the runtime. Do not pretend it
was answered or import it as a new event. Native rollback owner remains G.

### Explicit native pending-message recovery

Helena approved processing received native messages with no processed marker,
rather than asking her to resend. Added host-only `NativeReplayPlan` to the
existing native live command. It selects a finite closed-epoch interval (max 20)
using its original cursor, preserves native ID/epoch/sequence, validates full
contiguity and scope, and admits records serially through the existing live
runtime. Current active-window safety cursor/approval is separate from historical
read cursor. Every send retains current isolation/route/preflight checks.
Shadow mode cannot opt into this. No transcript heuristic or synthetic event.

Existing inbox dedupe suppresses processed records, including silent completed
executions. Checkpoint/journal fingerprints and outbox reconciliation still refuse
interrupted or uncertain work; this does not claim automatic crash recovery for
all admitted-but-uncommitted executions. Chronology is compared against the
history captured before recovery, not against replies generated during recovery.
Default snapshot behavior unchanged; replay is an explicit host recovery scope.

Local verification: 1,080 passed, 46 skipped; build clean. New tests cover closed
epoch order/identity, wrong session/version/epoch, gaps, changed tail, invalid
scope, and two missed native events recovered once with zero resends on restart.
`tools/prepare-native-recovery.mjs` read the I native journal and found exactly
one original record after C0 in closed epoch I. Prepared separate private
J `native-recovery.json`/`live-recovery.json`; original configs unchanged.

The subsequent runtime launch was rejected by tool authorization review BEFORE
process creation because it could send a real WhatsApp reply. No recovery model
call, execution or send occurred. Await explicit confirmation for one real reply;
do not work around the rejection. Original message remains in native epoch I.
Native rollback remains G; J maintenance cleanup requested after the rejection.

### K: authorized one-response native recovery

Helena explicitly authorized one real response. K was prepared with
`--one-care-response`; native/live/evidence limits were ONE from creation.
The initial piped-input run closed with zero executions/sends. The interactive
run `0a8d9bd68dca44ffb836364ea34d3cba` then recovered the original epoch-I
sequence 1 record, preserving its native identity. Exactly one admission,
execution and send attempt completed: `sent / provider_accepted`, followed by
`speech-committed` and `run_closed` with outbound disabled. No retry was issued.
Provider acceptance is not independent confirmation of handset delivery.

Total interaction duration: 29,112.73 ms; local situation call: 22,390.14 ms;
Luna medium generation: 4,243.02 ms, 2,947 input / 145 output tokens.
The response still included unsolicited health advice; the care clause is not
a guarantee of the desired conversational tone. No additional tuning here.

K capture cleanup confirmed OFF, journal healthy, cleanupFailure null. Current
epoch K had zero new inbound records; recovery consumed only the approved old
record. Original G rollback requested after config/launcher hashes matched.
Rollback completed: 10,298 artifact hashes verified, original configs byte-exact,
OpenClaw 2026.8.1 / expected base verified. Initial startup probes timed out;
subsequent read-only checks returned healthz 200, readyz 200 / ready=true,
eventLoop.degraded=false. No retry/send or additional live execution followed.

### L: approved care-tone v3 operational test

Helena confirmed K delivery and approved testing the locally evaluated care-v3
artifact (`BDE240996D7739FF7CB025DDDF9998928F22D06CE7238DA50DC9FD58E2C25686`).
One new manual inbound / at most one real reply; no replay of the processed K
message. New private window: `openclaw-observation-ops-20260922-live-l`.
This is a fresh native deployment; L, not G, owns its rollback.

Added the exact old-care to care-v3 checkpoint-transfer pair and tested exact
history/timestamp/dedupe preservation. Full regression: 1,083 passing, 46 skipped;
import tool build clean. New provisioning flag `--one-care-v3-response` pins the
approved artifact and retains maxInteractions=1 from creation. No authority,
model, FSM, Graphiti, outbound implementation or native journal changes.

Staging and service checks underway; readiness, arm and runtime activation are
not yet claimed. LM Studio, Graphiti health and original Gateway ready returned
HTTP 200. Do not ask for the new manual message until preflights and history
restore succeed. Cleanup: runtime closes, maintenance disarms, then L rollback
and hash/health verification. No message was sent during preparation.

L ready: native artifacts match G's validated manifest exactly. Gateway PID 55488;
maintenance session 69226. Startup capture OFF / journal healthy proved before
arm. Dispatch inactive; two legacy recovery items remain explicitly quarantined,
zero active/uncovered owners. New epoch `ec793549-edc0-49b2-b60c-386a9c7fcff3`.
Exact route unique, limits ONE. K checkpoint imported offline: 13 history entries,
Engajada, zero executions/sends, timestamps and dedupe retained.

Transport and Graphiti read-only preflights passed (social memory present,
Boundaries valid empty, namespace/canaries passed). Runtime session 77542;
run `987351bb55404e61a6638e05117806b5`, native cursor 0, snapshot zero,
restoredHistoryCount 13. Await one NEW manual DM and confirmation, then exactly
one `/poll`; no poll or outbound performed yet. Evidence:
`runtime-journal/native-live-25bee8f51a96f46368ef6c57c03ca118a.jsonl` in L.
After this one interaction close/disarm and execute L rollback, NOT G.

L manual message processed once, sequence 1 / fromMe=false. Run closed normally:
one execution, zero send attempts, outbound disabled on close. Local Gemma
classified indirect involvement; presence chose `calar / sem_contribuicao`,
continue following=true. State remained Engajada. No Luna generation occurred,
so the live interaction did NOT validate the care-v3 tone. Total 19,113.22 ms;
situation 10,006.88 ms and presence 7,474.36 ms. The message is processed, not
missing, and must not be replayed to force a reply. No participation rule changed.

Maintenance confirmed capture OFF, journal healthy, cleanupFailure null. L
rollback requested after active config/launcher hashes matched the prepared
maintenance artifacts. Preserve the updated checkpoint for future context.
L rollback verified 10,298 artifact hashes and byte-exact configs, version/base
2026.8.1 expected. After startup, two consecutive health/ready checks passed:
healthz 200, readyz 200 / ready=true, eventLoop.degraded=false. Restored Gateway
PID 7348. Capture was OFF before rollback. No additional input was polled.
