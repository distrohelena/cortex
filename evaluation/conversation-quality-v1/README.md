# Conversation quality v1

This is a development-only corpus of 25 small, provider-neutral synthetic cases covering five behavior areas: recipient and self-reference; context, replies, and batch interpretation; useful participation; semantic joke repetition; and natural adult voice.

Every case is synthetic and inspired by themes in the 48-hour audit and recipient clause. None is an exact historical replay or a reconstructed transport payload. Names and message text here form illustrative fixtures; no real transport IDs are included. Contexts contain structured `history` and `currentBatch` messages with sender references and display names, plus an optional `reply` object where relevant. The representation is deliberately independent of any provider's request or response shape. A future adapter maps these cases to the inputs and outputs of the step under evaluation.

Each case has an `area`, expected constraints tagged `critical` or `quality`, optional accepted provider-neutral participation labels, expected context referents with certainty, notes, and provenance. Participation labels (`respond`, `stay_silent`) describe rubric outcomes and are not a new runtime enum, personality rule, or FSM state. Where more than one label is accepted, evaluate the stated constraints and context expectations as well.

`targetKind` marks whether a case can be compared against typed SystemOne output and full completion, or needs full completion only. The typed expectations use existing enum/boolean schema dimensions (`envolvimento`, `acao`, `continuar_acompanhando`, and, where useful, `ambiente`); adapters may map these stable meanings to their provider contract. Free-text summaries, generated prose, and voice quality are not typed SystemOne targets. All five natural-voice cases are marked `full_completion_only`, so qualitative voice is not reduced to a boolean benchmark. Full-completion constraints remain applicable when a case has both target kinds.

All cases are known to the corpus proposer. This is a development corpus, not a holdout, and it provides no independent validation. Any future reserved holdout must be separately sourced, access-controlled, and kept distinct from these cases. The corpus is not evidence that any change is effective, and it does not certify the historical incidents.

The cases express evaluation expectations only. They do not add runtime behavior, a personality rule, or FSM rule. In particular, they do not assert that the system changes batching or memory automatically. Direct questions remain response-worthy; indirect group participation may be silent when it adds no value. Context can resolve a referent, while missing context must preserve ambiguity. Semantic repetition is evaluated across distinct messages, not just exact text duplicates.

No model was called while preparing this corpus. `ConversationCaseInput` now maps its context
to situation-step inputs through the real `FlowDomain.ForModel` serialization. The mapper
does not consume `expected`, `typedSystem1Expectations`, notes or provenance as model input.
Missing quote text stays null; quoted message IDs are synthetic, never inferred from text.
All 25 cases have offline mapping coverage. This is not a model-quality or full runtime result.

`TypedRubricProjection` maps `evaluationSchema.typedSystem1.eligibleDimensions` and each case's
`typedSystem1Expectations` to deterministic criteria for the selected output fields. It reports
excluded dimensions explicitly: a situation-only run does not grade `acao` or certify participation.
Expected enum/boolean types are validated even for dimensions outside the selected step; unknown
dimensions/values fail instead of being ignored. Critical dimensions must be selected by the evaluator
policy, not inferred from prose or provided by the candidate. All 20 typed cases map; the five voice-only
cases have no typed score. This is rubric plumbing, not a provider benchmark or semantic voice review.
