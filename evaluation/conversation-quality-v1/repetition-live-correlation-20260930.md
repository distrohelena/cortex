# Historical repetition episode: correction in the current inbound

Read-only correlation performed on 2026-09-30. No inference, replay, send,
Graphiti operation, configuration change or restart was performed.

## Evidence

Historical supervisor `conversation-host-running-3`, epoch
`dcf8e183-b21d-4873-bcda-53c260038985`, cursor sequences 73 and 75.
Sequence is a poll cursor, not necessarily a single-message batch identifier.
The matching persisted inbound at sequence 75 is dated
2026-09-25T18:47:12Z. Do not present this as a new current-release incident.

The current message says: `Fez a mesma piada duas vezes aqui`.
Its persisted reply quotes Filomena's previous response about deliberation,
three models and instructions from the plenary. The quoted sender is null in
this historical record; current reply/identity fixes are not evaluated here.

The recorded situation output says:
`Filomena repete piada sobre deliberação colegiada no grupo, mantendo o tom humorístico da conversa.`

Participation selects `responder`, reason `continuidade`. Generation returns:
`É, repeti mesmo. A deliberação colegiada aprovou a própria piada duas vezes. Vou encerrar a sessão antes que isso vire ata.`

The trace records provider acceptance and speech commit, not an independent
recipient confirmation. Boundary recall returned zero items; this is not a
Boundary-enforcement coverage case.

## What this establishes

- The correction was current inbound, not merely a historical message that
  could have expired under the 15-minute history rule.
- The recorded summary describes repetition as continuation of humor rather
  than preserving the corrective conversational intent.
- The candidate acknowledges repetition explicitly, yet appends another joke
  using the same premise. This is a concrete generator-quality failure, not
  evidence that it could not see any reference to repetition.
- The summary is a plausible contributing input, not a proven causal driver.
  The logs retain model responses and usage, not complete per-step requests.
  Exact selected history and full wire input cannot be certified retrospectively.

The historical situation output was recorded with the older Gemma/Jev path;
the current Luna-summary release is different. This case motivates a development
regression fixture but does not establish current online recurrence.

## Preservation and next boundary

Full selected events, input records and source-buffer SHA-256 hashes are saved
in the existing restricted maintenance directory as
`repetition-live-correlation-20260930.private.json`. Extraction helper:
`C:\Users\Helena\filomena-self-reference-tests\correlate-repetition-20260930.mjs`.
Two execution events and the bounded surrounding inbound selection were saved.
No private transport identifiers are reproduced in this report.

Next experiment, if pursued: preserve this exact correction/quote as a known
development case and distinguish summary framing from generator behavior in
separate controlled conditions. Do not expand history as a purported fix for
this case, ban all callbacks, or claim a current-release fix from old evidence.
No additional paid comparison was executed in this audit.
