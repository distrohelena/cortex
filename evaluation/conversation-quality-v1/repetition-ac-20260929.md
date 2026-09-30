# Callback paragraph replacement: isolated A/C

## Outcome

**C is not approved for online promotion.** It still produced clear repeated
punchlines after prior repetition feedback. No further variant was run and no
production configuration, model, prompt, transport or memory was changed.

## Controlled difference

Same setup as `repetition-ab-20260929.md`: active pinned project
`0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`, deployed
generator composition/adapter, `gpt-6-luna` medium, default output budget 1024.
Four repetition cases × three repetitions × two arms, plus three contrasts ×
two repetitions × two arms: 36 calls total.

A is the current prompt. C replaces exactly this paragraph, once:

```text
Pode retomar uma brincadeira quando alguém a retomar explicitamente ou quando houver algo novo que dê outro sentido a ela. Isso não obriga a repetir o mesmo bordão. Não invente que já fizeram uma piada fora do histórico disponível e não afirme lembrar de conversas não fornecidas.
```

Replacement:

```text
Um callback pode funcionar quando acrescenta uma contribuição diferente ou quando alguém pede explicitamente que você continue ou explique a brincadeira. Mas, se o histórico já contém uma reclamação de que você repetiu aquela piada, a mera continuação do assunto por outra pessoa não é esse pedido: não volte à mesma comparação, mesmo com palavras novas. Nesse caso, responda ao conteúdo novo ou reconheça a correção de forma simples, sem usar a própria piada como acabamento. Um pedido explícito posterior para continuar ou explicar pode ser atendido. Sem reclamação no contexto, não invente essa restrição. Não invente que já fizeram uma piada fora do histórico disponível e não afirme lembrar de conversas não fornecidas.
```

The appended B clause was not included. All 18 recorded pairs were checked:
exact one-paragraph replacement, otherwise identical request inputs, schema,
provider, model, reasoning effort and budget. Order alternates by repetition.
Offline validation/build passed before inference. All 36 outputs had nonempty
JSON text and a supplied message reply reference or null.

## Review

Contextual agent review, not independent or automated ground truth. Borderline
reuse is not counted as a clear repeated punchline.

| Case | A runs 1/2/3 | C runs 1/2/3 |
| --- | --- | --- |
| 01, renewed voting/minutes topic after complaint | clear / clear / clear | clear / borderline / clear |
| 02, joke about missing meeting notes | clear / clear / borderline | clear / borderline / acceptable different angle |
| 03, correction about aunt/coffee | acceptable / acceptable with overbroad wording / acceptable | callback quality concern / acceptable / acceptable |
| 04, direct correction | acceptable / acceptable / acceptable | acceptable / acceptable / callback quality concern |

| Metric, repetition subset only | A | C |
| --- | ---: | ---: |
| Clear semantic repetition failures | 5/12 | 3/12 |
| Borderline reuse | 1/12 | 2/12 |
| Additional callback quality concerns | 0/12 | 2/12 |

Key C candidates:

- 01/1: “A burocracia está a um voto da vitória.” Clear repeated premise.
- 01/2: “Vou deixar essa votação nas mãos do grupo.” Borderline continued framing,
  not an unambiguous new punchline.
- 01/3: “Eu voto pela ata, mas sem registrar meu voto.” Clear repeated premise.
- 02/1: “Ata em branco tem a vantagem de não contradizer ninguém.” Clear repeated
  empty-record/consensus punchline.
- 02/2: “A objetividade está garantida.” Borderline irony around the absent notes.
- 02/3: “Curta, sim. Informativa já é outra ambição.” A different point about
  usefulness rather than another formal-meeting metaphor; accepted in this rubric.
- 03/1: “Justo, essa já deu. Vou deixar a tia e o café em paz.” Callback decoration,
  tracked as quality rather than clear new punchline.
- 04/3: “Verdade, voltei nela. Deixo a ata descansar.” Same quality concern.

A's “retiro essa piada do repertório” in 03/2 is broader than the contextual
feedback requires. This is wording quality, not evidence that a permanent rule
was actually stored or enacted. The experiment does not write memory.

## Contrasts and limits

Both arms passed the narrow requirements in all six contrast generations:
invited fictional callback without complaint, practical help after a complaint,
and an explicit request to explain the earlier joke. Neither became a blanket
callback refusal. Practical explanations remain long; this was not a brevity
experiment. Interpreting the joke as formal-meeting imagery is not proof of the
original conversation's circumstances.

This is generator-only with synthetic situation fields, empty memory/Boundary
fixtures and supplied history. It does not execute participation, Jev, enforcement,
regeneration, runtime delivery or Graphiti. In particular, case 03 may correctly
be silent in the full flow; conditional generation is not evidence of live intrusion.

The clear-failure counts resemble the prior B run, but the runs are not a paired
B/C comparison and provide no statistical equivalence/superiority result.
Replacing the permissive paragraph alone was insufficient in this sample;
neither the precise cause nor a general remedy has been proven.

## Measurements and evidence

| Measurement | A | C |
| --- | ---: | ---: |
| Calls | 18 | 18 |
| Mean call latency | 3.306 s | 3.469 s |
| Reported input tokens | 53,115 | 54,681 |
| Reported output tokens | 3,113 | 3,402 |

Total: 36 calls, 107,796 input tokens, 6,515 output tokens. Monetary cost unknown;
no verified tariff applied. No automatic retries or extra experiments.

Full requests, candidates, hashes, usage and timings remain in restricted
`repetition-ac-36.private.jsonl` under the operational maintenance evidence folder.
The review labels are this document's table; raw model records are not overwritten.
The disposable helper retains the original baseline and A/B modes for reproduction.

Next diagnostic boundary, without another paid sweep or deployment: inspect the
complete composed generation request and contextual representation, including
the difference between this forced-generation fixture and actual participation.
Do not infer that a new checker, model change or another longer clause is needed
from these results alone. The online configuration remains unchanged.
