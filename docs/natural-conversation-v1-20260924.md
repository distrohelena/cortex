# Natural conversation v1

Approved scope: adult everyday Portuguese, normal spelling, less rehearsed phrasing. No forced abbreviations, slang, or random typo injection. The exact instruction is in `natural-conversation-clause-v1.txt`.

Artifact: `manual-group-live-project-v3-natural-v1.json`, SHA256 `FE18A25EB48388EE65704A9D3FA9E234DB944D3A187DA6733AC703B2594DC0ED`.

Only the six group generation/regeneration prompts gain the clause. Models, personalityCore, participation, FSM, grounding, context fidelity, Boundary enforcement, memory and optional native replies are unchanged. A structural equality test removes precisely the appended clause and verifies equality with the prior artifact.

## Verification

- Artifact compilation and structural check passed.
- Checkpoint revision migration: new regression failed before adding the limited approval, then all 22 migration cases passed.
- Full local suites: Core 616 passed / 10 skipped; OpenClaw 154 passed; Llm 546 passed / 41 skipped.
- Generator-only synthetic comparison: five scenarios per variant using the existing Luna configuration and compiled output schema. Both variants returned valid text/reply objects. No transport or Graphiti was present in this comparison.
- An initial exploratory comparison omitted the output schema and was not accepted as output-contract evidence; the schema-correct comparison supersedes it.

The small sample shows fewer obligatory follow-up questions; spelling and numerical totals were preserved. Some quips still sound polished. This is a bounded instruction change, not proof that voice is universally natural. No repeated automatic tuning was performed.

## Operational migration

The continuous group runtime was stopped at its idle boundary, not killed. Native Gateway/capture/isolation were not restarted or reconfigured. The same healthy epoch, valid config revision and inactive legacy dispatch were checked before migration.

Offline transfer preserved 233 history entries, original timestamps/state/dedupe journals, and cursor sequence 34 in the same epoch. Transfer executed/sent nothing. Old runtime directory and the two original private config files were preserved. New directory: `runtime-natural-v1` under the existing private group operations directory.

Rollback before further work: idle-stop the runtime, verify no new processed events or sends, restore the two `*.before-natural` configs, and resume the original launcher. If the new run processed anything, do not roll back to the old checkpoint: reconcile/migrate the newer history and cursor first to avoid replay or loss. Never re-send an uncertain outbound.

No artificial group message was sent to test this change. Ordinary user-authorized continuous operation remains distinct from the synthetic comparison.
