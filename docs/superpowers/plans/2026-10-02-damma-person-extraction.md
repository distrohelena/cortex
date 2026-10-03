# Damma person extraction implementation plan

Goal: replace the Corpus Index default NER with literal, segment-aware Gemma extraction through Cortex, falling back to Luna only after local failure.

Architecture: reuse IFlowCompletion, LM Studio structured decoding and OpenAI Responses. A small validated completion policy in Cortex owns one local repair and a single remote fallback. Damma owns the strict mentions contract, literal validation, deterministic UTF-16 occurrence expansion and atomic names-only replacement. Existing indexed text, segments, geometry, foundation runs and SQL schema remain unchanged.

Execution is inline, following the user's explicit implementation request.

- [x] Add failing Cortex policy tests: local success, repair, exhausted invalid output, transport failure, invalid fallback and cancellation.
- [x] Implement the centralized bounded policy with attempt telemetry.
- [x] Add Damma tests for strict JSON, unknown IDs, exact OCR text, duplicates, repeated strings, UTF-16 and immutable names-only reindexing.
- [x] Implement segment-aware extraction, bounded contextual batches and default provider wiring. Retain spaCy only as benchmark baseline.
- [x] Add names-only CLI/API scope using stored corpus pages/segments and optimistic concurrency with atomic replacement.
- [x] Preserve old pilot results; manually label 50 real segments. Compare English/Portuguese Gemma with exact span metrics.
- [x] Reindex only the two Folha pilot pages after quality improves. Verify unchanged OCR runs, text, segments and geometry references.
- [x] Run Cortex policy/transport and Damma regression tests; document metrics, limitations and reproducible commands.
- [ ] Validate Luna against real pilot text if external egress approval is received. Automatic approval review rejected the remote benchmark; the question is pending. Controlled fallback tests pass, and the final local pilot needed no remote calls.

Result: old NER F1 75.00%, Gemma English 88.61%, Gemma Portuguese 80.52%. English is selected. 63 mentions replaced 180; 830 segments and 19 foundation processing runs are unchanged. Local success 32/32 batches after one repair. This is a calibrated pilot with documented false positives/negatives, not corpus-wide acceptance.

No new schema, entity model, OCR, semantic identity resolution or full-corpus processing.
