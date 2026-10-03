# Damma person extraction implementation plan

Goal: replace the Corpus Index default NER with literal, segment-aware Gemma extraction through Cortex, falling back to Luna only after local failure.

Architecture: reuse IFlowCompletion, LM Studio structured decoding and OpenAI Responses. A small validated completion policy in Cortex owns one local repair and a single remote fallback. Damma owns the strict mentions contract, literal validation, deterministic UTF-16 occurrence expansion and atomic names-only replacement. Existing indexed text, segments, geometry, foundation runs and SQL schema remain unchanged.

Execution is inline, following the user's explicit implementation request.

- [ ] Add failing Cortex policy tests: local success, repair, exhausted invalid output, transport failure, invalid fallback and cancellation.
- [ ] Implement the centralized bounded policy with attempt telemetry.
- [ ] Add Damma tests for strict JSON, unknown IDs, exact OCR text, duplicates, repeated strings, UTF-16 and immutable names-only reindexing.
- [ ] Implement segment-aware extraction, bounded contextual batches and default provider wiring. Retain spaCy only as benchmark baseline.
- [ ] Add names-only CLI/API scope using stored corpus pages/segments and optimistic concurrency with atomic replacement.
- [ ] Preserve old pilot results; manually label 20–50 real segments. Compare English/Portuguese Gemma and fallback with exact span metrics.
- [ ] Reindex only the two Folha pilot pages if quality improves. Verify unchanged OCR runs, text, segments and geometry references.
- [ ] Run Cortex policy/transport and Damma regression tests; document metrics, limitations and reproducible commands.

No new schema, entity model, OCR, semantic identity resolution or full-corpus processing.
