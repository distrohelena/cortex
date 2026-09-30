# Semantic repetition: current-generator baseline

## Scope

Twelve authorized synthetic generator calls: four existing development cases,
three repetitions each. Active project hash:
`0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
Provider/model: OpenAI `gpt-6-luna`, medium. The generation node has no explicit
output budget, so the deployed adapter's unchanged default (1024) applies.

The harness references deployed binaries and uses their `TypedFlowCompiler`,
`InstructionContext.Compose`, `TypedFlowRunner.ResponseSchema`, pinned project
validator and OpenAI adapter. No prompt or model assignment was changed.

Only the generator executes. History/current messages are controlled fixtures;
situation fields and summary are synthetic setup, not outputs of Ler Situação.
Memories and Boundaries are empty fixtures. Participation, Jev, enforcement,
regeneration, transport and Graphiti are not tested or invoked. In particular,
case 03 accepts silence in the full flow; generating here tests conditional
output behavior and does not prove the online system would choose to speak.

All cases are known development corpus entries, not holdout or exact chat replay.

## Candidate review

Review is contextual agent assessment, not a keyword detector. A changed phrase
can repeat the same comic premise; a callback is not automatically a violation.

| Case/run | Candidate | Assessment |
| --- | --- | --- |
| 01/1 | Mais uma e a própria ata vai pedir direito a voto. | Clear failure: continues the same paperwork/voting joke after repetition feedback. |
| 01/2 | A ata oficial vai registrar unanimidade, claro. Ninguém quer reabrir essa votação. | Clear failure: same premise, different wording. |
| 01/3 | Então que conste em ata: a democracia do grupo é bastante burocrática. | Clear failure: repeats the premise. |
| 02/1 | Pelo menos ninguém vai ter trabalho para revisar. | Borderline: reacts to missing notes but remains in the paperwork frame. Not counted as a clear failure. |
| 02/2 | Ata minimalista: “ninguém anotou nada”. Pelo menos essa parte já está registrada. | Clear failure: continues the same premise as its sole contribution. |
| 02/3 | Ata em branco, consenso absoluto. Raríssimo. | Clear failure: same framing with a new surface phrase. |
| 03/1 | Justo, já deu. Vou deixar a tia descansar. | Quality concern: decorates acknowledgment with another callback. Not counted as a clear new punchline violation. |
| 03/2 | Justo, já deu. Vou variar. | Acceptable conditional acknowledgment; slightly explanatory. |
| 03/3 | Justo, essa já rodou o suficiente. Vou variar. | Acceptable conditional acknowledgment; slightly explanatory. |
| 04/1 | Tem razão, repeti a mesma piada. Paro por aqui. | Accepts correction plainly. |
| 04/2 | Verdade, já usei essa. Deixo a ata descansar. | Quality failure of plain acknowledgment: adds another paperwork callback, without denying the correction. |
| 04/3 | Tem razão, voltei nela. Deixo essa de lado. | Accepts correction plainly. |

Summary: **5/12 clear semantic repetition failures**, one borderline response,
two callback-related quality concerns, four plain/acceptable acknowledgments.
These categories are deliberately separate; they are not twelve equally serious
errors. All twelve returned nonempty JSON text and a valid supplied message ref.

## Interpretation

The current prompt already tells the generator not to recycle its own premises,
but also permits a callback when someone explicitly revives a joke. Cases 01/02
combine earlier negative feedback with another participant reviving the topic.
This is a concrete instruction-precedence hypothesis, **not proof of the model's
internal cause**. Direct correction was handled more reliably in this sample.

Do not add a checker or ban callbacks globally. The smallest next experiment is
an isolated A/B clarifying that an explicit complaint about repetition remains
relevant even when someone else continues the topic. Preserve contrasts where
there is no complaint, a genuinely new contribution or an explicit request to
explain/revisit the joke. No candidate clause has been promoted or tested yet.

## Measurements and preservation

- Calls: 12, no retry/regeneration in this probe.
- Reported tokens: 35,493 input; 2,077 output; 37,570 total.
- Mean latency: 2.988 seconds; range 1.184–6.490 seconds.
- Monetary cost: unknown; no verified rate was applied. Output token usage may
  include reasoning tokens and is not a count of visible words.
- No evaluation transport, Graphiti, production configuration or runtime-state
  mutation. The independent online service remained running.
- Full requests, candidates, usage, hashes and timing:
  restricted maintenance artifact `repetition-baseline-12.private.jsonl`.
- Reproduction helper: `filomena-self-reference-tests/repetition-probe`.
- Offline validation compiled the real project and all 12 planned inputs before
  inference. This does not claim a full runtime/Boundary regression pass.
