# Group memory preflight — 2026-09-24

Scope: Alunos de Drift do Fiuk, read-only Graphiti checks; no group activation.

## Preparation

- New ignored host artifacts under `state/group-memory-preflight-20260924/`.
- Explicit private ACL: current Windows user + SYSTEM; inheritance disabled.
- Five observed canonical PN participants; ten reciprocal PN/LID aliases.
- Native account/group routing cross-checked against local session metadata.
- Group operational project SHA256 unchanged:
  `A3B066C5B05B3D28E1B429CE0D900C38C8FF82D892429C3D2E42A4E2B15AAF97`.
- Ten probes: DRIFT42 positive; PRIVATE91 and PLEX73 foreign controls for each actor.
- These are **memory-preflight documents**, not an approved live native window:
  no native revision, epoch, reader token or capture authority is minted. They must
  not replace current DM/Jev operational configuration.

Ruling: reuse `--manual-shadow` only with both `--preflight-only` and a separate
host-owned `--group-participants` file. A group without preflight-only is rejected
before network. All group binding/role checks use the existing native validators.
No new runtime, sender, model, or Graphiti semantics. Red/green test reproduced the
old premature DM-only rejection; the contrasting execution attempt stays rejected.

## Live result: FAIL, canonical participant evidence incomplete

The first participant (the trusted Helena DM peer, verified by identity equality)
passed both complete read-only preflights: canonical identity,
exact/semantic contract, namespace, positive and negative canaries, social recall
with memory, Boundary recall as valid empty, and no outage-to-empty fallback.
The next participant failed `canonical_subject_unproven`. Execution stopped at zero.

A separate bounded diagnostic performed five exact `get_entity_neighborhood` reads
and preserved raw envelopes only in the private directory. Ordinals refer to the
sorted host participant list, not names inferred from content.

| Participant ordinal | Exact result | Interpretation |
|---|---|---|
| 0 | 3 facts; matching UUID, stable_id and Person labels | Canonical proof present; both full probes PASS |
| 1 | successful `facts: []` | Existing node, but response omits identity |
| 2 | body `error`: node not found | Expected canonical UUID unavailable in this namespace |
| 3 | body `error`: node not found | Expected canonical UUID unavailable in this namespace |
| 4 | successful `facts: []` | Existing node, but response omits identity |

The server returns `isError=false` even for the two error bodies. No adapter was
changed to accept them; they are failures, not empty memory.

## Cause verified in actual running backend source

Read-only inspection of `/app/mcp/src/graphiti_mcp_server.py`, function
`get_entity_neighborhood` (around line 590), in the running Graphiti container:

1. Clones driver for the requested group.
2. Loads `EntityNode.get_by_uuid` and verifies `entity.group_id`.
3. Retrieves/filters active edges.
4. When no edges remain, returns only `FactSearchResponse(message=..., facts=[])`.

Thus ordinals 1 and 4 are not evidence of missing nodes or cross-namespace leakage;
the API omits canonical metadata needed by the preflight when their neighborhood is
empty. For ordinals 2 and 3, node lookup itself returns not-found. We have NOT proved
those people are absent under every legacy ID, only that their expected canonical
UUIDs are missing in this group's namespace.

No confirmed memory leakage observed. Full five-participant readiness is NOT PASS.

## Required next scope

1. Add a read-only exact identity proof (UUID, stable_id, labels, namespace) independent
   of whether a person has memory facts. Preserve strict provenance validation; do
   not infer canonical identity from successful empty recall alone.
2. Resolve/provision the two missing canonical identities through the existing trusted
   deterministic registration mechanism, after checking legacy mappings. This may
   require **live Graphiti writes**, which were not authorized or performed here.
3. Rerun all participant preflights. Never create fake facts or Boundaries for coverage.

Alternatively, a separately agreed narrower pilot could include only the proven
participant; this was **not silently applied** to the five-participant scope.

## Evidence and safety

- Initial full LLM/host regression: 476 passed, 39 live tests skipped.
- Final local regressions after diagnostics: LLM/host 476 passed, 40 opt-in tests
  skipped; OpenClaw adapter 124 passed. Diff check and preparation-script syntax pass.
- Raw diagnostic: one explicitly opted-in test passed **as data capture**, not as
  Graphiti readiness. No other live tests enabled.
- Group and original DM project hashes unchanged.
- Self-review: new group branch cannot reach model creation; preflight-only returns
  before model credentials/providers. Existing DM validation retained. No independent
  review session launched.
- No active deploy/restart, capture arm, Observation Open/Poll, Filomena model call,
  runtime execution, WOULD_SEND, Graphiti write, or WhatsApp outbound.
- Graphiti's existing semantic reads use its ordinary embedding backend; no Luna/Jev
  generation was invoked by this preflight.

## Authorized identity correction and final preflight (2026-09-24)

This continuation supersedes the readiness blocker above, not the historical evidence.
Helena explicitly authorized the read-only API correction and provisioning only missing
canonical identities, after checking existing records.

- Exact neighborhood now returns `entity: {uuid, group_id, labels, stable_id}` from
  the loaded, namespace-checked node, whether facts are empty or nonempty. Semantic
  recall and facts are unchanged. No identity is inferred from empty recall.
- The .NET probe validates the explicit entity's UUID, actor, Person label and
  required namespace. An invalid explicit identity cannot fall back to other facts.
  Legacy fact endpoint evidence remains supported with its existing validation.
- Read-only audit found 212 nodes in the approved group: three selected canonical
  people already existed; two expected UUIDs and stable IDs did not. Trusted PN/LID
  alias variants did not match legacy identity metadata. No name-based merging.
- Exactly two `Entity:Person` identity nodes were created by a guarded atomic query.
  Existing nodes were untouched; no edges, episodes, facts, summaries of experience,
  or Boundaries were created. The empty summary is a storage field, not memory.
- The receipt writer initially referenced an unsupported `QueryResult.statistics`
  attribute AFTER successful creation. We did not retry the write. The pre-write
  intent was preserved; a separate read-only reconciliation verified both nodes.
  The operator script was corrected and its empty failed receipt retained as evidence.

Final evidence: private `manual-shadow-a5461eb7943e40c9b597b058643e635a.jsonl`.
Ten probes (five participants, two foreign controls each), **180 checks PASS**, zero
failed checks, final `PREFLIGHT_PASS`, executions=0. DRIFT42 positive and PRIVATE91 /
PLEX73 exclusion controls passed for every participant. Boundary recall was valid
empty in all ten probes. Social recall found memory for one participant (two probes),
and returned valid empty for the other four. This is not full membership coverage.

### Verification

- Regression RED observed: exact empty neighborhood omitted identity.
- Regression RED observed: .NET accepted explicit entity without namespace.
- .NET LLM/host suite: **481 passed, 40 opt-in skipped, zero failed**.
- Python neighborhood/social-memory focused suite: **15 passed**.
- Identity-plan tests: **3 passed**; disposable network-isolated FalkorDB test:
  **1 passed**, proving create-only behavior, no duplicate/rewrite, all-or-none
  conflict rejection and zero edges.
- App build: zero warnings/errors. Focused Ruff and diff check passed.
- Full upstream MCP suite was interrupted before results because it includes live
  integration tests outside this approved scope; no full-upstream-suite PASS claimed.
- No OpenClaw restart, capture arm, Observation operation, sender, runtime execution,
  candidate generation, Luna/Jev call, WOULD_SEND or outbound in this slice. Semantic
  read-only Graphiti queries used the existing embedding backend.

### Narrow deploy and rollback

Only two files in `filomena-graphiti-mcp` were replaced and that container restarted:
`/app/mcp/src/graphiti_mcp_server.py` and `/app/mcp/src/models/response_types.py`.
Originals are in the ACL-restricted private evidence directory as
`graphiti_mcp_server.pre-change.py` and `response_types.pre-change.py`.
Original image: `sha256:53e0ef206586350730380d36ddf45dd1c35e5398c0278a591d93d82cd6fd86f0`,
preserved as `filomena-graphiti-mcp:identity-proof-rollback-20260924`.

An offline two-file overlay image was built using `runtime/Dockerfile.graphiti-identity-proof`
and tagged `filomena-graphiti-mcp:identity-proof-20260924` and `:local`, so a future
recreation using the local image retains the fix. No dependency/config/env changes.
The running container retains its original image ID with the two verified file updates;
the new image and running files have identical SHA256s:

- Server: `800f4e7f3e8bc415bb678a69f8ce3fd48a06530ff159e541961678636a838f3a`
- Types: `50ce3728b0a3c0450832f720b8f05dc986c2cc247d12a6b021d57c183034b865`

Code rollback: copy the two preserved originals to their original container paths,
restore the `:local` tag from the rollback image, restart only this Graphiti container,
verify health. Final observed health was Docker `healthy`, HTTP `/health` 200.
Identity rollback must be separately reviewed: use the private intent's exact UUIDs,
verify each node still has its original identity and no incident edges, then remove
ONLY those two newly created nodes if needed. Never delete connected nodes or use
`DETACH DELETE` to force rollback. No rollback was needed or executed.

Group memory preflight is now PASS. This does not enable group capture or sending;
the next scope is the bounded group's transport/operational handoff.
