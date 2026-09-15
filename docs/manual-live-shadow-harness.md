# Manual Live Shadow Harness — blocked at Graphiti preflight

## Scope and invocation

The explicit development CLI bypasses OpenClaw and the legacy host entirely:

```powershell
dotnet src/Filomena.App/bin/Debug/net10.0/Filomena.App.dll --manual-shadow config/manual-shadow.local.json --preflight-only
# After Graphiti readiness is established, omit --preflight-only for the interactive CLI.
# Enter one message, wait for the result, then enter another; /quit ends the run.
```

Do not run another live probe merely to reproduce this report. The authorized first probe failed and the experiment stopped.

Host configuration is private/ignored and supplies the logical Helena DM, canonical peer, namespace, existing canary references, journal directory and project hash. No transport credentials or recipient come from the graph. The logical self actor is `agent:filomena`: this manual harness makes no claim to validate native WhatsApp PN/LID suppression.

The project is `manual-shadow-project-v1.json`, project ID `manual-shadow-grounding-B-v1`, SHA256 `425FA07CAD30B750BB8BB41573CABA971E96DDFD18DDBEE78B3667E152482301`. It exports the approved H composition and inserts the existing shadow Send sink on DM response paths. An inverse-transformation test verifies that removing those sink adaptations recovers the original project exactly. Prompts, compiler, enforcement, grounding B and FSM rules are unchanged.

Existing Instruction choices are preserved: Gemma for situation/presence; Luna medium for generation, regeneration and the two detectors. No Terra. This is not a silent migration of upstream Instructions to Luna.

## Runtime semantics

`ManualShadowRun` wraps the existing trusted handoff and real session runtime. IDs are `shadow:<runId>:<ordinal>`, explicitly sourced from `manual_shadow_harness`, not provider-native IDs. A single run retains inbound history and normal participation state; restart starts a new run/default state and never resumes old executions. Project contents are pinned in memory.

The CLI waits for normal quiet/max-wait batching and then drains the normal session queue; it does not implement another batching engine. Each console submission waits for completion before the next input. The core wrapper remains independent of WhatsApp; this pilot CLI validates the existing WhatsApp-derived memory namespace.

SendGuard and private candidate lineage are real. The host has no transport adapter, uses unproven transport capabilities and rearms only SHADOW. A shadow result does not append sent history or apply a speech-dependent state transition. Hypothetical transition is trace-only. Memory errors stop the run, never become empty recall. Full candidates are allowed only in the local homologation evidence.

## Actual live result

Evidence: `state/manual-shadow/manual-shadow-9c12439c9e204ac9bff64d2b50ca0f99.jsonl` (private worktree artifact). The preflight-only CLI exited 2 before constructing a model provider or ShadowRun.

| Check | Result | Elapsed from probe start |
|---|---|---|
| Static binding/project validation | PASS | — |
| Graphiti reachable | PASS: `read_succeeded` | 257.76 ms |
| Canonical participant evidence | PASS | 258.78 ms |
| Namespace/returned response contract | FAIL: `invalid_or_cross_namespace_result` | 2219.67 ms |
| Boundary/social/canary/empty-versus-outage proof | Incomplete, failed closed | about 2219.99 ms |

The existing diagnostic combines invalid responses and cross-namespace results. **This is not sufficient evidence to claim confirmed memory leakage.** Nor does it establish that Boundary memory was empty or unavailable. The next recall stage was rejected by the contract; raw data was not preserved for further classification. PRIVATE91/DRIFT42 isolation was not proved and the subsequent Plex probe was not reached. No further live probes were executed.

The preflight used real Graphiti read-only, not fixture results. Existing canary names supplied test targets, not proof that those memories were returned. No Boundary was fabricated.

## Milestone accounting

- Interactions: **0/20**; ShadowRuns/executions: 0.
- WOULD_SEND, silence decisions, regeneration and execution ABORT: all 0. The failure is a **preflight failure**, not a candidate failure.
- Boundary/personal-grounding/context/style quality: not evaluated; no candidates exist.
- Voice, useful memory use and pipeline latency: not measured. Only the preflight latency above is available; its cumulative check timings must not be summed.
- Model calls/tokens/cost: **0 / 0 / US$0**. Graphiti reads occurred; Graphiti writes: 0.
- Transport sends, `messages_send`, WhatsApp sends, legacy agent dispatch caused by the harness: **0**. No Shadow transport listener or OpenClaw deployment occurred.

## Local verification

- Core suite: 491 passed, 10 skipped, 0 failed.
- Selected CLI/project/Graphiti-probe/preflight/multi-detector composition suite: 24 passed, 0 failed.
- New coverage includes same-run multi-turn, normal quiet batching, restart/new run, rejected peer/self, memory outage, strict host configuration and reversible project adaptation. Existing tests cover pinning, cross-session rejection, canary failure, empty/outage, provenance and shadow state/history rules.

No closure commit: the worktree contains substantial earlier, uncommitted experimental work. No push. OpenClaw/Observation diagnosis remains frozen; active installation, personal configuration and DREAMS were not changed.

## Next decision

The harness exists, but **the end-to-end live milestone is not achieved**. First diagnose the rejected Graphiti response/namespace contract in a separately scoped follow-up, preserving evidence and fail-closed semantics. Only after live preflight passes should the 20 interactions evaluate the brain. There is not yet evidence to prioritize transport observation or a Damma slice over this blocker.
