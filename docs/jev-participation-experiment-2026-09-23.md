# Jev live participation pilot

Follow-up: [focused involvement comparison and captured contract rejection](jev-involvement-followup-2026-09-23.md).
The results below preserve the original pilot without replacing its failed run.

Executed only in the isolated checkout against the synthetic participation corpus.
No active deployment, WhatsApp, Graphiti, response generation or outbound.

## Credential and model

The supplied credential was entered through masked process stdin, assigned only to the
test process environment, and removed in `finally`. It was not written to source, reports,
configuration or command-line arguments. No authorization headers were recorded.

Endpoint: `https://api.typesafe.ai/v1/systemone` with redirects disabled.
Requested `jev-latest`; observed response model `jev-1.13.0`.
The current [official API contract](https://docs.typesafe.ai/api) was checked before the run.

Source artifact SHA256 remains
`7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.
Live project and model assignments were not modified.

## Completed quality samples

One repetition per fixture; these are bounded pilot outcomes, not statistical accuracy estimates.
The earlier baseline was reused, not rerun. Synthetic input, runtime, FSM and direct rule were identical;
System 1 projections explicitly omit the unsupported free-text summary.

| Variant | Accepted outcomes | Mean situation | Mean presence* | Mean combined |
|---|---:|---:|---:|---:|
| Earlier Gemma baseline | 9/9 | 6.071 s | 2.371 s | 8.443 s |
| Jev situation + Gemma presence | 8/9 | 0.359 s | 1.991 s | 2.350 s |
| Jev both stages, completed repeat | 8/9 | 0.337 s | 0.150 s | 0.487 s |

*Includes zero when direct involvement bypassed presence. Different execution times and hardware;
this is observed end-to-end stage latency, not a controlled hardware benchmark.

In both completed samples, the failed fixture was #7: Gabriel and Bia discussing C++/RAII
without inviting Filomena. Jev classified involvement as `direto`, so the unchanged direct rule
correctly forced a response given that incorrect input. This is a classification failure, not a
failure of the direct rule. The three intended direct calls passed in every observed run.

## Contract failure and diagnostic follow-up

The first run completed all nine situation-only fixtures, then stopped at both-stage fixture #5
(migration) with `Invalid System 1 decision contract`. The situation result was valid and the
presence result was rejected before publication. No fallback, default response or silent repair occurred.

That rejected payload was not captured by the original harness, so the exact failed validation
cannot be reconstructed. It is **not** classified as a proven adapter bug or a proven backend defect.

Added test-only wire evidence recording (JSON payloads for this synthetic corpus, never headers)
and optional `FILOMENA_SYSTEMONE_CASE` selection. A targeted reproduction of fixture #5 succeeded,
then a complete nine-fixture both-stage repeat also succeeded at the contract/runtime level.
Validation was not relaxed, and the failed original run remains preserved.

Observed later Jev probability values use two decimal places; the original client uses a tighter
Laya-derived aggregate rounding allowance. A precision mismatch is a hypothesis, not an established
cause of the uncaptured rejection. A future rejection should be diagnosed from its preserved wire
payload before changing any tolerance.

The integration-test green result means execution/contract integrity passed; per-fixture quality
is recorded separately as `pass`. The completed repeat's 8/9 score does not erase the earlier rejection.

## Usage and evidence

- Situation-only completed block: 9 Jev calls; 30,043 input / 1,921 output tokens.
- Both-stage completed repeat: 13 Jev calls; 43,661 input / 2,793 output tokens.
- Initial partial both block: 6 validated Jev calls; 21,420 input / 1,285 output tokens,
  plus one rejected response whose usage was not retained.
- Targeted diagnostic: 2 calls; 6,070 input / 430 output tokens.

Exact retained total: **101,194 input / 6,429 output tokens across 30 validated Jev calls**.
One additional rejected response makes 31 observed request attempts. Monetary billing was not
retrieved; no price or zero-cost claim is made for hosted Jev. Local Gemma usage is not included.

Evidence directories under `TestResults/systemone/`:

- `20260923-154756-6e7504db0f2142008aa2af4cf7284b8b`: first run, including failed migration.
- `20260923-155023-0fc7be8bcf834aa7b8511deaf2f5c618`: targeted migration reproduction.
- `20260923-155129-8d50e55a85c44a32af2f6cdc9ce8aff7`: complete both-stage repeat.

Verification after diagnostic harness change: build succeeded with zero warnings/errors;
28 offline System 1 tests passed, one live test skipped when opt-in was absent.
No production implementation or validation changed in this follow-up.

## Decision

Jev is substantially more promising than the first Laya mapping on this corpus, but is not yet
approved as a live replacement: the false direct involvement and uncaptured transient contract
rejection remain explicit limitations. Keep the current brain unchanged. Next useful work is a
bounded involvement-specific question comparison with retained wire evidence, not a live deployment.
