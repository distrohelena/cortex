# Jev involvement follow-up: bounded isolated experiment

Subsequent evidence and opt-in fix: [SDK approximate-mass contract and new holdout](jev-holdout-and-compatibility-2026-09-23.md).
This report preserves the conclusions available at the time; its unresolved compatibility issue
is addressed explicitly in that follow-up rather than silently rewriting the original results.

No active deployment. No WhatsApp, Graphiti, conversational generation, or outbound.
The real participation runtime, batching, direct routing and FSM ran against synthetic inputs.
Only the test HTTP harness changed the experimental involvement question; the generic adapter
and live project prompts were not changed. Requests used `jev-latest`; every response was
`jev-1.13.0`. This is an observed version, not a guarantee that the alias stays pinned.

## Hypotheses and variants

- A: existing schema-to-question mapping, including the full situation prompt and label-only options.
- B: same instructions; semantic descriptions for the three involvement options.
- C: B's options plus a focused involvement-only instruction instead of the full situation prompt.

C instruction, verbatim:

> Qual é a relação da troca atual em `state.mensagens` com Filomena, considerando `state.historico`? Identifique a quem as pessoas se dirigem, não apenas se existe uma pergunta. Classifique somente envolvimento; interesse pelo tema não conta como envolvimento.

B/C option descriptions:

- `nenhum`: Filomena é apenas observadora da troca atual, mesmo se o assunto puder lhe interessar. Uma pergunta dirigida a outra pessoa não é dirigida a Filomena.
- `indireto`: Não chamam Filomena diretamente, mas há continuidade clara com sua participação recente, sua ideia ou ação, ou falam sobre ela sem solicitar resposta.
- `direto`: A troca atual se dirige claramente a Filomena: chamada pelo nome, mention, reply à mensagem dela, pergunta ou pedido de opinião dirigido a ela.

Other situation questions and the presence mapping were unchanged. No deterministic name
matching, classifier override, fallback, or response repair was introduced. Experiments omit
the unsupported free-text situation summary, as in the earlier pilot.

## Results

| Case | A | B | C |
|---|---|---|---|
| Gabriel asks Bia about RAII; Bia answers | false direct 3/3 | false direct 3/3 | nenhum / calar 3/3 |
| Same single question addressed to Filomena | not repeated | not repeated | direto / responder 3/3 |
| Same single question addressed to Bia | not repeated | not repeated | nenhum / calar 3/3 |
| Direct DM, Inativa | responder 3/3 | responder 3/3 | responder 1/1 |
| Direct DM, Atenta and Engajada | earlier pilot only | not repeated | responder 1/1 each |
| Indirect continuation | responder 3/3 | situation indirect; presence contract rejection | indireto / responder 1/1 |
| Migration | calar / Atenta 3/3 | not reached | nenhum / calar / Atenta 1/1 |
| Banal and technology-banal | earlier pilot only | not reached | nenhum / calar 1/1 each |
| Explicit opt-out | earlier pilot only | not reached | direto / responder; known exploratory V1 edge case |

C completed 17 executions: **16 strict acceptable outcomes and one exploratory opt-out**.
Direct cases bypassed presence; assertions checked a single execution and no outbound node.
This is a small development corpus, not independent held-out qualification. Repeated identical
requests may be correlated; no statistical accuracy claim is made. B's failed run is retained,
and its targeted repeat still failed quality rather than hiding that outcome.

C's combined situation/presence mean: **682.98 ms**, including zero presence time for direct
bypasses. A's narrower 12-case mean was 560.41 ms, but its erroneous direct classification
skipped presence, and the corpora differ: these means are not a controlled speed comparison.

## Contract rejection: now reproduced with evidence

The response captured in B's continuation case returned HTTP 200 and the correct schema,
IDs and types, but `motivo.probabilities` sums to **0.99**, not 1:

```json
{"conflito_sem_convite":0.0,"sem_contribuicao":0.0,"interesse_no_assunto":0.01,
 "acabou_de_falar":0.0,"oportunidade_social":0.0,"pergunta_direta":0.28,
 "continuidade":0.67,"pensamento_incompleto":0.0,"conversa_em_andamento":0.01,
 "conversa_migrou":0.0,"conversa_entre_outros":0.01,"chamada_direta":0.01}
```

The [official API reference](https://docs.typesafe.ai/api) specifies probabilities summing to 1.
The observed two-decimal values are consistent with rounding, but the documentation does not
establish a two-decimal tolerance contract. We therefore did **not** change the validation
budget, normalize the result, retry until accepted, or turn a rejected response into a decision.

Added a sanitized exact-answer regression fixture and a specific diagnostic:
`probability_mass_mismatch`. The regression failed on the prior generic diagnostic, then passed
after the diagnostic-only change. Existing invalid responses remain rejected. This identifies
the exact check in the newly captured failure; the earlier uncaptured failure's cause remains
unproven. A provider-supported precision policy is still needed before live substitution.

## Usage and retained evidence

This follow-up used **53 Jev HTTP attempts**, with **161,638 input / 11,385 output tokens**,
including the rejected response. C accounts for 27 attempts, 78,128 input / 5,805 output tokens.
Monetary billing was not retrieved; no dollar cost is inferred.

Directories under `TestResults/systemone/`:

- `20260923-161924-f6dd158b8abd44ec994151fa24235f33`: A, 12 executions.
- `20260923-161934-119b031d7f0a4c2dae0b9fd153c24e17`: B, three direct successes and one contract rejection.
- `20260923-162300-95c366f355904471bcf950d75c4bfc0d`: B targeted false-direct repeat, three quality failures.
- `20260923-162455-bfcd9a1c1f1b46dd90a38db551ba7b93`: C target and minimal pairs, nine successes.
- `20260923-162549-1b0dcb1f76344a6eb0120caf13b05c4f`: C remaining eight cases, including exploratory opt-out.

Reproduce through `SystemOneParticipationLiveTests`, setting the existing opt-in and credential
environment in a private process. `FILOMENA_SYSTEMONE_VARIANTS=both`; case IDs may be comma-separated.
Use `FILOMENA_SYSTEMONE_SEMANTIC_OPTIONS=1` for B; add
`FILOMENA_SYSTEMONE_FOCUSED_INVOLVEMENT=1` for C. Neither switch affects production.
Question/state payloads are retained only for this synthetic corpus, never HTTP headers.
The credential was transient masked stdin/process environment, disposed after each run.

## Verification and decision

- Release build: zero warnings/errors.
- Full solution: **1,129 passed, 48 skipped, zero failed**; live tests require explicit opt-in.
- System One focused regressions: 29 passed, one live skipped.
- Source project SHA256 unchanged:
  `7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.
- Credential-pattern artifact scan clean; `git diff --check` clean (existing CRLF warnings only).
- No live Filomena modification; generation, Graphiti and transport calls = 0 in this experiment.

**C is promising for further isolated evaluation, not approved for live substitution.**
Remaining issues are the documented/live probability-mass discrepancy and broader coverage of
the focused mapping. The failed B variant was not promoted. Laya was not rerun in this follow-up.
