# Jev: explicit compatibility and frozen-question holdout

Isolated checkout only. No live Filomena deployment or model reassignment, Graphiti,
WhatsApp, conversational generation, or outbound. No memory writes. No API credential
persisted in files; masked stdin and a short-lived process environment were used.

## New contract evidence and bounded fix

The [official Python SDK response contract](https://docs.typesafe.ai/sdk/python/api/types/responses)
explicitly describes Choice probabilities as summing to **approximately** 1. This qualifies
the API overview's stricter wording used in the prior report. It does not specify an exact
decimal precision or tolerance. Captured Jev 1.13.0 responses showed two-decimal probabilities,
including the preserved distribution summing to 0.99.

Implemented a **local, explicit compatibility policy**, not a claimed provider guarantee:

- Default remains `LayaFourDecimals`; previous rejection and diagnostic remain test-covered.
- Host opt-in: `FILOMENA_JEV_PROBABILITY_POLICY=jev-1.13-two-decimal-v1` for the standalone
  command or experiment. The typed client exposes `SystemOneProbabilityPolicy.Jev113TwoDecimals`.
- Compatible policy requires the returned model to be exactly `jev-1.13.0`; another version fails.
- Existing near-unit mass validation remains. Outside that budget, compatibility accepts only
  two-decimal values whose clipped half-cent rounding intervals can contain unit total mass,
  with an additional absolute mass deviation ceiling of 0.02. Epsilon is only 1e-9.
- The 0.02 ceiling is our conservative policy, not a published TypeSafe constant. Large option
  sets cannot accumulate an unbounded rounding allowance. Grossly wrong mass still fails.
- Probabilities and confidence are returned unchanged. No renormalization, retry-until-valid,
  decision fabrication, default action, or fallback model.
- Output/trace names the selected policy. Unknown host policy fails before HTTP.
- Laya does not inherit the Jev profile.

Added the missing documented invariant that `choice` must be a highest-probability option
(ties are allowed). A wrong lower-probability selection now fails under both policies.
It was reproduced with a failing test before the fix. Existing range, exact-key, schema,
type, confidence and usage rejections were retested under both profiles.

The exact earlier 0.99 response is now replayed offline in both modes: default rejects;
explicit Jev mode accepts without changing any answer field. The first uncaptured rejection
from the original pilot still cannot be conclusively attributed.

## Holdout protocol

Kept question C and its option descriptions unchanged from the preceding follow-up.
Added twelve new synthetic scenarios before the first API call, with expectations fixed in
`SystemOneHoldoutCorpus.cs`. Three repeats per scenario; both participation stages use Jev.
Requested the versioned model `jev-1.13.0`, not an alias. All sixty responses matched it.

Runtime/compiler/batching/FSM and the direct-involvement rule are real. These tests do not
generate replies and do not validate memory recall. The projection still omits unsupported
free-text `resumo`; it is **not** proof of drop-in whole-brain replacement.

| Scenario | Involvement, 3/3 | Action, 3/3 |
|---|---|---|
| New direct technical group question | direto | responder |
| Direct care request | direto | responder |
| Direct social challenge | direto | responder |
| Technical request to Bia | nenhum | calar |
| Quoted old call to Filomena | nenhum | calar |
| Mention without invitation | indireto | calar |
| Implicit continuation of Filomena's suggestion | indireto | responder |
| Explicit migration to an exchange between others | nenhum | calar; receded to Atenta |
| Conflict between third parties | nenhum | calar |
| Banal DM, exploratory action | nenhum | calar |
| Incomplete thought explicitly addressed to Filomena | direto | responder, preserving V1 rule |
| Technical word in routine exchange | nenhum | calar |

**36/36 recorded acceptance checks passed**: 33 strict participation checks plus three
exploratory DM-action samples with a strict non-direct involvement expectation.
Twelve direct executions bypassed presence entirely. Fifteen selected responder and 21 calar.
Every execution occurred once; no outbound node was visited. No contract errors or retries.
No distribution in this new live sample needed the extra rounding allowance; compatibility
acceptance is demonstrated by the separately captured 0.99 replay, not claimed from this sample.

This is a small author-written development holdout, not a blind independent benchmark.
Repeated identical inputs may be correlated. No tuning took place after viewing these results.

## Latency, usage and estimated price

- Direct path: situation mean **328.21 ms**, zero presence calls.
- Non-direct: situation **318.91 ms**, presence **326.02 ms**, combined **644.93 ms**.
- Overall combined stage mean: **539.36 ms**. This excludes a real-time inbound waiting window,
  memory, generation, enforcement and transport; it is not whole-Filomena response latency.
- 60 Jev requests: **181,270 input / 12,923 output tokens**.
- [Official model pricing](https://docs.typesafe.ai/models), checked 2026-09-23:
  $0.042 per million input tokens; output tokens free.
- Estimated API cost: **$0.00761334**. This is a published-rate estimate, not an invoice or balance audit.

Evidence: `TestResults/systemone/20260923-163633-ee70435604494420bf5dbf63ee5cd64e/`.
All sixty captured responses also passed an offline replay through the final client, including
the new choice/max validation; answers were deep-equal to the original wire answers.

## Reproduction

For the already-built isolated test suite, provide the credential privately, then set:

```powershell
$env:FILOMENA_SYSTEMONE_EXPERIMENT='1'
$env:FILOMENA_SYSTEMONE_PROVIDER='jev'
$env:FILOMENA_SYSTEMONE_MODEL='jev-1.13.0'
$env:FILOMENA_SYSTEMONE_VARIANTS='both'
$env:FILOMENA_SYSTEMONE_CORPUS='holdout'
$env:FILOMENA_SYSTEMONE_REPETITIONS='3'
$env:FILOMENA_SYSTEMONE_SEMANTIC_OPTIONS='1'
$env:FILOMENA_SYSTEMONE_FOCUSED_INVOLVEMENT='1'
$env:FILOMENA_JEV_PROBABILITY_POLICY='jev-1.13-two-decimal-v1'
dotnet test tests/Filomena.Llm.Tests -c Release --filter FullyQualifiedName~SystemOneParticipationLiveTests --no-build --no-restore
```

Offline wire replay uses `FILOMENA_SYSTEMONE_REPLAY_DIR` and the
`SystemOneWireReplayTests` filter; its HTTP handler is a local fixture and cannot call a service.

## Verification and deployment boundary

- Red/green tests proved new compatibility and rejection cases, and the missing choice/max invariant.
- Focused tests: 41 passed, two opt-in tests skipped.
- Full solution: **1,141 passed, 49 skipped, zero failed** (595 Core, 429 LLM, 117 OpenClaw).
- Release build: zero warnings/errors.
- Original source artifact SHA256 unchanged:
  `7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.

Jev is promising for the next **isolated integrated** experiment. It is not deployed live.
The focused questions remain experimental test configuration; the live graph, memory-query
use of summary, existing participation assignments and response generator remain unchanged.
Laya was not restarted or reevaluated in this turn; its previous limitations remain documented.

Follow-up: [summary-preserving integration](jev-summary-integration-2026-09-23.md).
