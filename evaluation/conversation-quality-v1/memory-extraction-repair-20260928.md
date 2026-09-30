# Conversational memory extraction repair

## Status

Historical repair applied and verified. Candidate ingestion-context change passes
offline tests but is **not deployed**. Semantic acceptance of the active Gemma
extractor failed twice. Do not promote the candidate on unit-test evidence alone.
Filomena's conversational brain, transport, model assignments and running service
were not changed or restarted by this work.

## Observed failure and bounded implementation

`plugins/openclaw-memory-graphiti/message-lifecycle.ts::buildInboundCapture`
stored reply metadata in the episode envelope but previously supplied only the
current text as `extraction_body`. The candidate now supplies a versioned
`conversation-evidence:v2` object with separate current and quoted text. Unknown
quote author stays null. Only the original bound speaker is authoritative; a
quote's native sender field does not create a canonical actor binding. Provider
metadata is excluded from the extraction view. Persisted source envelopes, IDs,
namespace and deduplication semantics remain unchanged.

The social extraction policy is exported by the same module and consumed by
`index.ts`. It explicitly preserves speech acts, negation, scope and attribution;
questions/quotes are not endorsements, and second-person claims are not speaker
attributes. This is probabilistic guidance, **not a deterministic semantic guard**.

Graphiti's `node_operations.py::_extract_entity_summaries_batch` appends edge
facts to short summaries. Longer summaries use another model prompt, which does
not receive this custom extraction policy. Future acceptance must therefore test
both edge extraction and summarization. No backend summary code was changed here.

## Offline evidence

- Four new context tests were observed failing before the implementation.
- Focused context and native-recorder tests: 19 passed.
- Full plugin default suite: **299 passed, 15 skipped**, 27 passed test files and
  two skipped. External-service e2e tests are excluded by its default config.
- Typecheck and TypeScript emit succeeded.
- Scoped `git diff --check` succeeded (existing Windows line-ending warnings).
- No full .NET solution or Graphiti integration acceptance is claimed.

## Semantic evaluation

Corpus: `memory-extraction-cases.json`; complete outputs, policies and review:
`memory-extraction-review-20260928.json`.

| Candidate | Cases | Material failures | Mean latency |
| --- | ---: | ---: | ---: |
| Active Gemma model + initial candidate policy | 8 | 3 | 8.18 s |
| Active Gemma model + revised policy | 8 | 3 | 8.88 s |
| Luna + revised policy | 8 | 0 observed | 5.57 s |

Gemma is `google/gemma-4-e4b`, using the deployed Graphiti Python client and
`extract_edges.edge` prompt. Luna is `gpt-6-luna`, medium, using the existing
.NET `OpenAiFlowCompletion` Responses adapter with the same rendered Graphiti
task and schema inside its input. This is a **candidate comparison**, not proof
that Graphiti's deployed provider can already use Luna equivalently.

Cases cover negation, unresolved addressee, hypothetical question, slang, quoted
preference, explicit preference, explicit Boundary request, and emoji reaction.
The two Gemma attempts each retained three material failures. In the revised
attempt the hypothetical relation was labeled `ASKS_ABOUT` while its fact text
still claimed a suggestion: a correct relation label alone is insufficient.
Luna retained explicit preference and Boundary while abstaining on unresolved
attribution. One run per case, assistant review, no holdout or statistical quality
claim. Entity candidates were controlled: discovery, resolution, summaries,
embeddings and persistence were not exercised. No database clients were created
by the probes and no memories were written during evaluation.

24 successful evaluation requests total. Token usage and monetary cost were not
captured by these existing adapters; they are unknown, **not zero**. No chat
messages were generated or sent by the evaluation. Ordinary ongoing service
activity was not stopped, so no global zero-activity claim is made.

## Historical repair

Only the exact host-bound group and canonical Felipe identity were used. Identity
was verified by deterministic participant UUID from namespace and stable ID.
Every selected edge had source episodes by that canonical actor in that group.

- Seven incorrect active edges were expired, not deleted or rewritten in place.
- Seven affected entity summaries had the reviewed erroneous statements removed
  or corrected. Unrelated summary content was preserved.
- Six distinct source episodes stayed byte-identical; no re-extraction, identity
  merge, new episode, replacement fact, or embedding mutation occurred.
- All edge states, summaries and source bytes were reread and verified.

Repair scope: inverted negation, wrong second-person attribution, literalized
slang, three question-to-proposal promotions, and a TSE subject inversion. This
is not a full audit or certification of all of Felipe's other memories or every
derived relation in the group.

Private backup (restricted Helena/SYSTEM ACL):
`C:/Users/Helena/.config/openclaw-observation-ops-20260925-member-dms/graphiti-semantic-repair-20260928`.
It contains the exact before/after plan, SHA-256 and applied timestamp. The repair
uses a single Cypher update with compare-and-set predicates for every changed
edge/summary and source episode. A concurrent difference prevents the whole
planned update. Rollback applies the inverse only if the repaired values still
match, never overwriting newer work blindly.

Operator utility:
`C:/Users/Helena/filomena-self-reference-tests/repair-felipe-semantics.mjs`.
Modes: audit (already done), apply (already done), verify (read-only), rollback
(explicit recovery only). Do not rerun audit or apply on this backup.

## Remaining activation boundary

Do not deploy this as a permanent fix while the active extractor fails the corpus.
Recommended next step: approve a dedicated Luna extraction profile, then validate
the actual Graphiti provider, complete ingestion and summary path in a disposable
namespace, including repetitions and unseen contrasts, before controlled rollout.
Changing continuous memory extraction from local Gemma to remote Luna changes
recurring cost and data processing; the evaluation did not activate that change.
Do not modify the conversational generator merely to repair memory extraction.
