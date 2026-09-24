# DM Live local resumption — 2026-09-21

Reviewed the handoff at `4f4c439`, following `4e16e12`. Existing work was
preserved, including the uncommitted Graphiti plugin changes (not modified here).
The user's concern is the previous agent's execution/process, not a request to
retune the brain.

## Reproduced and fixed

OutboundJournal enforced transition legality through Resolve, but its constructor
accepted individual known-status rows without validating their history. Ten new
negative restart tests reproduced acceptance of orphan terminal records, skipped
transitions, duplicate reservations, terminal reopening, changed session/execution/
inputs/enforcement revision/namespace, and sent records without a provider receipt.

Replay and append now share ValidateNext. Invalid histories fail before restart
recovery appends anything; the original bytes remain available for investigation.
Valid pending recovery, durable receipts and explicit reconciliation continue through
the existing state machine. No repair, retry or send is inferred from corruption.
The old Shadow fixture now writes a real pending-to-indeterminate sequence rather
than an orphan resolution. This does not change Shadow dispatch semantics.

## Verification

- New tests before fix: 10 failed (expected exception absent), 1 positive passed.
- Final `dotnet test Filomena.slnx --no-restore --nologo -v minimal`: exit 0.
- Core: 549 passed, 10 live experiments skipped.
- OpenClaw: 71 passed.
- LLM: 355 passed, 35 live experiments skipped.
- Total: 975 passed, 45 skipped. Application compiled in this run.
- Targeted diff check passed.
- Frozen brain SHA256 remains
  `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.

One earlier full Core run timed out in
`QueuedSuccessorSeesConfirmedPredecessorSpeechWithoutFutureInboundOrRepin`
at its final WaitForIdleAsync. The isolated rerun and final solution run passed
unchanged. This intermittent test failure is not claimed fixed; no timeout was
increased and no runtime timing behavior was changed.

## Remaining operational gap

The received implementation supplies a fake-tested Core live composition, not an
App command that safely runs DM Live. The existing route description and send are
separate calls, so route atomicity and the native single-message/no-hidden-retry
contract still need to be established before wiring a real sender. Complete
capability booleans are not a substitute for these guarantees.

No live send, deployment, model call, Graphiti call/write or capture arm was performed
in this resumption. DM Live remains disabled. Next work is the bounded transport
contract and executable composition, retaining frozen brain and Shadow behavior.

## Bound sender contract follow-up

Two additional negative tests reproduced that capability booleans could enable the
legacy describe-then-send adapter, and that a receipt without account was accepted
as confirmed. Both were fixed locally:

- Enabled now requires `IBoundOutboundTransport` in addition to the existing checks.
- `BoundSendRequest` carries the persisted attempt reference, expected route,
  channel, account, recipient, exact authorized text and single-part ceiling.
- The host dispatches through that operation only, without a legacy fallback.
- Receipt account is mandatory; absent metadata leaves the outcome indeterminate.
- Tests verify the exact request, route change after description, and zero calls
  to a legacy adapter even when all capability booleans are supplied as true.
- Runtime fakes throw if the legacy send is invoked. Their bound operation is
  explicitly a simulation, not evidence of native atomicity.

The existing MCP sender remains legacy-only; it is deliberately not advertised as
bound. Implementing the interface is a trusted code contract, not independent
proof of native behavior. A native implementation must still be verified at the
physical send boundary for routing, idempotency, queueing/retry and chunking.

Verification: `dotnet test Filomena.slnx --no-restore --nologo -v minimal` completed
with exit 0 after this change. Live experiments stayed skipped. Diff check passed;
the brain hash above is unchanged. No App live command or active deployment was
added. At that checkpoint the native bound sender and operational composition were unfinished.

## Approved bound connection follow-up

The native sender and the MCP/Gateway/.NET connection are now implemented in isolated source.
See `native-bound-sender-local-20260921.md` for exact guarantees, test results and live limitations.
The distinct `OpenClawBoundMcpTransport` is intentionally not substituted into the old legacy host.
Fresh solution verification: Core 551 passed/10 skipped; OpenClaw 88 passed; LLM 355 passed/35 skipped
(994 passed, 45 live experiments skipped). The frozen project hash is unchanged.

Remaining operational work is a reviewed App live composition/window, active provisioning and a
controlled real canary. This follow-up did not deploy, enable DM Live, send messages or call models.

## One-inbound observed composition follow-up

Added `ObservedLiveRun`, reusing the existing observed admission/cursor validation and
real session handoff. The public Shadow constructor still has no sender and a fixed
Shadow ceiling. The separate Live wrapper requires a bound sender, complete explicit
capabilities and a final host pre-send check. It rejects a batch containing two new peer
IDs before admission, suppresses self/backlog/duplicates, permits at most one transport
attempt and disables its host after the first admitted interaction. No retry/rearm is
created. A failed final check is conservatively indeterminate in the existing journal.

Five new local tests use fake external dependencies and the real runtime. Solution
verification completed successfully: Core 556 passed/10 skipped, OpenClaw 88 passed,
LLM 355 passed/35 skipped (999 passed, 45 live experiments skipped). Diff check passed.
The frozen v3 hash above remains unchanged.

The automatic tool reviewer rejected the proposed App/MCP Live CLI patch as requiring
explicit authorization to introduce a persistent real-send entrypoint. That patch did
not apply; its temporary uncompilable tests were removed. No CLI, active deployment,
capture arm, model/Graphiti request or real send happened in this follow-up. Next step
requires that authorization; the existing App still exposes Shadow only through the
native observation command. Core fake-tested composition is not a live readiness claim.

## Explicitly authorized CLI follow-up

After renewed operator authorization, the local App patch was applied:
`--native-live <host-document> --authorize-one-response`, separately from Shadow.
It validates a pinned, versioned host capability artifact bound to native session
and config revision, reuses native/Graphiti preflights, checks the bound MCP tool
and route, and repeats native preflight at the final .NET send boundary.
Solution verification: 1,005 passed, 45 live experiments skipped; build clean.

Installed-layout smoke caught an undeclared `zod` import in the native bound sender
hidden by checkout hoisting. It now uses the declared TypeBox dependency, copying
validated scalar fields before yielding. All 22 focused tests and the installed
LID smoke passed. Three isolated full-host startups reached ready and authenticated
OFF status (5.55, 5.16, 3.67 ms). No active files changed.

The tool authorization layer separately rejected creation/ACL preparation of the
new operation directory outside the worktree, citing a design-only restriction.
The command did not run. See `native-live-handoff.md` for invocation and the frozen
graph's post-send transition limitation. No real model/Graphiti/send call occurred.

## Explicit backup/deploy authorization and tool-policy blocker

The operator subsequently explicitly authorized the private ACL-restricted backup,
controlled active deployment/rollback and one real response in her DM. Private
operation `openclaw-observation-ops-20260921-live-a` was created with only the
operator and SYSTEM access. Original/staged core and WhatsApp artifacts were
hash-verified (10,230/12,032 core files and 68 WhatsApp files respectively).
The original Gateway's stop command initially reported a process still exiting;
read-only checks subsequently proved that process and listeners had terminated.
The unchanged controlled deployment procedure then completed.

The compiled runtime exclusion smoke passed again. The frozen brain hash remained
unchanged. Existing Graphiti/FalkorDB containers and the configured local Gemma
and embedding models were started; Graphiti HTTP health returned 200. No model
generation, memory write, capture arm or real send was performed.

The automatic tool reviewer rejected the existing `canary-probe` command twice,
citing historical no-Shadow authorization, including after the latest explicit
one-response authorization was quoted. Neither command executed. No alternative
arm path was attempted. The approved rollback was requested immediately; final
restoration/health evidence must be recorded separately when it completes.

Rollback controller subsequently completed. Fresh verification matched all 10,298
original artifact hashes and byte-exact launcher, OpenClaw config and observation
host config. Version/base remained 2026.8.1 / ea806575. All three cron definitions
and enabled flags matched the baseline. The restored Gateway process (59584)
remained alive with its original listeners, but HTTP health/readiness was not yet
confirmed while startup continued. A read-only readiness verifier remains running;
do not interpret artifact restoration as proven service readiness. No isolation
arm, capture arm, native Open/Poll, brain execution, model generation or send was
issued by this attempt. Existing Graphiti/LM Studio services were left running.

## Renewed explicit arm approval and route-shape regression

The operator explicitly approved arming capture/isolation for the one-response DM
test. Before deployment, read-only persisted session metadata exposed a real
contract mismatch: the outbound delivery route is E.164, while native observation
uses the account-owned LID mapping. These are not interchangeable literal strings.
The bound MCP bridge and .NET adapter now normalize only exact E.164 syntax to PN;
the live host binds outbound to the already trusted logical PN session. Inbound
observation remains bound to the verified LID. No alias guessing or brain change.

Regression first reproduced the mismatch, then passed: 11 native bridge tests,
94 OpenClaw adapter tests and 7 live-command tests. The .NET solution regression
completed successfully and solution build reported zero warnings/errors. Frozen
v3 SHA256 still matches the value above. A generic root `tsgo --noEmit` invocation
included unrelated test/tooling fixtures and failed; production core/WhatsApp
typechecks and the corrected native build are being verified through their actual
repository configurations. Operation B is staged but not deployed; its older next
artifacts must be refreshed and hash-verified after this rebuild.

Fresh restored-host checks subsequently returned `/healthz` live and `/readyz`
ready=true with eventLoop.degraded=false. Production core/WhatsApp typechecks
passed. Operation B captured the three idle cron definitions, unchanged original
configuration, and the account-owned bidirectional PN/LID mapping successfully;
all preparation remained private with activeMutation=false.

## Operation B: one inbound, failed before native sender load

Operation B subsequently passed native/Graphiti preflights and processed the
operator's manually sent message once. Run `546c540cbb6f4ac2b047dfe01c685127`,
execution `0ac49fb5978441ceac1fedf9ccdc4962`: one admission, sequence 1,
fromMe=false, direct-involvement route, one generation, one send-boundary attempt.
Boundary recall was valid empty; social recall returned one item. No memory write.
The frozen v3 SHA256 remained unchanged.

Result: **NO_CONFIRMED_SEND**, transport failure, durable **indeterminate**,
reconciliation required. There was no retry. The Gateway's correlated send error
at 2026-09-21T18:49:21.651Z was `MissingPublicSurfaceError`: unable to resolve
bundled `whatsapp/runtime-api.js`. The deployed WhatsApp owner is an external
installed plugin. This exception occurs before listener lookup/native submission;
it is evidence of a pre-submission failure, not a receipt of delivery. The durable
indeterminate record is preserved; no automatic reconciliation, replay or retry.

Measured pipeline: 38,167.9852 ms total; Ler Situação 31,692 ms, generation 4,447 ms,
Boundary recall 138 ms, social recall 190 ms. Luna usage: 1,537 input and 107 output
tokens (72 reasoning), 1,644 total. No monetary price inferred. This was an
authorized live attempt, not a zero-model Shadow test.

The runtime exited after failure. The maintenance controller then disarmed capture
and isolation, preserving the epoch. Approved rollback completed. Fresh verifier:
10,298 original artifact hashes restored, config/launcher/host byte-exact,
OpenClaw 2026.8.1, health HTTP 200/ok=true, ready HTTP 200/ready=true,
eventLoop.degraded=false, authenticated control capture=off/journal=healthy.

Local regression reproduced the exact loader error with a registered external
plugin and the real public loader. The correction selects only the unique,
absolute-root, host-registered WhatsApp owner and uses the existing generic public
artifact loader. Owner identity, registry generation and authority are rechecked
at the native fence; missing/ambiguous/revoked ownership never falls back to a
different package or legacy sender. Gateway suite: 151 passed; core and WhatsApp
production typechecks and focused lint passed. A compiled installed-layout smoke
also reproduced the original failure when bundled fallback was disabled. The
corrected compiled artifact still requires its post-build smoke verification.

No new deployment or old-attempt resend is authorized by a passing local test.
Before another live window, explicitly reconcile the preserved uncertain attempt
against evidence; never bypass it by silently choosing a fresh journal.

### Reconciliation and corrected artifact verification

Helena explicitly confirmed that the generated response did not arrive. Using
that confirmation plus the correlated pre-loader Gateway exception, the existing
domain reconciliation API appended `not_sent` for attempt
`f7904f27a3de4471ae15fac70b7fe21e`. Original bytes remain intact, with an immutable
backup inside the same ACL-restricted directory. Before SHA256:
`E0CCE92355106D4E8E595FA614259A690F1DF4B84DB300275E8BB9C80D44A89B`;
after SHA256: `3BAAEB7B390357AD71C8D54C37E5AC8302895F819EAB6339EE1495992506E7CA`.
No sender was constructed, no rearm/retry occurred. The local operator helper is
`tools/ReconcileNotSent`; it requires a pinned journal hash, exact attempt/session,
named evidence and explicit apply. Its first apply stopped before reconciliation
because Windows disallowed its own concurrent read handle; the corrected reader
uses the existing owner's permitted sharing, retaining exclusive writer ownership.

Additional checks: 33 MCP/native sender tests and 28 existing reconciliation/live
host tests passed. Official native build completed successfully in 10m17.9s.
Installed-layout smoke with bundled fallback disabled passed for both PN and LID:
real compiled Gateway, external public loader, channel runtime context, packaged
monitor and native bound sender, fake socket only; exactly one fake send and
duplicate attempt rejected. Three isolated full-host startups passed with capture
OFF; authenticated status 4.12/3.81/3.69 ms. Frozen v3 unchanged.

### Operation C: fresh one-response window ready

Controlled deployment of the corrected artifact completed, with a new private
rollback directory. Health/startup/ready were all valid; capture initially OFF,
journal healthy, self available and effective snapshot valid. Exact-DM isolation
then produced inactive dispatch, with all ten paths effectively excluded while raw
evidence remained visible. Two persisted recovery items remain quarantined;
uncovered/active recovery owners are zero. Temporary scheduler pause is unchanged.

Native capture epoch: `14fd07aa-c40e-412c-a5d0-57ca60506b8c`.
Runtime `95cfbf646d7449ab955fa33921b457c4` passed native and both read-only Graphiti
preflights, anchored at sequence 0 with zero snapshot executions, and now awaits
one NEW manual message followed by operator `/poll`. Original inbox and reconciled
outbound journals were copied byte-exact into the new restricted window before
runtime construction; no history reset or old-message replay. No send yet in C.

Operational sessions: native maintenance 25328, one-response runtime 59453,
Gateway PID 47116. Stop runtime before closing native maintenance, then use
the approved rollback for `openclaw-observation-ops-20260921-live-c`.

### Operation C: one real reply accepted

After Helena confirmed the NEW manual inbound, `/poll` was invoked once. Native
sequence 1/fromMe=false produced exactly one admitted execution
`ac3dbab76e284056aaf878abc43ea7b1`, using the frozen v3 direct-involvement rule.
Candidate and actual submitted text: **Estou. Diga.**

Result: **SENT / provider_accepted**, exactly one send-boundary attempt. Durable
attempt `4f46787ffe784b709243f866b8c39223` has `pending -> sent` with provider message
ID present, then `history_committed`. No retry. The previous failed attempt remains
audited as `not_sent`; it was not replayed. Runtime closed successfully with
executions=1, sendBoundaryAttempts=1, outboundMode=disabled_on_close.

Total pipeline: 12,172.5286 ms; Ler Situação 6,993.6168 ms, generation 2,498.3342 ms,
native send node 818.4638 ms. Boundary recall valid empty (68.409 ms); social recall
one fact (185.0763 ms). Luna medium: 1,533 input / 9 output / 1,542 total tokens;
no regeneration. One local Gemma call plus one Luna call. No memory-writing path.

This is an actual authorized outbound canary, not zero-outbound Shadow. The frozen
graph's existing missing `sent` transition remains: speech was committed, but
transitionApplied=false. No personality, FSM or prompt changes were made. This
proves one-response DM delivery, not continuous multi-turn DM Live.

Native maintenance cleanup reported capture=off, journal=healthy, cleanupFailure=null.
Rollback was then started; final restored-host verification is recorded below when
completed. Private receipt and journals remain preserved.

Final cleanup verified: rollback exited successfully; 10,298 original artifact
hashes restored, configuration/launcher/host byte-exact, OpenClaw 2026.8.1/base
ea806575. Initial readiness requests occurred during restored startup and timed
out; subsequent fresh checks on restored Gateway PID 42024 returned health ok,
ready=true, eventLoop.degraded=false. Authenticated control returned HTTP 200,
capture=off, journal=healthy, activeEpoch=null. The completed maintenance process
was terminated after its final successful disarm report; no listener/window remains.
Frozen project hash remains unchanged. **One-response DM Live canary PASS**;
continuous DM Live and multi-turn sent-state transitions are not enabled/validated.
