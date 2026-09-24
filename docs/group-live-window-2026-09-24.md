# Exact-group live window, 2026-09-24

Selected group only: Alunos de Drift do Fiuk. No brain/model changes.

## Verified results

- Native canary: one record, trusted participant, fromMe=false, native ID and replay stable; exact group/account and isolation revision checked. No send.
- Transport preflight and all ten Graphiti probe sets passed before runtime start. Boundary recall was valid empty; runtime social recall returned two items.
- Real message “Filomena, chegou por aqui?” was admitted once and classified direct. The direct rule bypassed Decidir Presença.
- Jev 1.13.0: 786.9 ms. Situation including local Gemma summary: 81.0 s. GPT-6 Luna generation: 6.27 s. Total: 90.23 s.
- Candidate: “Por aqui no grupo, sim. Se você quis dizer outro lugar, me diz qual.”
- No confirmed delivery. The receiver producer revision changed during processing. The pre-send check stopped before the actual transport boundary (counter=0). The outbox conservatively recorded indeterminate.

## Reconciliation and local fixes

The existing operator reconciliation API appended not_sent with exact journal hash and named audit evidence proving zero inner send calls. It retained original entries and a private backup. No resend or fabricated receipt.

The launcher now maps DPAPI-protected Jev credentials to the adapter's actual TYPESAFE_API_KEY. Windows PowerShell redirected stdin initially emitted a BOM; parent Console.InputEncoding is now UTF-8 without BOM, verified with a byte-level child-process test. No duplicate executions were involved in the earlier unused-runtime restarts.

Offline context import exposed two DM-era assumptions:

1. Healthy empty polls were rejected as unsupported execution batches. A regression failed before correction; empty polls are accepted only with zero outcomes, trace and decisions.
2. Runtime context restoration hardcoded DM. A group checkpoint regression failed before correction; restoration now requires the trusted host's expected kind and checks every history item against it. DM remains the default.

Import utility reuses NativeObservationHostBinding for exact group identity/participants. The reconciled group context contains one inbound, zero spoken replies, unchanged Inativa state and preserved dedupe. Sources were not overwritten; unsuccessful import targets remain private for diagnosis.

Fresh solution regression: Core 601 passed/10 skipped, adapter 128 passed, LLM 489 passed/40 skipped. Import tool build: zero warnings/errors. These tests do not prove real group delivery.

## Operational state

Window 1 was disarmed after the stale receiver proof. Window 2 was armed against a fresh gate/epoch but no runtime started: tool approval rejected the real-send launch and required explicit current authorization. Window 2 was then disarmed, journal healthy, cleanupFailure=null. Rollback was requested; final health/hash verification is recorded separately in the private operation root.

Private artifacts: `.config/openclaw-observation-ops-20260924-group` (operator/SYSTEM ACL). Reconciled context: `group-runtime-reconciled-03`. No secrets or raw participant IDs in this report.

Group live delivery remains unproven. Next action requires authorization to reopen a five-message real-send window; do not replay the prior attempted response. No permanent group service is claimed.

Final rollback verified: 10,298 artifact hashes restored; configuration byte-exact; OpenClaw 2026.8.1/base ea806575; health HTTP 200; ready HTTP 200/true; event loop not degraded; capture OFF; journal healthy.

## Second deployment and switch to Grupo Teste

The separately authorized second five-message window started, but the reader exited
with `unavailable` before admitting the new message. The native journal retained one
new peer event. No runtime execution or transport send was attempted for that event.
The exact HTTP failure cause is not established; it must not be called an empty poll.
Capture and isolation were disarmed with healthy journal and no cleanup failure.
That closed Drift epoch remains private and must not be replayed into another group.

Closed-epoch recovery now accepts the explicitly expected observation V2 contract
without weakening epoch/session/tail validation. Fresh solution results: 1,219 passed,
50 skipped, zero failed. This is local evidence, not proof of group delivery.

The operator selected Grupo Teste for the next small tests. Native metadata uniquely
resolves its group/account/route (masked session ref `29f69c6894ee`). A separate private
host configuration permits only the already trusted operator PN/LID aliases. No
Drift conversation state, facts, or Graphiti namespace is copied. Graphiti readiness
for Grupo Teste is unproven; the initial window is transport-only.

Before switching, current native status confirmed capture OFF, healthy readable
journal, and no armed isolation gate. A process replacement was observed without
an agent-issued restart; the existing readiness gate subsequently confirmed the new
process ready. Missing HTTP responses during its startup were not treated as a
terminal process failure or used as justification for a restart.

The target-only switch uses the existing installed experimental binaries and the
existing full rollback root `openclaw-observation-ops-20260924-group02`. It preserves
the previous host configuration separately; scheduler and Graphiti startup probes
remain suppressed. No canary should be requested until exact-group isolation and
capture are verified against the newly loaded configuration.

## Grupo Teste transport roundtrip

The manual canary was observed exactly once with trusted operator actor, fromMe=false,
stable native ID on replay, sequence advancement, empty tail after the new cursor,
stable effective revision and legacy dispatch inactive. The receive window was closed
with capture OFF, healthy journal and no cleanup failure.

A separate operator-authorized transport-only window submitted one fixed diagnostic
text through the existing `messages_send_bound` group contract. Exact account/group
route was checked before submission; a private fsync'd operational attempt record
was created before the sole call, with no automatic retry. The native response
matched contract, attempt, account, recipient and provider message ID. Its journal
echo matched that native ID, fromMe=true and actorRef=selfIdentityRef.

This is NOT a Filomena brain/SendGuard execution: no generated candidate, Graphiti,
models or conversational runtime ran. It proves native bound group transport only.
One provider-accepted submission; human-visible delivery confirmation still pending.
The reply window also ended capture OFF, journal healthy, cleanupFailure=null.
Private evidence is under `group02/test-group` and `group02/test-group-reply`.

Helena confirmed the diagnostic reply arrived in Grupo Teste. Native transport
roundtrip is therefore confirmed by both provider receipt and recipient observation.

## Drift recovery continuation

Restored only the previously approved Drift host binding; brain, binaries and Test
group history were not changed/copied. Recovery is explicitly restricted to the one
native event in the earlier closed epoch that never reached the runtime. It does not
retry the reconciled prior send attempt. Fresh local replay tests: 12/12 passed.

The first new preflight failed at semantic recall (`backend_read_error`) before any
model generation or send. Docker logs showed a connection error; LM Studio had no
running service or loaded models. The window was safely disarmed. Restored the local
server and the existing Nomic-v2 embedding/Gemma-4-e4b (131072 context) assignments.
The Graphiti container could then reach its embedding backend with HTTP 200.

A fresh revision-bound window (`drift-resume-02`) passed transport and all ten Graphiti
probe sets and started the real runtime with finite native recovery. Execution/send
outcome remains to be recorded; starting a run is not proof of successful delivery.

Recovery execution completed successfully: one native event admitted, one execution,
one send-boundary attempt, outcome SENT, confirmed-speech transition applied. Generated
reply: "Tô aqui. O que foi?" Total 12.19 s; Jev 0.783 s; situation/local summary stage
5.41 s; GPT-6 Luna generation 2.69 s (1,576 input / 12 output tokens). No generation
or participation settings changed. This was the real group runtime and SendGuard,
unlike the earlier fixed-text transport smoke. Human-visible confirmation is pending.
Run closed normally; capture/isolation cleanup completed, capture OFF and journal
healthy. Further group interactions are not yet running continuously.

Helena confirmed the generated Drift reply arrived and participants responded.
One subsequent target-group admission occurred after capture was closed; native
queue metadata confirms its payload was already tombstoned, so it cannot be
represented as an observed/replayable journal event. No synthetic recovery was made.

The saved runtime checkpoint contains three history entries and Engajada state,
including the confirmed reply. Prepared the remaining four-interaction window on
that same checkpoint, without reexecuting either prior input. Added an exact bounded
CLI authorization argument so the remaining allowance is four, not another five.
Two acceptance tests failed before the fix; full solution afterward: 1,226 passed,
50 skipped, zero failed. Budget mismatch, invalid count and malformed flag still
reject before sender launch. No brain/model setting changed.
