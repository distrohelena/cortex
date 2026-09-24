# Jev participation with the original summary boundary preserved

Isolated experiment only. Live Filomena and its project/model assignments were not changed.
No Graphiti calls or writes, WhatsApp calls, transport outbound, or conversational Luna calls.
Laya was not restarted or retested.

## Composition

The test-only `SystemOneSummaryComposition` uses Jev 1.13.0 for typed situation fields and
presence decisions. It uses the situation node's original generative provider/model
(`lmstudio`, `google/gemma-4-e4b`) for `resumo`, which Jev cannot supply as free text.

The original situation prompt and inputs remain unchanged. Only the output schemas are
partitioned: typed fields go to Jev; the original summary definition goes to Gemma. Calls
are sequential. The complete situation is emitted only after both results validate.
Missing, null, blank, duplicate or extra summary fields fail closed; typed failure cannot
fall back to generative classification. Downstream generation and enforcement retain their
original baseline completion route. The direct involvement rule still bypasses presence.

The full graph is not rewritten or pruned in composition tests. Its summary reaches the
memory query and the actual `resumo_situacao` request fields of presence, generation and
regeneration. A graph port ID and the request field name are not interchangeable.

## Offline full-flow proof

Eight tests cover direct/non-direct routing, regeneration, invalid summaries and invalid
typed responses. Full-flow tests use the real runtime, compiler, FSM, Boundary compiler,
enforcement aggregation and SHADOW SendGuard. MemoryItems and inference responses in
these tests are explicitly controlled fixtures, not live Graphiti or Luna evidence.

Assertions cover summary propagation, original graph equality, direct bypass, failed
composition stopping the flow, SHADOW-only outcome, inbound-only history and no actual
spoken-state transition. Tests were first observed failing before implementation.

## Five real participation probes

These use real Jev plus local Gemma, but stop before memory/generation/outbound. This is
not a real full-brain run. One sample per scenario; no tuning after observing the sample.

| Scenario | Action | Combined participation latency |
|---|---|---:|
| Direct help organizing the day | responder | 8.430 s |
| Continuation about a proposed queue | responder | 5.813 s |
| Migration from code to films | calar | 7.097 s |
| C++ discussion between others | calar | 5.299 s |
| Two failures, two hours total | responder | 4.826 s |

All five passed their acceptance checks. In the multiplicity case Gemma summarized:

> Helena reclama a Filomena que o sistema apagou seu relatório duas vezes e pede como evitar perder trabalho.

It omitted duration rather than inventing a per-occurrence duration. The original inbound
remains available in the graph context. This does not prove summaries preserve every fact.

Evidence: `TestResults/systemone/20260923-192224-6af49ec1b2fb4453bf1ff5bb092748f2/`.
The harness opt-in is `FILOMENA_SYSTEMONE_PRESERVE_SUMMARY=1` with variant `both`.

## Latency and cost

- Combined situation/presence mean: **6.293 s**.
- Gemma summary call mean: **5.721 s**; approximately 91% of the combined mean.
- Eight Jev calls: **24,622 input / 1,723 output tokens**.
- Five local Gemma calls; token usage and local compute cost were not instrumented.
- Estimated Jev cost: **$0.001034124**, using the published rate recorded in the preceding
  report ($0.042/M input, output free), not a billing audit.
- No contract retries or rejected responses in this sample.

The preceding 0.539 s result excluded summary. It is not an integrated latency claim.
This small sample does not establish a full-pipeline speedup or a controlled warm/cold
comparison. No optimization or deployment follows from these numbers.

## Verification

- Full solution: **1,149 passed, 49 skipped, zero failed**.
  Core: 595 passed/10 skipped; LLM: 437/39; OpenClaw: 117/0.
- Release build: zero warnings/errors.
- `git diff --check`: clean.
- Source project SHA256 remains
  `7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.
- No credential written to source/artifacts; no commit or push.

Result: experimental composition is viable and context plumbing is covered locally.
Live full-pipeline quality/latency is still unproven. The summary stage remains generative;
Jev is not a drop-in replacement for the whole situation node.
