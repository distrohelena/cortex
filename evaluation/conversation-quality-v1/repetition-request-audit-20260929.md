# Repetition request audit: offline adapter and runtime boundaries

## Outcome

The deployed adapter preserves all 84 saved synthetic requests. There is no
evidence that adapter serialization removed the supplied history or correction.
This does not establish what a model attended to, nor identify the cause of
repetition. No online configuration was changed or variant promoted.

## Evidence and reproducibility

Inputs: `repetition-baseline-20260929.md` (12 requests),
`repetition-ab-20260929.md` (36), and `repetition-ac-20260929.md` (36).
Their private JSONL artifacts retain full requests and candidates.

Offline harness: `C:\Users\Helena\filomena-self-reference-tests\repetition-wire-audit`.
Command executed: `dotnet run --project` against its `Audit.csproj`.
The harness references the deployed assemblies and runs `OpenAiFlowCompletion`
with an in-memory `HttpMessageHandler`, not a socket handler. Its synthetic
response tests serialization/parsing only; it is not an inference result.

Restricted artifact: `repetition-wire-audit.private.json` in the existing
`memory-extraction-maintenance-20260929` operational evidence directory.
It contains source hashes and per-request checks. This is a reconstruction
through the deployed adapter, not historical HTTP packet capture.

| Check | Result |
| --- | --- |
| Exact serialized input preserved | 84/84 |
| Exact system prefix and composed output contract preserved | 84/84 |
| Output schema preserved | 84/84 |
| Network requests / model calls during audit | 0 / 0 |
| Production changes | 0 |

## Prompt findings

All 84 composed prompts contain both `Retorne apenas texto, não JSON.` and,
later, `Retorne o objeto JSON com texto e replyTo.` The adapter additionally
supplies the structured output schema. The previous trials returned valid
structured outputs; the contradiction is real instruction debt, not a proven
cause of semantic repetition.

| Variant | Requests | System characters | Whitespace-delimited words |
| --- | ---: | ---: | ---: |
| A | 48 | 11,904 | 1,778 |
| B | 18 | 12,744 | 1,905 |
| C | 18 | 12,348 | 1,845 |

These counts precede the appended output contract and are not token counts.
There are overlapping style instructions. Length alone does not demonstrate
attention failure or justify removing grounding, identity or fidelity controls.

## Synthetic versus real generation

The experiments deliberately invoke generation directly, with Engajada state
guidance and the instruction that participation has already been decided.
They do not execute situation reading, participation decisions or the FSM.
One repetition fixture can reasonably require silence in the real flow;
forcing generation cannot measure that participation behavior.

The harness manually constructs model-facing messages, uses fixture references,
a fixed situation summary, and empty social memory/Boundaries. It bypasses
`FlowDomain.Check` / `FlowDomain.ForModel` and runtime history selection.
Sender names, the supplied history and repetition feedback survive the adapter,
but full runtime equivalence has not been established.

The four repetition fixtures have at most three historical messages and 150
historical text characters. Their fixed summary about a joke already appearing
may itself influence generation; no causal comparison of that summary was run.

The repeated-punchline failures remain evidence of **conditional generator
behavior under these fixtures**. They are not evidence of the online rate of
unnecessary participation or of missing live history.

## Source boundaries inspected

- `src/Filomena.Core/Flows/InstructionContext.cs`, `Compose`: composes persona,
  state guidance, interests and the node prompt.
- `src/Filomena.Llm/FlowInstructionPrompt.cs`, `Build`: appends output contract
  and optional format correction.
- `src/Filomena.Llm/OpenAi/OpenAiFlowCompletion.cs`, `CompleteAsync`: sends the
  serialized input and strict response schema; no input truncation was found.
- `src/Filomena.Core/Flows/FlowDomain.cs`, `Check` / `ForModel`: validates and
  projects runtime inputs, including opaque identity references.
- `src/Filomena.Core/Flows/TypedFlowRunner.cs`: applies that projection and
  prompt composition before completion; validates returned output.
- `src/Filomena.Core/Flows/Sessions/SessionFlowRuntime.Execution.cs`,
  `SelectHistory`: selects bounded history preceding the current batch.

In the inspected pinned operational project, group history has limits of 20
messages, 5,000 characters and 15 minutes; DM history has the same count/size
limits without the time cutoff. Confirmed character speech is eligible.
Consequently an older correction can be absent from a live group request.
That possibility was not established as the cause of a specific live incident
and cannot explain synthetic failures where the correction is already present.

No new live request was captured in this audit. Structural graph tooling was
unavailable; source inspection is not a claim of exhaustive graph coverage.

## Next bounded step

Before another paid comparison, exercise the real runtime projection and
history selection with an offline recording completion, preserving fixtures
and comparing the actual generated request. Separate participation outcomes
from generation quality. Treat removal of the contradictory output instruction
as a distinct local candidate, not an online fix or a proven repetition remedy.

No checker, model change, Graphiti write, send, deployment or automatic
promotion was introduced. Prior A/B and A/C results remain unchanged.
