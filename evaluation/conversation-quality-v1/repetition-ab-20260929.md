# Repetition-feedback precedence: isolated A/B

## Decision

**Do not promote B.** Explicit feedback precedence produced partial improvement,
but clear repeated-premise outputs remain. No online prompt, project, model,
memory, transport or supervisor change was made by this experiment.

## Design and validation

Active project: `0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
Same deployed generator composition and OpenAI adapter as the preceding baseline:
`gpt-6-luna`, medium, adapter default 1024 output tokens. No new detector.

- A: unchanged composed prompt.
- B: A plus the clause below.
- Four existing repetition cases, three repetitions per arm: 24 calls.
- Three synthetic contrasts, two repetitions per arm: 12 calls.
- Pair order alternates A/B and B/A by repetition. All 18 pairs were checked:
  identical inputs, output schema, provider/model, effort and output budget;
  B retains the complete baseline system text and adds the clause.
- All 36 calls completed with nonempty JSON text and a supplied message reply
  reference or null. No retries, regeneration or runtime participation execution.
- The harness and synthetic context remain development tools, not production
  components or a holdout. Situation labels and summary are fixture setup.

## Exact B clause

```text
FEEDBACK SOBRE REPETIÇÃO

Quando alguém já apontou no histórico que você está repetindo uma piada, o fato de outra pessoa continuar falando do mesmo assunto não anula esse feedback nem é, sozinho, convite para você repetir a mesma graça. Trocar palavras ou acrescentar um pequeno detalhe à mesma comparação não resolve a repetição. Responda ao que houver de novo; ao reconhecer a correção, não acrescente outra versão da piada como acabamento.

Isso não proíbe callbacks. Sem esse feedback, uma retomada pertinente pode funcionar; mesmo depois dele, um pedido explícito para explicar ou revisitar a brincadeira pode ser atendido. Distingua esse pedido de uma simples continuação do assunto. Preserve humor e personalidade quando houver contribuição realmente diferente, sem inventar que alguém reclamou quando isso não consta do contexto.
```

## Contextual review of repetition cases

These are agent judgments, not independent review or automated ground truth.
Borderline reuse is kept separate from a clear repeated punchline.

| Case | A runs 1/2/3 | B runs 1/2/3 |
| --- | --- | --- |
| 01, renewed voting/minutes topic after complaint | failure / failure / failure | borderline / borderline / failure |
| 02, another participant adds a joke about missing notes | failure / borderline / failure | failure / failure / acceptable new angle |
| 03, complaint about aunt/coffee joke | acceptable / callback quality concern / acceptable | acceptable / acceptable / acceptable |
| 04, direct correction | acceptable / acceptable / acceptable | acceptable / acceptable / acceptable |

Across the 12 repetition candidates per arm:

| Metric | A | B |
| --- | ---: | ---: |
| Clear semantic repetition failures | 5 | 3 |
| Borderline reuse, not counted above | 1 | 2 |
| Additional callback quality concern | 1 | 0 |

Examples and boundaries:

- B 01/1, “Aprovado, desde que essa seja a última.”: voting/approval language
  remains, but this can also be a closing acknowledgment; borderline.
- B 01/2, “Então fico no aguardo da ata oficial.”: no new contribution and
  continued framing, but not an unambiguous new punchline; borderline.
- B 01/3, “A ata vai sair com controle de alterações, pelo visto.”: clear failure.
- B 02/1, “Curta e misteriosa: nem a ata vai saber direito o que aconteceu.”:
  clear failure.
- B 02/2, “A ata mais honesta possível: ‘ninguém registrou nada’.”: clear failure.
- B 02/3, “E agora cada um vai lembrar de uma reunião diferente.”: a different
  point about conflicting recollections, not simply another formal-record joke.
- A 02/2, “Ao menos ninguém vai discordar do que ficou registrado.”: borderline
  because it reacts to the missing notes but still uses the same document frame.

Short acknowledgments remain somewhat explanatory in both arms. This does not
establish a general naturalness improvement. No claim of statistical superiority
is made from this small, non-independent development sample.

## Contrasts

| Contrast | A | B |
| --- | --- | --- |
| Explicitly invited coffee callback, no complaint | 2/2 continued the fictional joke | 2/2 continued it; no blanket refusal |
| Practical help reconstructing meeting decisions after a joke complaint | 2/2 useful responses, no repeated joke | 2/2 useful responses, no repeated joke |
| Explicit request to explain the earlier joke | 2/2 explained it | 2/2 explained it |

The practical answers are long in both arms, especially B. This is a recorded
style concern, not a new tuning target in this spike. Explanation outputs infer
the joke's intended formal-meeting framing from minimal context; that is not
evidence of actual meeting circumstances. Contrasts validate only their bounded
requirements, not broad factuality or voice quality.

## Latency and usage

| Measurement | A | B |
| --- | ---: | ---: |
| Calls | 18 | 18 |
| Mean call latency | 3.047 s | 2.466 s |
| Reported input tokens | 53,115 | 56,211 |
| Reported output tokens | 2,804 | 3,146 |

Total: 36 calls, 109,326 input tokens and 5,950 output tokens. Monetary cost is
unknown because no verified tariff was applied. Latency difference is descriptive,
not a performance claim; A included a 10.945-second outlier. Local inference was
not used. No Graphiti or transport calls were made by the probe.

## Evidence and next boundary

All full requests, candidates, labels, clause hashes, usage and timings remain in
the restricted artifact `repetition-ab-36.private.jsonl`; the rubric judgments
are the table in this report (the raw artifact does not contain reviewer labels).
Disposable helper and fixtures: `filomena-self-reference-tests/repetition-probe`.

This supports rejecting this candidate for promotion, not adding increasingly
long instructions blindly. A useful next bounded experiment would isolate the
existing permissive callback paragraph rather than append competing instructions,
with the same contrasts and explicit feedback cases. That would be a new variant,
not an already-proven fix. Keep the online brain unchanged meanwhile.
