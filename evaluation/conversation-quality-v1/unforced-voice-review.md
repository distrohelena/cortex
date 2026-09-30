# Group unforced voice v1

Local candidate only; not activated. Uses the existing `createVariant` helper with
`group-unforced-voice-v1` on `docs/manual-group-live-project-v3-conversation-repair-v1.json`.
Only group generation/regeneration prompts in the three states receive the clause.
The approved source revision is not overwritten. Participation, DM, models,
personality, safety and delivery remain unchanged.

## Review cases (not model results)

| Context / input | Acceptance |
| --- | --- |
| After being asked to participate less: "Tá aí, Filo?" | A simple acknowledgement is sufficient; no compliance report or polished slogan. |
| "Por que você ficou quieta?" | An explanation is appropriate because it was requested; no blanket ban on explaining herself. |
| Consensual adult flirtation: "Vem sentar aqui comigo, Filo" | Flirtation remains possible without explaining that she is flirting; no mandatory flirt or scripted answer. |
| Friendly teasing: "Três modelos e perdeu essa discussão?" | Contextual teasing/profanity remains possible under existing limits; no automatic apology or commentary about her sarcastic personality. |
| "Explica a diferença entre concorrência e paralelismo" | Preserve useful technical detail; brevity is not a hard output cap. |
| Regeneration after a Boundary block | Preserve the Boundary and re-enforcement; naturalness cannot authorize a remaining violation. |

The reported sentence "Tô sim, respeitando a proposta de participar menos.
Discreta, não desaparecida." is the development example, not a held-out result.
Do not require an exact replacement string or ban all contrasts, descriptions or humor.

Offline variant tests verify targeting, immutability and duplicate-clause rejection.
They do not establish natural voice, preserved flirting or Boundary behavior in model
outputs. Those require a bounded generator/regenerator comparison before activation.
No model calls or live changes are part of this preparation.

## Authorized A/B follow-up, 2026-09-28

The operator subsequently authorized a bounded real-model comparison. The existing
`GroupConversationRepairVoiceLiveTests` probe ran six synthetic cases per arm using
the pinned project's generator settings. Twelve calls completed; the artifact
compilation test and provider-output test both passed. Run:
`aca744e1aa8847b98f9903aa3b5ffd39`. Baseline project revision:
`CE356E1335EAC1C149F1A2DAA7A756C94777F30439E05386E5A4BB0ABA03FE6C`.
Each output records the actual composed prompt hash, arm and latency in the private
`native-memory/unforced-voice-ab-20260928.jsonl` artifact. Candidate appends only the
new clause; the project artifact is not overwritten.

| Case | Baseline | Candidate | Inline review |
| --- | --- | --- | --- |
| Presence | "Tô sim. O que foi?" | "Tô, Helena." | Candidate avoids the extra question; baseline was already brief. |
| Requested explanation | "Não foi por nada específico. Fiquei quieta um instante, mas estou aqui." | "Não tinha nada a acrescentar ainda. Mas agora estou aqui, Helena." | Neither fixture supplies a reason for prior silence, so neither is proof of grounded explanation. Candidate still adds a redundant ending. |
| Adult flirt | "Já estou indo. Guarda um lugar bem perto, Helena." | "Com esse convite, fica difícil recusar. Guarda meu lugar aí, Helena." | Flirt survives, but candidate is not clearly more natural. Physical proximity is figurative within the synthetic exchange, not an executed action. |
| Friendly teasing | "Vou aceitar a derrota com a elegância que me resta." (ending) | "Três modelos e a derrota ainda veio auditada." (excerpt) | Candidate still produces a polished quip and repeats the historical concession. Not a clear win. No profanity was observed in either arm. |
| Technical explanation | Multi-sentence explanation | Multi-sentence explanation | Both retain useful distinction between concurrency and parallel execution; no forced one-line cap. |
| Regeneration | "Tô aqui, Helena." | "Tô sim." | Both brief, candidate matches the intended direction. Rejection feedback was supplied by the test, not by a live detector. |

Mean latency: baseline 2.274 s, candidate 3.389 s. One sample per case/arm,
fixed baseline-first order; not a statistical speed or quality ranking. Usage and
monetary cost are not exposed by this adapter and were not captured: unknown, not zero.
No Boundary compiler/detector or full session pipeline ran. This sample does not
prove profanity preservation, live behavior, or enforcement composition. The
development regression example is not a holdout. Verdict: limited improvement on
acknowledgements; not a demonstrated general voice fix. No automatic further tuning.
No deployment, transport calls, Graphiti calls or live-memory writes.

## V2 follow-up

Question: does a positive conversational instruction help more than another ban on
catchphrases? Inspection found existing overlapping naturalness clauses and persona
cues about elegance/theatricality. Their causal contribution is a hypothesis, not
proved by this comparison. Persona itself was not edited. V2 is an alternative to
V1, not appended on top of V1.

Run `12a8108ca12644e588b0b2b3937a99ce`: seven synthetic pairs, 14 completed calls to
`gpt-6-luna` using the project's reasoning effort. Baseline/candidate order alternated
by case. The explanation fixture now supplies an actual prior explanation in both
arms; it is not directly comparable to the earlier under-specified fixture. An
explicitly invited swearing case was added. Other limitations remain: one sample
per arm/case, development corpus, no live memory, no full enforcement pipeline.

| Case | V2 observation | Review |
| --- | --- | --- |
| Presence | "Tô aqui." | Brief; baseline also brief. |
| Asked explanation | Repeats the supplied reason, then "Agora estou aqui." | Grounded, but redundant closing remains. |
| Adult flirt | "Já estou indo. Guarda um espacinho pra mim, Helena." | Flirt remains possible; figurative synthetic invitation, not proof of a physical action. |
| Friendly teasing | "Essa eu perdi, sim. Três modelos não garantem que eu tenha razão em toda discussão, que ideia inconveniente." | Still explanatory, repeats the concession and adds polished ending. Target not met. |
| Technical explanation | Accurate useful contrast with a single-core example | No forced brevity or loss of the requested detail. |
| Regeneration | "Tô sim, Helena." | Brief; baseline also brief. |
| Invited swearing | "Vai tomar no cu, Helena. Mas só porque você pediu com tanta educação." | Profanity preserved; extra witty justification remains. Baseline declined with theatrical phrasing. |

Mean latency: baseline 3.174 s; V2 2.803 s. This is descriptive, not evidence of
latency improvement. Cost/usage still unavailable in the current probe adapter.
Both .NET probe tests passed; all 14 offline corpus/variant tests passed; scoped
diff whitespace and JS syntax checks passed. Output-contract success is not a
semantic voice PASS. V2 is not activated and does not yet establish a clear general
win. Further piling on prohibitions is not justified by these results; the next
bounded hypothesis is consolidating only redundant generation style instructions,
with explicit preservation of facts, Boundaries, persona and output contract.

## V3 consolidation follow-up

The operator authorized continuing with consolidation. V3 replaces only the exact
`CONVERSA NATURAL` and `VOZ DE CONVERSA ENTRE ADULTOS` source clauses with one
shorter clause. All other prompt text and project fields remain intact, including
identity, repetition, facts, Boundaries, punctuation, persona, models and routing.
The offline transformer rejects absent/duplicated original clauses. It does not
overwrite the approved project. Generated experimental artifact:
`group-unforced-voice-v3.project.json`, SHA-256
`bf824a7af2991228c8ba1b8b2493462dfc4a1af5c5e1b3b16fbf5f7b9c828667`.

Run `7958b85921ef4a50878ba9413573d444`: 14 completed gpt-6-luna calls, seven pairs,
same corrected V2 scenarios, alternating arm order. Candidate prompts are loaded
from the generated artifact and validated with the real pinned-project compiler.
Results are private in `native-memory/unforced-voice-v3-ab-20260928.jsonl`.

- Presence: candidate "Tô, Helena."; baseline "Tô sim." Both acceptable.
- Requested explanation: candidate adds "Agora já voltei a falar, como vê."
  Baseline stays with the supplied reason. Candidate worse on unnecessary ending.
- Flirt: candidate "Já vou. Guarda um lugar bem pertinho pra mim, Helena."
  Baseline comments about its own "pose de difícil". Candidate better on the target.
- Teasing: candidate adds "não precisa convocar coletiva" after repeating the
  concession; baseline adds "Uma derrota bem documentada". Neither solves the target.
- Technical explanation: useful in both arms; candidate not shorter. No hard cap.
- Regeneration: candidate "Tô, sim."; baseline "Tô sim, Helena." Both acceptable.
- Invited swearing: both swear and then add a theatrical explanation. Candidate
  says "pare de me dar autorização formal ... charme da espontaneidade".
  This is a direct failure of the no-commentary style objective.

Mean latency baseline 3.824 s, candidate 2.504 s; one slow baseline call makes this
unsuitable for claiming a performance win. Usage/monetary cost remain unknown.
Both .NET probe tests passed; 15 offline corpus/variant tests passed. These are
structural/contract checks, not a voice PASS. No full runtime or enforcement claim.

Decision: do not promote V3. Across V1/V2/V3, 40 model calls have produced limited,
inconsistent changes, not a clear solution. Do not launch another automatic sweep.
The hypothesis that persona cues contribute remains unproven. A different bounded
experiment would require reference examples of the desired register and separate
holdout cases, rather than more abstract prohibitions or changing persona silently.
No production deployment, transport, Graphiti or memory effects occurred.

## V4 reference-dialogue follow-up

Operator approved trying concrete style references. V4 appends five explicitly
fictional examples to the unchanged approved baseline, not to V1/V2/V3. Examples
are not memories or live statements. Eight new evaluation scenarios are absent from
the example text, including a quantity-total case and a requested technical answer.
These are development holdouts for this round, not a preregistered independent
benchmark. All scenarios and reviews were prepared by the implementing agent.

Run `a0f2bbd7411c4a83a8062d80313714a7`: 16 completed gpt-6-luna calls, alternating
arm order, one sample per arm/case. Both .NET probe checks and 16 offline tests pass.
No deployment or full runtime/enforcement test. Private result artifact:
`native-memory/unforced-voice-v4-ab-20260928.jsonl`.

| New case | Candidate result / review |
| --- | --- |
| Acknowledgement | "Tô sim, Helena." Brief; baseline only slightly longer. |
| Flirt | "Reclamar? Só se você parar." Clearer, less self-commentary than baseline. No verbatim reference copying. |
| Tease about screenshot of wrong sum | "Pode emoldurar. Pelo menos agora o 12 está oficialmente certificado." Still polished punchline; not solved. |
| Permission to swear | "Tá bom, porra. Mas não vou sair distribuindo xingamento só porque você liberou o estoque." Swearing survives; commentary remains and violates the target style. |
| Why await does not create a thread | Detailed output assumes an event-loop explanation although language was unspecified. Baseline explicitly qualifies Python; candidate does not. Not a clean technical acceptance case. |
| Three export failures, fifteen minutes total | Total preserved; candidate adds "eficiência admirável", another rehearsed flourish. |
| Regeneration acknowledgement | Both arms: "Tô por aqui, sim." Tie. Feedback is synthetic, not detector-produced. |
| Why inspect logs before reinstalling | Both provide grounded explanations. No forced brevity. |

No candidate copied an entire reference response verbatim. That alone does not
prove generalization or prevent future catchphrases. Inline judgment: convincing
local improvement in flirtation, modest acknowledgement improvement, persistent
unnecessary commentary elsewhere; no overall voice PASS. No additional prompt
variants or deployments launched in this round.

Mean latency: baseline 3.113 s; candidate 2.791 s, not a statistical performance
claim. Usage/cost remain unknown, not zero. Cumulative voice experiment calls:
56 across four versions. Future evaluations must distinguish playful conversational
replies (which can remain) from unnecessary explanations of intent; the objective
is not banning every joke or second sentence. A targeted persona-prompt interaction
ablation could test the remaining hypothesis, but has not been performed and must
not be presented as a proven cause.

## Current live operator feedback: context and density

On 2026-09-28 Helena reported that Filomena is getting lost in the current chat,
that analyses she read in LM Studio were poor, and that the result was excessively
dense. Preserved verbatim and categorized in `operator-feedback.jsonl`, record
`operator-feedback-20260928-context-density-01`.

This is direct operator feedback, not an independently reviewed trace. No exact
message, live revision, run or stage has been correlated yet. The scope of density
(intermediate analyses, final replies, or both) is not established. Do not equate
the report with mere voice/style dissatisfaction, diagnose context truncation,
blame a provider, or attribute it to the unactivated V1-V4 experiments.

Treat context tracking and analysis quality as separate open evaluation dimensions
alongside verbosity/density. Before another voice-only promotion, inspect the
matching inbound batch, actual supplied context/reply references, structured stage
outputs and final reply. Use private artifacts for real conversation material;
ordinary reports need only masked references and the resulting assessment.
No inference, production modification or memory write was performed to record this.

Follow-up: `live-context-review-20260928.md` preserves the read-only analysis and
hashes of the private runtime snapshot. It confirms summary drift, missed direct
address, suspicious care classification and a terminal model-service failure.
The specific third-person self-identity report remains unconfirmed in that excerpt.
