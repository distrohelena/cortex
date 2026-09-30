# Continuous memory extraction: Luna acceptance and deployment

## Scope

The authorized change replaces the memory extraction model, not Filomena's
conversational brain. The operational project hash remains pinned. Embeddings
remain `text-embedding-nomic-embed-text-v2-moe`, local, 768 dimensions. No graph
migration, namespace remapping, participant merging, or wholesale reingestion is
performed.

The opt-in profile is `runtime/config.luna-memory.yaml`: OpenAI Responses,
`gpt-6-luna`, medium reasoning, 4096 output-token limit. Both Graphiti model sizes
use that model. Requests set `store=false`. Factory failures stop initialization;
they no longer select Graphiti defaults silently. This does not claim external
zero retention beyond the request parameter.

## Correctness changes

- `message-lifecycle.ts` exports the shared `conversation-evidence:v2` extraction
  policy and separates current text from quoted context. Raw source episodes are
  preserved.
- `tools/conversation-memory.mjs` consumes that export instead of its older copied
  policy. An outdated compiled artifact fails startup explicitly.
- The policy reaches `extract_attributes_from_nodes` and the batch summary prompt,
  not just edge extraction. Short fact-based summaries remain fact-based.
- The MCP exact-neighborhood response carries the loaded entity's UUID, namespace,
  stable ID, labels and display name even when there are no active facts. Exact
  absence has a distinct versioned result; outages remain errors. No identity is
  inferred from a display name or fabricated fact.

The last contract already existed in the checkout, but was absent from the
installed image. Restart exposed two existing participants with zero active
facts: the old endpoint omitted their identity and preflight correctly stopped.
The four existing neighborhood tests reproduced two failures against the old
image and passed against the final candidate. Ten operational participant probes
then returned matching UUID, stable ID and namespace, including both empty cases.

## Evidence

- Plugin: 299 tests passed, 15 skipped; TypeScript no-emit check passed.
- Maintenance/supervisor: 26 tests passed, including drain, restart and rejecting
  an obsolete policy artifact.
- Graphiti Luna/provider contracts: 6 tests passed.
- Exact neighborhood identity/absence/outage: 4 tests passed.
- Candidate Docker image built offline from the preserved installed image.
- Actual service composition initialized with Luna and local embeddings in a
  separate `eval-memory-service-*` namespace.
- Eight actual `Graphiti.add_episode` executions ran in distinct
  `eval-memory-luna-*` namespaces. This exercised extraction, resolution,
  embeddings, graph persistence/readback and summaries. Five summary-model calls
  occurred. These are controlled fixtures, not production memories.

Full results: `evaluation/conversation-quality-v1/memory-full-luna-20260928.json`.
Reproducible explicit live harness: `runtime/validate_graphiti_luna.py`.
Input consists of the shared policy, configured entity types and the existing
`memory-extraction-cases.json` corpus, mounted as `/evaluation/memory-input.json`.
The harness is never an automatic unit test: it spends money and creates eval
graphs. It does not invoke a transport sender.

Observed review: no material semantic error in these eight single runs. Negation,
hypothetical questions, unresolved addressees, slang and quoted preferences were
not converted into unsupported personal facts. Explicit preference and boundary
were retained. One emoji fact was in English; this is a minor language-quality
issue, not silently counted as perfect output. This sample is not a statistical
guarantee or a holdout benchmark.

Measured full-ingestion total: 135.272 seconds, 29 model calls, 37,039 input tokens,
6,610 output tokens. Cost is **unknown**, not zero; no verified tariff was applied.
The earlier generator-only comparison is preserved separately and is not being
reported as a full-ingestion result.

## Operational procedure and limits

Use both compose files explicitly:

```powershell
docker compose -f runtime/docker-compose.yml -f runtime/docker-compose.luna-memory.yml up -d --no-deps --no-build graphiti-mcp
```

The existing authorized `OPENAI_API_KEY` must be supplied through the host
environment. Never paste it into YAML or print rendered compose configuration.
Running only the base compose file selects the old local profile; the overlay is
intentional and required for future recreations. Container restart itself retains
its configured profile. The narrow Dockerfile is pinned to the locally preserved
pre-change image; it is not advertised as a portable upstream release build.

Before replacement, the existing supervisor was stopped through `stop.request`.
Its durable recovery status confirmed 494 persisted, cursor 494, pending zero.
No journal, cursor, receipt or historical source message was reset. The Gateway
was not restarted. A changed operational revision was revalidated against the
same route and epoch, valid snapshot and inactive legacy dispatch before pins
were refreshed. No proof was bypassed.

Private rollback artifacts and hashes are under the existing operations root,
`luna-memory-20260928`, ACL restricted to Helena and SYSTEM. The previous image is
preserved as `filomena-graphiti-mcp:pre-luna-memory-20260928`. Historical repairs
have their separate compare-and-swap rollback. Application rollback cannot undo
later accepted episodes and must not rewind ingestion cursors. Restoring the old
API image also restores its empty-neighborhood limitation; do not automatically
resume a worker whose preflight fails.

Seven previously reviewed invalid facts remain expired and seven reviewed
summaries remain repaired; verification confirmed unchanged source episodes.
This is not a claim that every historical fact is now correct. Future extraction
is probabilistic, protected by explicit attribution policy and regression
fixtures, not guaranteed infallible.

Retained-history coverage remains partial: unavailable payloads and unsupported
non-text messages were not invented or counted as ingested knowledge.
