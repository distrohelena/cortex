# Operational DM continuity correction

Helena requested implementation after L's processed inbound chose silence.
The approved frozen original v3 remains unchanged. This is a separately pinned
operational follow-up to care-v3, not a generic runtime rule or an all-DM reply
override.

## Evidence and correction

L: inbound admitted once; Gemma classified indirect involvement; presence chose
`calar / sem_contribuicao`. Generation did not run. That event is completed and
must not be replayed to force an answer.

The DM situation node lacked a conversation-kind input and explicitly classified
continuation of Filomena's recent participation as indirect, using the same
wording as group observation. Presence received kind=dm but only described a
greater expectation of replying. Therefore the direct-response invariant did
not cover bilateral continuation; response depended on the probabilistic branch.

Candidate `manual-live-project-v3-dm-continuity.json`, SHA256:
`62368EC88ED3B7690066BB56BE61A0E59C08348128ACDD3A4EC7DECD170D32A5`.

Only three `dm-situation` nodes change: project-owned constant `tipo_conversa=dm`,
DM-scoped prompt clarification, aligned involvement field description. Bilateral
continuation directed to Filomena is direct without a mention/question. Personal
notes and quoted third-party exchanges are not automatically direct. Group nodes,
presence prompts, FSM, existing direct routing, generators/care guidance, models,
memory, Boundary and send paths remain structurally unchanged.

## Tests

Real session runtime participation cut, actual current local Gemma, no generation,
Graphiti or transport. Eight cases: vent continuation in all three states,
technical continuation, social continuation, group third-party exchange, DM
archived transcript, DM personal note. History is controlled fixture context;
the critical inbound wording reproduces L, not a claim to replay its entire
private history.

Baseline `dm-continuity-probe-6bbbd2dafa4942809fb1bf57ba0c1b58.jsonl`:
0/5 bilateral continuations classified direct; all five happened to respond via
presence in this sample. Thus this reproduces the classification gap, not five
new silences. Three contrasts passed.

Candidate `dm-continuity-probe-aa086114d20b4e1897dcf24c78cb78da.jsonl`:
8/8 passed, all five continuations direct and deterministic responder with zero
presence calls, three contrasts not forced direct. Original artifact test failed
before candidate creation and passes after; real compiler and structural equality
check guard the restricted scope. Full local regression 1,085 passed / 47 skipped;
build clean. Repeated live-local gate separately preserved.

## Operational continuity

Exact care-v3 → DM-continuity checkpoint migration was tested failing first, then
allowed only for the two immutable approved hashes. Existing validation/dedupe
and uncertainty guards unchanged. Provisioning flag
`--one-dm-continuity-response` retains ONE interaction/send budget from creation.
No previous processed event is automatically readmitted.

Fresh private window M uses the established staging/deploy/rollback procedure.
Its rollback owner is M, not L/G. At document creation no active deployment or
new outbound had occurred. Readiness, isolation, Graphiti read-only preflight and
history restore must pass before requesting a new manual DM.

Repeat `dm-continuity-probe-8bf04340c9bc484189976c19d199000b.jsonl` also passed
8/8: combined 10/10 directed bilateral continuations and 6/6 non-direct contrasts.
This is bounded empirical coverage, not elimination of all model variance.
The existing direct invariant applies only after classification; no global DM
auto-response rule or change to `Decidir Presença` was added.

M staging verified native artifacts identical to validated L. Backup and exact
rollback prepared; deployment completed. Gateway PID 38724. Initial maintenance
session 84502 exited before arm; capture OFF/healthy was verified before retry.
Replacement maintenance session 66045 reached CANARY_WINDOW_READY with dispatch
inactive, revision-bound isolation and healthy journal.

Transport and Graphiti read-only preflights passed. Runtime session 96827 is
waiting for one NEW manual DM, with 14 history entries restored and maximum one
interaction. Run `dfb5ade4630d47d38cbcbdee85bb5464`, epoch
`ab35582e-6519-459e-932b-d909a8b56d54`, initial cursor sequence 0. Snapshot produced
zero executions. No automatic polling, generation or outbound occurred during
preparation. Next: user sends a new DM, then one controlled `/poll`; after settled
execution close maintenance and rollback M (never L/G), verifying health/ready.

## Window M controlled result

User confirmed a new DM; one `/poll` processed native sequence 1, fromMe=false.
Exactly one execution completed, with direct involvement and the deterministic
direct rule; no presence-decision call. Situation 7.430 s, Luna generation 9.422 s,
total 19.709 s. Social recall returned one fact; Boundaries returned valid empty.
One send attempt returned `sent/provider_accepted`, with speech committed. Runtime
closed automatically at its one-interaction limit and disabled outbound on close.
Delivery to the user's screen is not yet confirmed. Generation improperly used
the generic label `Pessoa` as a vocative; recorded as a quality issue, not fixed
in this bounded participation test. Full candidate remains in private evidence.
Maintenance confirmed capture OFF, journal healthy and cleanupFailure=null.
Rollback M was started; final restoration verification follows separately.

Rollback M completed: 10,298 original artifact hashes verified, original configs
byte-exact, OpenClaw 2026.8.1 / expected ea806575 base verified. Initial probes
during restart timed out; subsequent read-only checks returned healthz=200/ok,
readyz=200/ready=true, eventLoop.degraded=false. Capture had been disarmed before
rollback. No second poll into the conversational runtime and no resend occurred.
