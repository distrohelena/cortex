# MCP extraction text rejection — 2026-09-29

> Follow-up: the maintenance was subsequently authorized and completed. The
> corrected image is active; all six allowlisted episodes completed processing
> and their receipts were reconciled through the normal service as persisted.
> Original UUID, namespace, name and raw content were checked. The historical
> sections below describe the earlier diagnostic state, not current operation.
> After separate authorization, isolation/capture were restored and run 22
> resumed with preserved context. See `post-memory-resume-review-20260929.md`
> for the subsequent read-only operational sample. Full maintenance evidence
> remains in the restricted host maintenance directory.

## Root cause reproduced

FastMCP `FuncMetadata.pre_parse_json` decodes JSON-looking strings when the
argument annotation is not exactly `str`. `add_memory.extraction_body` was
annotated `str | None`; the conversation-evidence:v2 JSON text became a Python
dictionary before Pydantic validation. Pydantic then rejected it with
`extraction_body / string_type` before `add_memory` or its processing queue ran.

The adapter discarded the plain-text MCP validation message and exposed only
`MCP tool returned an error`. This is distinct from HTTP rejection and from a
failure after Graphiti accepted work.

All six currently pending payloads were tested through the installed FastMCP
argument parser, offline with network disabled:

| Masked receipt | Current backend | Corrected backend |
| --- | --- | --- |
| 7f6caa0f4d18 | extraction_body/string_type | valid, exact text |
| d830ea3aca33 | extraction_body/string_type | valid, exact text |
| 7fea223e2f2f | extraction_body/string_type | valid, exact text |
| 0e72c78d892c | extraction_body/string_type | valid, exact text |
| b84858aadd31 | extraction_body/string_type | valid, exact text |
| 8546f4eb4d26 | extraction_body/string_type | valid, exact text |

Original historical exceptions were not retained; this demonstrates the failure
of those payloads under the installed contract, not recovered historical logs.

## Minimal fix

`graphiti_mcp_server.py` uses a nullable-text PlainValidator with an underlying
`str` annotation at this MCP boundary. It retains the public string/null schema,
allows omission and null, rejects actual dict/list/number/bool input, and prevents
FastMCP from changing JSON text. No JSON reserialization, prompt change, namespace
change, permissive fallback or memory rewrite is involved.

The TypeScript adapter additionally classifies this known MCP validation envelope
as `argument_validation`; private `input_value` text is not copied to durable
diagnostic fields. Retry authority remains unchanged.

## Idempotency scope

The installed queue, Graphiti core and bulk persistence files matched local hashes.
Offline tests exercised the actual queue and `Graphiti.add_episode` with external
model/database boundaries replaced:

- Same UUID admitted while the first attempt is in flight: one extraction/write.
- Failure before persistence followed by explicit queued retry: one final write.
- Counterexample, direct concurrent invocation without queue: two writes.

This is a bounded single-queue-owner property, not a universal exactly-once claim.
Existing episode presence still proves raw storage, not complete fact extraction.
FalkorDB `execute_write` is not a multi-statement database transaction.

## Live diagnostic attempts and preservation

One explicit attempt each was made for the first three masked receipts above.
They did not produce an accepted-for-processing result. The third retained the
raw MCP response privately and proved argument rejection before the queue. The
first two retained only the generic adapter error; no success was inferred.
No automatic repeat was performed. All six receipts remain unresolved.

A SQLite backup and write-ahead attempt files exist in the host's restricted
`native-memory/reconciliation-six-20260929` directory (Helena and SYSTEM ACL).
No receipt was reset to safe/queued or manually marked persisted. Normal audit
documents contain neither conversation content nor secrets.

## Verification

- Plugin suite: **306 passed, 15 skipped**; typecheck passed.
- Offline Graphiti runtime suite: **13 passed**, including the nullable MCP
  contract and queue characterization tests. Synthetic failure logs are expected.
- Six real extraction payloads: **6 rejected before / 6 valid with exact text after**.
- Targeted `git diff --check`: passed.
- No production container restart, deployment, memory deletion, WhatsApp send,
  brain execution or model invocation was initiated by these diagnostic tests.

## Deployment boundary / next operational step

`runtime/Dockerfile.graphiti-extraction-text` layers only the corrected server
file over the captured active image. Keep the original image for application
rollback; this does not reverse any subsequently accepted memory writes.

Built candidate: `filomena-graphiti-mcp:extraction-text-candidate-20260929`,
manifest `sha256:2c49d49654c5569a8eb5fa88ddb55c693a2b17f901df928d8c55ddd0bb7fd567`.
The same 13 offline tests also passed against the built image without a server
source bind mount. The active image remains
`sha256:1e0c3806d18879b492c743e1c388d5758c9ea37c5d14382e2d23b985e1e06347`.
The first build invocation incorrectly used a bare image ID as `FROM`; BuildKit
attempted a registry metadata lookup and failed. Building by the verified local
tag succeeded. No image was deployed by either command.

The correction is not yet active. Before replacement, establish a controlled
writer/drain window: current `get_status` does not expose queue/in-flight work,
and other stored queued receipts exist outside the six-item scope. Do not treat
their age, a healthy HTTP endpoint or silent logs as proof of an empty queue.
Do not restart the backend merely to discard unknown work.

After deployment, verify the live MCP schema and corrected validation, then
reconcile the exact allowlisted receipts using normal host binding/authorization,
same payload/UUID, bounded audited attempts and actual persistence confirmation.
Do not restore an old receipt backup over new work or infer extraction success
solely from raw episode presence.

### Deployment-window audit (follow-up)

Read-only enumeration of both configured bindings confirmed:

- Target binding: six failed receipts, no corresponding episodes.
- Other binding: all 35 queued receipts already have exactly matching stored
  episode identity, namespace, name and content; zero provenance mismatches.
  Their receipts were not modified by this audit.
- The conversation supervisor remains running with six pending memory items.
- The referenced plugin configuration has `autoCapture=true`. Its real
  `message_received` and `message_sent` handlers call the lifecycle recorder,
  independently of the coupled maintenance worker. Stopping only that worker
  cannot establish a writer-free window.
- The existing Gateway shutdown procedure is broader than a Graphiti-only
  restart. Restart also invalidates process-owned observation authority; the
  current resume helper requires the same epoch and must not be used blindly
  after a Gateway restart. Fresh isolation/capture/revision checks and a safe
  cursor handoff are required before resuming conversational delivery.

Approval was requested for a temporary Filomena/Gateway maintenance window.
No process was stopped, configuration changed, receipt reset or memory resubmitted
during this follow-up audit. Do not equate the 35 confirmed episodes with proof
that the backend's entire volatile queue is empty.
