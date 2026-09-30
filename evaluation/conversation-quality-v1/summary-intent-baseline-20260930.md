# Current summary intent: prepared baseline evaluation

## Status

Offline preparation passed for six contrasts. A separately authorized batch of
12 real Luna summary calls completed, without retries or online changes.
The original 32-call allowance was not reused; the user approved this new batch.

## Baseline and composition

Project SHA-256:
`0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
Compile the actual `group-situation` node from the Engaged state, using its real
InstructionContext composition. No prompt, model, budget or project mutation.
Current assignment: OpenAI `gpt-6-luna`, medium, maximum output tokens 4096.

Use the deployed `SystemOneParticipationCompletion` with in-memory typed and
summary implementations during preparation. The real wrapper strips `resumo`
from typed schema and passes only the summary schema to the summary completion.
Assertions verify one call to each stub, exact original prompt/input preservation,
summary-only schema and unchanged model settings. No HTTP handler is constructed.
The future paid probe uses only the captured summary request, not Jev or a full
conversational execution.

## Six contrasts and review criteria

| Case | Required meaning |
| --- | --- |
| Direct repetition correction with quote | A participant points out Filomena's repetition; not just continuing humor |
| Explicit self-reference clarification | The pronoun refers to Filomena; do not introduce another woman |
| “vim enxer” continuation | Preserve playful wording or quote it, not “came to see” |
| Operator update | Leaving Filomena online while adjustments occur, not a conversational invitation |
| Invited coffee callback | Explicit request to continue the joke, without invented complaint |
| New practical contribution | Help reconstructing decisions, explicitly without the joke |

Inputs reuse the prior window's runtime-projected minimized development fixtures.
Their partial/adapted histories are not exact live request replays. Situation
labels from the previous generator setup are not supplied to this summary probe:
only `mensagens` and `historico`, as the compiled node requires.

Planned repetitions: two per case, 12 total, current baseline only. No candidate
prompt yet, no automatic retries, no model ranking. Review structural validity,
20-word limit, speaker/recipient fidelity, negation/contrast and communicative
meaning separately. A quotation can preserve ambiguity without speculative intent.
Do not judge whether to respond from summary-only outputs.

## Reproduction and evidence

Local disposable harness:
`C:\Users\Helena\filomena-self-reference-tests\summary-intent-20260930`.
Executed `dotnet run --project Prepare.csproj`: exit 0, six composition checks PASS.
`prepared.json` retains all 12 exact requests and criteria. Preparation has
network calls 0, model calls 0, transport calls 0, Graphiti calls 0.

No online change or deployment. The earlier manual-summary contrast remains a
hypothesis generator, not evidence that the current Luna summarizer is broken.

## Real-provider results

| Case | Repeat 1 | Repeat 2 | Author review |
| --- | --- | --- | --- |
| Repetition correction | Participante comenta que Filomena fez a mesma piada duas vezes. | Participante comenta que Filomena fez a mesma piada duas vezes no grupo. | Correction content retained; not reframed as ongoing humor |
| Self-reference | Participante confirma a Filomena que “ela” se referia à própria Filomena. | Participante confirma a Filomena que “ela” se refere a Filomena. | Explicit self-reference retained |
| Playful continuation | Participante responde a Filomena: “nada, só vim enxer mesmo”. | Participante responde a Filomena que não foi nada e que só veio “enxer” mesmo. | Wording preserved rather than changed into “came to see” |
| Operator update | Participante diz que vai deixá-la ligada, mas Astra está trabalhando em alguns detalhes. | Participante diz que vai deixar 'ela' ligada, mas Astra está trabalhando em alguns detalhes. | Update and ongoing work preserved; no invented invitation |
| Invited callback | Participante pede que Filomena continue falando sobre a consultoria de café e diz que quer ouvir. | Participante pede a Filomena que continue a história da consultoria de café e diz que quer ouvir. | Explicit request retained; no invented complaint |
| Practical help | Participante pergunta a Filomena como reconstruir as decisões da reunião, pedindo resposta sem a piada. | Participante pede a Filomena, sem a piada, orientação sobre como reconstruir as decisões da reunião. | Help request and no-joke contrast retained |

12/12 valid summary-only objects, 12/12 within the 20-word limit; observed range
9–17 words. No material intent/factual error identified by author review against
the predeclared criteria. This is not an independent or blinded review.

The operator fixture has no preceding history resolving its pronoun explicitly;
preserving that pronoun is not scored as inventing another person. Conversely,
the explicit self-reference fixture resolves the pronoun and both outputs name
Filomena correctly. Quoting “enxer” is permitted by the current prompt and keeps
the source wording available downstream without claiming a hidden intent.

This is a small, known development corpus. It does not prove general summary
reliability, participation quality, live-history completeness or generator voice.
No Jev inference, generation, Graphiti, enforcement or sender was exercised.

## Measurements and artifacts

- Calls: 12; failures: 0; retries: 0.
- Reported input tokens: 12,094; output tokens: 2,087 (may include reasoning).
- Mean latency: 2.968 seconds per summary request, not end-to-end conversation.
- Monetary cost: unknown; no verified tariff applied.
- `results-12.jsonl` in the local harness retains requests, results, usage,
  word counts, timings, baseline hash and preparation hash.
- Result SHA-256: `0DEE22C03B619462C71AA4FAF9442DFF3AB1E4D7F13880DE055B574296DD3525`.
- Paid runner is separate under `paid/`, allows only the Responses endpoint,
  caps requests at 12, disables redirects and creates a new output file.
- Baseline hash checked before and after execution; unchanged.

## Decision

**Keep the current summarizer unchanged.** The old “veio ver” failure did not
recur, and the correction/identity contrasts passed. Do not add another summary
clause merely because manually improved summaries helped an earlier experiment.

The remaining observed voice problem belongs to conditional generation in the
current evidence: self-explanation, polished acknowledgments and manager-like
phrases despite adequate context. A future bounded investigation should target
that output behavior, keeping these summary regressions as controls, rather than
retuning the summarizer or expanding history without evidence.
