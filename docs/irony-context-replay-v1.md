# Irony/context replay V1: experimental, not deployed

## Question and approved scope

Does later, actually observed conversation change the interpretation of the four
reported examples: Megabrain/Copythief, “você é woke”, “Moggei você”, and “Agora com
1000”? Understanding a message later does not itself authorize a late reply.

This is a development replay, not a new brain node, participant profile, memory
writer or response scheduler. Filomena remains offline. The separate memory-only
backfill is unaffected.

## Corpus

All four target messages were found in the retained, exact-account/group native
history through the read-only source adapter. The private pinned corpus contains
eight earlier and eight later retained human text messages per target. Earlier and
later are strictly separated by provider timestamp. Same-timestamp neighbors, if
present, are omitted rather than inventing order. Observed provider quotes remain
quotes; their content is not asserted as the current speaker's statement.

Corpus SHA256:
`63a0ddfa60434b37a7a0a9b6e44efc33021b55d67f58b22c332d18bae39f8476`.

The corpus is stored outside git in the restricted host operations directory,
with access limited to Helena and SYSTEM. Speaker/message references are opaque,
but message text is still private and can contain personal information. Do not
describe it as anonymized or publish the surrounding conversation in this report.

Known coverage limitations: deleted payloads, unsupported/non-text content,
account-sent messages excluded by the memory history source, and bounded context.
Missing Filomena replies may materially limit interpretation. The replay declares
these gaps to the analyzer; it does not reconstruct omitted messages.

## Experiment

Two independent reads per target, eight calls total:

1. Initial: earlier context and the target, with no later-message leakage.
2. Reassessment: identical earlier context and target plus actual later messages.

The experimental analyzer returns reading, an optional alternative, qualitative
confidence, visible evidence IDs, a short explanation, and a diagnostic assessment
of whether responding remains relevant. Confidence is not a calibrated percentage.
No conversational response, permanent personality inference or runtime execution
is produced. A response-opportunity label is not a participation decision.

The planned model is GPT-6 Luna medium using the existing `OpenAiFlowCompletion`
client. No local GPU inference is added while memory extraction is running. API
usage and latency are recorded; no unverified price is inferred.

Files:

- `tests/Filomena.Llm.Tests/IronyReplayExperimentTests.cs`: test-only input projection,
  schema, prompt and evidence validation.
- `tests/Filomena.Llm.Tests/IronyReplayExperimentLiveTests.cs`: explicit opt-in model
  experiment, private result files, no runtime/transport/Graphiti dependency.

Local test gate: five tests first failed against unimplemented helpers, then
passed. Full Llm/App test project: 612 passed, 44 skipped, zero failed. Skipped
tests include the replay's explicit external-model opt-in.

## Current result

**Model comparison NOT RUN.** The tool authorization review rejected transmission
of private historical excerpts to the external model API because that payload and
destination were not explicitly approved. Zero replay model calls occurred. No
semantic accuracy, improvement, latency or cost result is claimed.

The next step requires explicit approval for those eight external analyses, or a
separately chosen local-model experiment. Do not work around the rejection by
indirectly invoking the same API. Do not activate this analyzer in the live brain
or infer that the reconsideration queue has been implemented.
