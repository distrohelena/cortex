# Offline runtime history and projection audit

## Scope

Follow-up to `repetition-request-audit-20260929.md`. Executed the deployed
`SessionFlowRuntime` with a recording `IFlowCompletion`, synthetic messages,
controlled UTC clock and the repository's `typed-flow-slice2.json` fixture.
No network adapter, sender, memory reader or production configuration is used.

The fixture's accumulator is configured to close at one message. History uses
before-current-batch semantics. Its prompt records the selected batch/history
and returns a fixed silence decision. Thus selection and model-facing projection
are real; participation judgments and generation quality are not evaluated.
This does not execute the complete operational Filomena graph or prove reply
projection, delivered-character history, or all operational context equivalence.

## Execution

Harness: `C:\Users\Helena\filomena-self-reference-tests\runtime-history-audit`.
Command: `dotnet run --project` with its `Audit.csproj`.
References deployed DLLs from `context-repair-release-20260928/app`.
Result: exit 0; all six cases passed. Full synthetic requests are saved in
`bin/Debug/net10.0/results.json` beneath the harness directory.

| Case | Selection settings / elapsed | Observed history |
| --- | --- | --- |
| Group recent | 20 messages, 5,000 chars, 15 minutes / 5 minutes | Correction and subsequent message retained |
| Group expired | Same / 16 minutes | Correction excluded |
| DM without age cutoff | 20 messages, 5,000 chars / 60 minutes | Correction retained |
| Count boundary | Limit 2, three prior messages | Two newest retained |
| Character boundary | Limit 10, texts of 6 and 7 characters | Newest text retained |
| Oversized newest | Limit 10, newest prior text exceeds limit | Empty history, even though an older short text fits |

Every case additionally asserts current-batch text is present, the current
message is absent from historical selection, raw sender identity is not exposed,
the same sender has a consistent opaque reference across current/history inputs,
and completion count equals admitted messages. These are synthetic identity
checks, not a live participant-binding acceptance test.

The oversized-newest result follows the existing stop-at-character-limit
selection rule. It is a characterized edge case, not a newly introduced change
or a demonstrated cause of the reported live behavior.

## Interpretation

There is now execution evidence, rather than source inspection alone, that a
group correction can expire before reaching a prompt. There is also evidence
that a recent correction survives both real selection and projection in this
fixture. No general adapter truncation or sender-reference mismatch was found
within the tested cases.

The previous generator-only repetition failures had the correction explicitly
present. History expiry cannot explain those failures. Keep these two hypotheses
separate: absent context in some real executions versus repeated jokes despite
supplied context in controlled generation.

Before changing group retention, correlate a specific real incident with the
selected history for that execution. Before attributing repetition to a prompt
clause, use a separately versioned bounded comparison. Do not remove grounding,
identity or factual-fidelity protections on the basis of this audit.

## Effects and limits

Model calls: 0. Network calls: 0. Sender calls: 0. Graphiti operations: 0.
Production changes: 0. No paid experiment or online promotion.
Only a disposable local harness and this report were added.
The codebase graph connection returned `Transport closed`; targeted source and
existing fixture inspection were used instead, without exhaustive graph claims.
