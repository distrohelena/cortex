# Graphiti Live Preflight — contract diagnosis

**Atualização posterior:** após autorização para iniciar o serviço, o embedder foi iniciado e o preflight passou. O registro abaixo permanece histórico. Ver [run manual e resultado atualizado](manual-live-shadow-six-report.md).

## Outcome

**GRAPHITI LIVE PREFLIGHT = FAIL: backend_read_error.** No ShadowRun or interaction was started. The returned semantic response is a valid backend **ErrorResponse**, not evidence of cross-namespace memory. The adapter/preflight's old aggregated error classification obscured this distinction; fail-closed behavior itself was already correct.

Two problems are distinguished:

1. Operational blocker: `search_memory_facts` returns `Error searching facts: Connection error.` The configured embedding endpoint is unreachable from the Graphiti container.
2. Local diagnostic bug, corrected: backend errors, malformed success schemas and missing/foreign provenance all previously appeared as `invalid_or_cross_namespace_result`.

No service was started, reconfigured or restarted to force this gate through. The next operational step is to make the **already configured** embedding service reachable, then repeat the read-only preflight. This is not a request to change namespace rules or relax validation.

## Actual service/contract inspected

- Endpoint used by the existing host binding: loopback MCP `/mcp`, port 8000, Streamable HTTP.
- Active container: `filomena-graphiti-mcp`, image `filomena-graphiti-mcp:local`.
- Image identity: `sha256:53e0ef206586350730380d36ddf45dd1c35e5398c0278a591d93d82cd6fd86f0`.
- Active `/app/mcp/src/graphiti_mcp_server.py` SHA256: `c94faa8ddd84136f0b01e129256198dd243101c8adde0b9c71351a2a3a0be0f8`.
- Active `/app/mcp/src/utils/social_retrieval.py` SHA256: `cebd43fb0a9befbb1a843030cfb703ae25f2df620a984bb0f02bdc7f51a0d222`.
- Also inspected the installed `graphiti_core/search/search.py` and `graphiti_core/graphiti.py` under the container's Python site-packages. Local vendor code was a reference, not assumed to identify the deployed revision.

The MCP tools return `FactSearchResponse | ErrorResponse`. The observed SDK `StructuredContent` has a single `result` wrapper. Both responses had `isError=false`; therefore MCP success **does not mean the backend query succeeded**. No explicit application schema/version field was returned. Numeric HTTP status was not recorded by this diagnostic; MCP RPC completion and its envelope were recorded.

Observed exact shape:

```json
{"result":{"message":"Neighborhood retrieved successfully","facts":["<one full fact in private artifact>"]}}
```

Observed semantic response (complete application payload, no personal data):

```json
{"result":{"error":"Error searching facts: Connection error."}}
```

The successful fact contained `uuid`, `group_id`, `source_node_uuid`, `target_node_uuid`, `created_at`, `name`, `fact`, `episodes`, `expired_at`, `valid_at`, `invalid_at`, `attributes`, `source_node`, `target_node`.

Namespace is on **each fact**, not inferred from text. The endpoint records carry UUID, labels, stable identity and canonical flag; they do not repeat `group_id`. The active backend loads endpoints from each edge's physical/group-bound graph before formatting them. The C# mapping already preserves fact ID, namespace, canonical subjects, exact/semantic source kind and episode references. No missing namespace mapping was found in the successful response.

## Trusted binding and per-item proof

Expected namespace is derived from the host-owned logical Helena DM binding, not from response content. Its opaque SHA256 ref is `D8B377A09FFCA48EF61D2E0C5A0EE91C55F8FED3BCA69AD5C454574582ACC4EB`.

The single exact item has opaque fact ref `2AB326B86018FB6D4D28396EF483B8AF0EAE3FF32B49A0B74B89FE5EA3FA75BD`:

- `group_id` exactly matched the expected namespace.
- Both endpoint UUIDs matched the edge's corresponding endpoint UUID fields.
- Source was canonical Person, with the expected stable peer identity and deterministic participant UUID.
- Target was Entity/Topic, not falsely treated as a Person.
- Relation was `SHARES_INFORMATION`; one episode reference was present.

This is provenance evidence for **that exact result**, not proof of all namespace isolation, canaries, or Boundary availability. No fact content or raw transport identity is included here.

## Operational cause

The active semantic implementation calls Graphiti's edge hybrid search, which obtains a query vector from the configured embedder before its graph retrieval. Here “semantic” names the C# recall mode; Graphiti internally uses hybrid vector/text retrieval. Filomena's own `Hybrid` mode composes exact neighborhood plus that semantic operation.

Active non-secret embedding configuration inspected:

```text
EMBEDDER_API_URL = http://host.docker.internal:1234/v1
EMBEDDER_PROVIDER = openai
EMBEDDER_MODEL = text-embedding-nomic-embed-text-v2-moe
RERANKER_PROVIDER = none
```

A single TCP connection check from the Graphiti container to that host/port returned errno **111: connection refused**. This establishes an unavailable required semantic dependency, consistent with the returned backend error. The MCP ErrorResponse contains no traceback, so it does not independently identify a deeper internal exception beyond “Connection error.” No guessing about data corruption or namespace leakage is warranted.

## Exact, semantic, hybrid

| Mode | Transport | Schema | Namespace/identity/subject | Result |
|---|---|---|---|---|
| Exact, live direct read | Valid | Valid facts response | Expected namespace, canonical participant, 1 fact | Successful |
| Semantic, live direct read | Valid MCP, `isError=false` | Valid **ErrorResponse** | Not applicable: no facts returned | Backend error; not empty |
| Hybrid | Composition previously failed at the semantic leg; not rerun as a functional live recall this round | Exact data cannot substitute for failed semantic leg | Local regression verifies no partial return and no provenance loss on successful merge | Still blocked, no live PASS |

After the local fix, one preflight-only revalidation made an exact read and then a targeted semantic canary read. It again stopped on the backend error, before canary evaluation, foreign-namespace reads or functional Boundary/social recall. It exited nonzero; zero executions.

The raw diagnostic's passing test result means **response capture succeeded**, not that Graphiti passed readiness.

## Minimal local correction

- Shared internal `GraphitiResponseValidation` distinguishes `backend_read_error`, `response_schema_invalid`, `fact_identity_missing`, `namespace_provenance_missing`, and `confirmed_cross_namespace_leakage`.
- Stages report `transport_valid`, `response_schema_valid`, `backend_read_success`, `result_identity_valid`, `result_namespace_valid`; canonical participant evidence is reported separately. A valid error schema never makes `backend_read_success` true.
- Missing/blank namespace is insufficient provenance, **not foreign namespace**. A differing nonempty namespace is a confirmed mismatch.
- Reader and preflight use the same validation; errors remain exceptions, never `[]`.
- Stages not reached are represented as `not_run` in the final implementation, rather than claiming a failed live recall occurred.
- Canary isolation now gates functional Boundary/social recall. Positive controls use targeted queries; foreign markers stop immediately after structural namespace validation. An absent positive control is `expected_canary_not_found`, not leakage.
- No changes to memory data/writing, namespaces, Boundary semantics/compiler, prompts, Luna, FSM, transport or OpenClaw.

## Deferred proofs

- PRIVATE91 expected presence and DRIFT42/PLEX73 exclusion: **not proved**, since semantic backend success was unavailable. No canary failure/leakage was observed.
- Boundary live and compiler acceptance: **not reached**. Do not report a valid empty Boundary.
- Social memory functional recall: **not reached**. The one exact fact is not a completed social-memory gate.
- Empty versus outage: covered by local contract/integration regressions; the live backend error was correctly rejected. No live successful semantic empty result was claimed.

## Evidence and verification

Private, ignored experimental artifacts:

- `state/manual-shadow/contract-75d36c86f96e4408ab5b18a97b5abf30.jsonl`: full raw MCP responses for exact and semantic reads.
- `state/manual-shadow/manual-shadow-57c75494e7274fca88c969de129bb230.jsonl`: post-classification-fix live preflight, about **1.77 seconds** to failure. A subsequent local-only presentation fix marks unexecuted checks `not_run`; it did not trigger another live probe.

Local tests: **38 passed** in the focused adapter/preflight/harness selection; **491 passed, 10 skipped** in Core. New regressions cover the observed error envelope, sanitized successful fact shape, exact/semantic/hybrid provenance and dedupe, missing versus foreign namespace, no partial hybrid fallback, and stopping before Boundary/social after foreign canary. Existing tests retain canonical-subject rejection, valid empty, outage and temporal filtering.

TDD red/green observed for diagnostic separation, canary stop ordering and unexecuted-stage reporting. No production memory semantics were loosened.

## Final accounting and recommendation

- **0/20 interactions**, 0 candidates, 0 WOULD_SEND, 0 conversation executions.
- Filomena model calls, Luna cost and tokens: **0**. Graphiti attempted its configured semantic dependency and returned an error; this is not a claim that semantic search is independent of embeddings.
- `messages_send`, transport outbound, Graphiti writes: **0**.
- SendGuard remains SHADOW-only in the harness, with no real transport; enabled remains impossible.
- No service/config/deploy changes, no OpenClaw diagnosis, no push or closure commit.

Next milestone step: restore availability of the already configured embedding endpoint under operator control, then rerun Graphiti preflight. Only a full PASS releases the planned manual interactions. Do not start DM Live.
