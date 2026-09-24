# Jev operational integration checkpoint

Goal remains activation of Jev in Filomena, not merely a harness experiment.
This checkpoint proves real manual SHADOW through the operational command; WhatsApp
activation/canary is still pending. No active Gateway file has been changed.

## Implemented

- `SystemOneParticipationCompletion` routes only explicitly bound node IDs, never suffixes.
- Situation output is composed from typed Jev fields and the original generative summary.
  Original instructions/inputs, graph, memory wiring, generation and enforcement remain.
- Missing/invalid summaries fail closed. No partial situation or generative classification
  fallback is returned. Non-bound nodes use their original provider unchanged.
- `SystemOneChoiceQuestion` supplies trusted question semantics with exact schema choice
  membership. It cannot add a new output choice. Profiles are copied on construction.
- `SystemOneParticipationHost` binds the pinned project to an explicit, versioned host
  opt-in, exact Jev 1.13.0 and its explicit two-decimal compatibility policy.
- `ManualShadowConfig.systemOneParticipation` defaults to null. ManualShadow and
  NativeShadow/NativeLive share the same composition. No outbound authority is added.
- `docs/systemone-participation-jev-v1.json` preserves focused involvement question C and
  its tested semantic options. It is host configuration, not a project rewrite.
- `tools/prepare-systemone-shadow.mjs` derives a NEW manual-shadow host document under
  an operator-selected private parent, verifies the source project pin and refuses an
  existing destination. It does not modify active files or contain credentials.

The generic completion trace still names the original node's configured model for the
composite situation. Separate `kind=systemone` records identify actual Jev calls/model,
usage, policy and duration; do not attribute the whole composite duration to Gemma alone.

## Tests and verification

New routing/summary and host-binding tests were observed failing before implementation.
Explicit question-profile tests likewise failed before the profile was consumed.
Production composition is exercised through real ManualShadowRun with controlled external
inference/memory fixtures: direct bypass, non-direct presence, regeneration, memory summary
propagation, SHADOW outcome, inbound-only history and no spoken-state transition.

Full solution: **1,167 passed, 49 skipped, zero failed** (595 Core, 455 LLM, 117 OpenClaw).
Release build: zero warnings/errors. `git diff --check` clean. No commit/push.

## Real operational SHADOW

Private configuration/artifacts:
`C:\Users\Helena\.config\openclaw-observation-ops-20260922-live-n\jev-shadow-20260923`.
ACL verified: current user and SYSTEM only. This is a separate manual run, not reuse of
the prior native window or its history/checkpoint. Source project SHA256 remains
`7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.

Graphiti preflight passed twice (preflight-only, then run startup): canonical participant,
namespace, social memory, isolation canaries, exact/semantic provenance and empty/outage
checks. Boundary recall was valid empty, so detectors had no live Boundary coverage.

Run `8b3bac6a47e242b8bde6a6c86a9ca1ef`, report
`journal/manual-shadow-d04ac6cae31d4980bc49ae53e3df53cc.jsonl`:

| Manual input | Result | Total | Jev situation | Composite situation | Luna generation |
|---|---|---:|---:|---:|---:|
| Direct request to organize delivery and meeting | WOULD_SEND | 19.799 s | 1.113 s | 13.754 s | 4.350 s |
| Two failures, one hour total; complaint about system | WOULD_SEND | 11.179 s | 0.347 s | 6.636 s | 2.949 s |

Both classified direct and bypassed presence. Actual generation model: GPT-6 Luna.
Second candidate explicitly preserved two failures and one hour **total**, without
redistributing duration. First candidate asked for missing schedule details rather than
inventing them. No clear material grounding/context error was found in these two outputs.
This sample does not prove non-direct behavior in the full live pipeline.

Jev usage: 7,436 input / 426 output tokens, two calls. Cost at the previously verified
published input rate: approximately $0.000312312, not an invoice. Luna usage is retained
in the private usage records; no price estimate was fabricated. Local summary latency
still dominates participation; no end-to-end speedup is claimed.

The manual process ended normally after two executions. It constructed no transport
sender; both send-node results were shadowed and outboundCalls=0. Graphiti was read-only.
The Jev credential existed only in masked-input process memory/environment and was cleared.

## Next operational action

Prepare a fresh reversible native window using the previously approved provisioning
procedure; do not deploy the unrelated current OpenClaw group-work build blindly.
The last validated native candidate is preserved as `core-dist.experimental` and
`whatsapp-dist.experimental` in private window N. Verify it against N's manifest, plus
current active originals/version/config, before staging. Use the new .NET host composition
and pin/profile above. A new native canary must come from Helena; never synthesize WhatsApp
inbound or replay previously processed messages as new execution.

Native procedure location:
`C:\Users\Helena\.config\superpowers\worktrees\openclaw\native-observation-v1\diagnostics`.
Relevant scripts: `observation-stage.mjs`, `observation-deploy-control.ps1`,
`observation-maintenance.mjs`, `prepare-live-one-response.mjs`,
`verify-observation-rollback.mjs`. Readiness is lifecycle-based; preserve cron pause,
Graphiti startup suppression, exact native LID, isolation proof, capture OFF at startup,
cleanup and rollback. No active native window has been opened at this checkpoint.

## Native window opened (follow-up; awaiting the human canary)

Fresh private root: `C:\Users\Helena\.config\openclaw-observation-ops-20260923-jev`.
The active config had new MCP and compaction sections since N. They were preserved in
the NEW backup and maintenance config, not replaced with N's stale config.
The staging helper now optionally reuses a prior candidate only after exact manifest
verification. N's 12,039 core + 68 WhatsApp candidate files matched. Fresh original
backups contained 10,230 core + 68 WhatsApp files; copied artifacts were hash-verified.
Active version/base: 2026.8.1 / ea806575e6450e4d1efdfc72c19f04be982a1b9b.

The approved deploy procedure completed. Gateway PID 30968 reached health/startup/ready;
initial capture OFF, journal healthy, authenticated status 24.996 ms. The native channel
owner was initially unavailable, then became valid without a restart or elapsed-time
failure verdict. Scheduler disabled for this process; definitions preserved. Isolation
armed against the valid effective snapshot, all ten effective paths excluded, aggregate
INACTIVE. Two legacy recoveries quarantined, zero active/uncovered owners.

Epoch: `30eb3e6c-f308-4ba7-8b7c-6c4f7c4f6b2e`.
The new preparer `tools/prepare-systemone-native-shadow.mjs` read status and created only
one-interaction SHADOW config under `jev-runtime`; it never creates a sender or MCP route.
Runtime transport and Graphiti read-only preflights passed. Snapshot sequence 0, zero
records and zero executions. Run `19a4d68cbfdf4b1bac9ec2c65b31748c` is awaiting manual
inbound; report `jev-runtime/journal/native-shadow-af578b1a9d7d4f079adf03bee9d57f27.jsonl`.

Live tool handles at this checkpoint (revalidate before use): runtime session **42389**;
maintenance session **97680**. Do NOT send input to maintenance until runtime has finished.
After Helena confirms a NEW DM, send exactly `/poll` to runtime. Maximum one execution;
no automatic poller. After result/termination, newline to maintenance triggers its final
read-only poll and disarm; verify cleanup, then execute `observation-deploy-control.ps1
-Mode Rollback -OpsRoot <new root>` and `verify-observation-rollback.mjs <new root>`.
Do not reuse N for rollback. Credentials are transient process variables, not files.

Two additional failing-first tests reject unknown question names and choice sets in host
profiles before requests. Final suite: **1,169 passed / 49 skipped**, build 0 warnings/errors.
No native inbound has been executed at this checkpoint. No WhatsApp outbound occurred.
Goal remains active; activation is NOT yet declared complete.

## Native canary completed

Helena supplied the new real DM inbound. One controlled `/poll` produced exactly one
execution in the run above, sequence 1, fromMe=false. A subsequent maintenance read
returned the same stable native identity; no second runtime execution was opened.
The trusted session and epoch matched. Transport and Graphiti preflights passed;
Boundary recall was valid empty and social recall returned one fact.

Jev 1.13.0 classified direct involvement in 1,066.66 ms (3,657 input / 213 output
tokens). The deterministic direct rule bypassed Decidir Presenca entirely. The
combined Jev/local-summary situation stage took 13,911.90 ms. GPT-6 Luna generation
took 3,551.79 ms; total execution 19,210.27 ms. Luna usage: 2,405 input / 77 output
tokens, including 52 reasoning tokens. No monetary estimate is inferred here.

WOULD_SEND candidate:
> Claro. Quais sao as duas tarefas, e alguma delas tem horario ou prazo fixo?

The original UTF-8 candidate is preserved in the private JSONL. Send-boundary attempts
were zero; no sender was constructed, no spoken-state transition was applied, and
Graphiti was read-only. This proves native WhatsApp SHADOW, not permanent activation
or an outbound Jev deployment.

The one-interaction runtime exited normally. Maintenance disarmed capture and isolation:
capture OFF, journal healthy, cleanupFailure=null. Its idle stdin process was then
terminated after the successful cleanup record. Rollback completed and verification
confirmed all 10,298 original artifact hashes, byte-exact configs/launcher, and
OpenClaw 2026.8.1 at the expected base. Initial health samples during restored startup
timed out; these were not interpreted as proof of startup failure. The native log then
recorded gateway ready and Graphiti startup suppression/not_checked.
Final independent HTTP verification: `/healthz` 200/ok=true, `/readyz` 200/ready=true,
eventLoop.degraded=false. The experimental window is closed; the original launcher
and its scheduler behavior are restored.
