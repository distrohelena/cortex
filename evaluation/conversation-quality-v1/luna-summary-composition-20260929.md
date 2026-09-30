# Luna summary: isolated composition acceptance

**Result: existing composition validated; candidate not activated online.** No new router or production-code change was required.

## Minimal candidate

[group-luna-summary-v1.project.json](group-luna-summary-v1.project.json) changes only `provider`, `model`, `reasoningEffort`, and `maxOutputTokens` on `group-situation` in the three participation states: OpenAI / `gpt-6-luna` / medium / 4096. A structural comparison restored those fields and proved the remaining project identical to the active source. Twelve field changes in total. DM nodes, prompts, participation guidance, generation, FSM and enforcement are unchanged.

Source hash: `77ECDAABBF48A957516AD18371C32377495843D499F747425AC47A2C414F8FA8`.

Candidate hash: `0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.

`SystemOneParticipationHost.Create` already composes `SystemOneParticipationCompletion` with the provider dispatcher. Typed situation fields are routed to Jev; only the reduced `resumo` schema goes to the node's selected summary provider. `ProviderFlowCompletion` already supports the OpenAI adapter in the operational composition. This was characterized rather than reimplemented.

## Offline verification

Added 13 tests in `tests/Filomena.Llm.Tests/LunaSummaryCompositionTests.cs`:

- Real host composition and provider adapters; only external HTTP is replaced.
- Jev receives typed questions without the summary field.
- Luna receives only summary output schema, unchanged prompt/input, medium/4096, `store=false`.
- Null, absent, blank, extra-field and duplicate-field summaries fail without emitting a partial situation.
- Typed-provider and summary-provider outages propagate without a local fallback; a typed outage does not call Luna.
- Unrelated local node keeps its original route.
- Real session runtime retains direct bypass in Inactive, Attentive and Engaged; non-direct still calls typed presence. One execution per input, no outbound node.

Final complete Llm suite: **834 passed, 49 skipped, 0 failed**, 883 total. Opt-in environment variables and provider credentials were cleared only in the test process. Live tests remained skipped. TRX: `C:\Users\Helena\filomena-self-reference-tests\composition-results\luna-summary-offline-final.trx`.

These are characterization tests of an already supported capability, not a claim that new production logic followed a red/green cycle. Existing unrelated working-tree changes were preserved. The full solution and production operation were not validated by this suite.

## Real-provider smoke

Six controlled cases through the real compiled `SystemOneParticipationHost` and adapters from the active release, with the candidate pin. Each produced exactly one Jev request followed by one Luna summary request: **6/6 complete merged situations**, all 12 HTTP responses 200.

| Case | Jev involvement | Luna summary observation |
|---|---|---|
| Original `enxer` | direct | Preserved `enxer`, no invented purpose |
| Correct spelling `encher` | indirect | Preserved the original expression |
| Addressed to Gabriel | none | Preserved Gabriel as recipient |
| Neutral presence check | indirect | Preserved neutral meaning |
| Closure | indirect | Preserved farewell |
| Explicit playful approach | direct | Preserved playful approach |

No material summary error observed in this six-case smoke. **Jev still classifies the corrected-spelling continuation as indirect.** The summary change does not solve that separate classification issue or establish a final respond/silence outcome.

The real-provider smoke did not run a conversational session, memory, generation, presence, or sender. Runtime/FSM behavior was exercised offline with controlled HTTP, not with a real WhatsApp conversation.

## Usage and effects

Mean merged-stage latency: **2.68 s**. Jev: 6 calls, 27,495 input / 1,279 output tokens. Luna: 6 calls, 5,412 input / 827 output tokens. Monetary cost remains unknown; no verified tariff was applied. All counts are provider-reported.

Production changes 0; Graphiti calls 0; transport calls 0; online restarts 0. The active source hash remained unchanged. No commit or push.

Evidence: [structured results, field diff, usage and hashes](luna-summary-composition-20260929.json). Private raw evidence: `C:\Users\Helena\filomena-self-reference-tests\playful-evidence-20260928\luna-composition-smoke.jsonl`.

## Handoff

The summary-only candidate is ready for a separately approved controlled activation. It must be pinned through the normal operational revision mechanism, not copied over the active project silently. No activation was performed in this round. Preserve the separate backlog for typed involvement consistency and do not compensate by forcing participation.
