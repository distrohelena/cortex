# State-context audit: ongoing naturalness goal

## Status and decision

Investigation in progress. No improvement is declared validated and the goal is
not complete. No new model calls, production changes, memory access or activation.
The next diagnostic separates generation style from participation-state guidance,
not another naturalness clause. Personality remains unchanged.

## Verified composition

`src/Filomena.Core/Flows/InstructionContext.cs::Compose` prepends personality,
state guidance and interests only when their existing configuration flags enable
them. It then appends the node prompt. `TypedFlowRunner.cs` uses this composition;
`FlowInstructionInspection.Describe` exposes the same composition for inspection.
`InstructionContextTests` contains explicit-context, revision-pinning and malformed
configuration tests. These tests were inspected, not newly executed in this audit.

The local `group-luna-summary-v1.project.json` SHA-256 equals the saved experiment's
baseline hash:
`0F6247929215FDB20F470DB6122EBC23FDE1BD863A436E2019A78669B3EB385E`.
All three states enable `useStateGuidance` on DM/group generation and regeneration.
This is configuration, not proof of an engine defect.

The 16 A requests saved in `voice-direct-20260930/offline-results.json` all have
one identical composed system prompt, with Engaged guidance. All eight cases have
direct involvement. The presence fixture has zero history items while its system
says that Filomena already participates. This is an experimental setup limitation,
not evidence that live sessions are always incorrectly Engaged.

The guidance says not to monopolize space and allows receding to Attentive. The
generator separately states that participation has already been decided and state
guidance only contextualizes prior participation. Despite that qualification, it
still receives the policy prose. Whether this influences self-explanatory output
requires a controlled model comparison. Lexical/theme similarity alone is not
causal evidence.

## Avoid repeating failed experiments

`unforced-voice-review.md` already records V1/V2 instruction additions, V3 style
consolidation and V4 illustrative dialogues. None established a general voice fix.
`two-hour-quality-20260930.md` found no clear benefit from removing the text/JSON
contradiction alone. `summary-intent-baseline-20260930.md` does not justify changing
the current summarizer. `voice-direct-ab-20260930.md` found no benefit from another
replacement voice clause. These are distinct experiments; their rates must not be
pooled as though corpus, configuration and review were identical.

## Diagnostic control prepared offline

`C:\Users\Helena\filomena-self-reference-tests\voice-direct-20260930\audit-state-context.mjs`
reads the saved requests and creates `state-context-diagnostic.json`.

Eight pairs preserve personality, node prompt, facts/Boundary instructions, model,
schema and all inputs exactly. The diagnostic arm removes only the state-guidance
section, equivalent in composition intent to the existing `useStateGuidance=false`
flag. Assertions reconstruct the original request byte-for-byte at string level
and deep-compare all remaining fields. This is preparation, not model validation
or a deployable candidate. The script has no provider client or activation path.

Executed `node audit-state-context.mjs`: eight pairs prepared, zero network calls;
all baseline requests Engaged; presence history count zero; one baseline system.

Do not remove personality or change participation decisions in this experiment.
Do not simultaneously fix the output-contract contradiction: it would confound the
state-guidance comparison. Do not claim a reduction in excessive participation
from conditional generation; that requires separate participation evidence.

## Remaining work toward the goal

1. Prepare a local project-level toggle using the existing flag, verify all-state
   generation/regeneration composition and unchanged participation/routing fields.
2. Add contrasts for presence with empty history, established ongoing exchange,
   requested reason for silence with/without evidence, and explicit opt-out. These
   must not all be forced to Engaged and must distinguish generation from selection.
3. Validate behavior with authorized inference only. The previous 32-call budget
   is consumed, not renewable by changing the experiment name. Static checks cannot
   demonstrate naturalness or preservation of humor/flirt.
4. If supported, validate runtime composition and regeneration before recommending
   any activation. Keep the online baseline unchanged throughout.

No independent review was performed. Codebase-memory returned `Transport closed`;
the bounded composition source was inspected directly. No exhaustive architecture
or caller-coverage claim follows from this audit.

## Local project composition follow-up

Implemented `state-guidance-variant.mjs` as an offline transform using the existing
flag. It changes only six group generation/regeneration flags in three known
states. DM, personality, prompts, schemas, models, participation nodes and all
edges remain equal. Missing/duplicate targets, wrong node kinds, already-disabled
flags and an unreviewed state set are rejected.

Test-first run: six assertions failed against the identity transform. After the
implementation, all six passed; the existing variant suite also passed: 22 total.
This proves transform behavior, not generated voice.

The isolated `state-guidance-20260930/Probe.csproj` references the existing deployed
assemblies without starting the host. All 12 compiled comparisons passed: six group
generation/regeneration systems differ solely by the expected state section, six
DM systems are unchanged, schemas and input names remain equal. The first sandbox
compile could not resolve the protected DLL path; the read-authorized offline run
then compiled and passed. No runtime service or model client is constructed.

`state-guidance-20260930/context-contrasts.json` records six additional contrasts:
presence across all states, established exchange, supported/unsupported silence
explanation, uninvited operator update and explicit opt-out. The last two concern
participation and must not be scored by forcing generator output. These are
prepared fixtures, not observed model results. The original eight cases retain
humor/flirt, correction, identity, technical detail, factual totals and vent coverage.

Still unresolved: whether state-guidance removal improves naturalness, preserves
behavior across states, or influences needless participation at all. No causal or
activation claim is supported yet. New paid inference needs a new bounded approval;
the previously approved 32 calls were all consumed. Local composition validation
does not renew that authority and cannot replace behavioral evidence.
