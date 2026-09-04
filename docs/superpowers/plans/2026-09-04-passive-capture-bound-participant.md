# Passive Capture and Bound Participant Implementation Plan

1. Add failing TypeScript tests for deterministic inbound episode mapping,
   WhatsApp sender normalization, UUIDv5 stability, reply metadata, and
   `message_received`-only hook registration.
2. Implement a focused passive-capture module and replace the `agent_end`
   handler. Extend the Graphiti HTTP client request contract.
3. Add failing Python tests for generic bound entities, canonical UUID handling,
   alias union, speaker-only normalization, edge endpoint normalization, and
   fail-closed persistence.
4. Implement the small generic `BoundEntity` extension in vendored Graphiti and
   carry its contract through the MCP queue, including deterministic episode
   UUID and original reference time.
5. Add queue/idempotency and concurrency tests. Ensure raw episodes survive a
   binding violation while entity facts do not.
6. Add FalkorDB integration coverage for the acceptance matrix and execute it
   against the runtime stack without reimporting historical exports.
7. Add and run a read-only legacy Person audit.
8. Run the plugin suite, Graphiti suite, typechecks, `git diff --check`, and
   inspect the final diff. Commit on `main`; do not push.
