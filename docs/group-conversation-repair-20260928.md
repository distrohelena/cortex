# Group conversation repair, 2026-09-28

Status: corrected release activated on 2026-09-28 as `conversation-host-running-12`. Earlier startup attempts and blockers below are retained as historical evidence. Final polling verification is recorded below.

## Evidence

Recent operator-authorized read-only inspection of private group history and runner traces found 25 SENT outcomes across 27 evaluated batches through sequence 56. Eight responses had `envolvimento=nenhum`. Development updates directed to Helena were answered by the non-direct `continuidade` branch. The generator described Filomena's proposed childhood in the third person and mixed first/third person after an explicit correction. Situation summaries sometimes described the preceding bot response rather than the new message.

The host still pins the older `manual-group-live-project-v3-natural-v1.json` artifact. Previously prepared recipient/context/contribution variants were not active. The fixed direct-involvement route is not the cause of the eight non-involved responses.

## Local changes

`SystemOneFlowCompletion.Questions` replaced all node instructions with a choice profile when configured. It now preserves task, field and description and adds `choiceGuidance` alongside them. In the inspected live configuration the profile applies to `envolvimento`, not `acao`: this defect does not independently explain over-participation. The adapter change affects configured choice profiles; no live assembly was replaced.

`evaluation/conversation-quality-v1/build-group-candidate.mjs` derives an offline candidate from the natural-v1 project. Only group situation, presence, generation and regeneration prompts change. It reuses existing recipient/context, contribution, voice and repetition clauses and adds explicit first-person self-reference and non-host behavior. DM prompts, personality, models, schemas, direct routing, FSM and delivery/enforcement configuration remain unchanged. The script verifies the prompt-only delta. The generated artifact is `docs/manual-group-live-project-v3-conversation-repair-v1.json`.

## Verification

- The choice-profile regression failed before the fix and passed afterward.
- SystemOne suite: 69 passed, 2 opt-in skipped.
- Conversation corpus/variant suite: 11 passed; new variant first failed as unsupported before implementation.
- Full Llm suite before adding the opt-in evaluation: 805 passed, 47 skipped.
- Candidate parses and compiles via real `PinnedShadowProject.FromBytes` in the evaluation harness.
- No source/static test constitutes semantic model acceptance.

The opt-in `GroupConversationRepairLiveTests` uses the real session runtime's existing participation-only harness, Jev/Gemma and the configured choice profile. Scenarios are controlled reconstructions, not native payload replay. They cover a question to another participant, a self-development update and a direct technical question in all three states. There is no generation, memory provider, observation or sender. Both arms use the corrected adapter; this does not isolate its model-quality impact against the old binary.

Real candidate evaluation encountered HTTP 503 and failed; it was not counted as silence. A diagnostic rerun stopped at `group-presence: HTTP ServiceUnavailable`. Baseline control passed its first case and then failed at `group-situation: HTTP ServiceUnavailable`. Thus no valid A/B ranking or behavior PASS is available. First cases showed relevant current-message summaries, but that is partial evidence only. Private request/result evidence remains in the existing ACL-restricted operational directory. No API keys are stored in those reports.

Generation/self-reference prose, naturalness, repetition and complete runtime composition remain unvalidated with the candidate. Next: rerun the bounded model gate when the service responds, then test generator/regenerator composition and review before controlled activation. Do not silently change model or claim the current live conversation is fixed.

### Completion attempt: provider-specific diagnosis

The subsequent controlled run passed `other-recipient` in Inactive (silent). The next `self-development-update` failed at `group-presence`. HTTP diagnostics attribute that failure specifically to Jev (503); the preceding local LM Studio completion returned 200. This narrows the provider failure but does not establish whether the underlying cause is availability, request content/size or another provider condition. Do not classify the failed decision as a participation outcome.

An additional diagnostic attempt was rejected by the execution safety review, which requires explicit approval to send the evaluation prompts to Jev. It was not executed or routed around the restriction. The opt-in harness now supports preserving a failed provider response only inside the existing private evaluation artifact, not production logs. This diagnostic enhancement has not yet captured a failing response. Live deployment remains withheld; generator/self-reference acceptance is still pending.

### Authorized follow-up results

The user then explicitly authorized transmitting candidate prompts and synthetic scenarios to Jev. That permission blocker is resolved.

- First follow-up: all three Inactive scenarios passed (two silent, direct question responding with no presence call). The first Attentive case returned HTTP 200 but was rejected by the existing client with `choice_probability_mismatch`. The raw response for that particular successful-HTTP call was not captured, so its underlying cause is not established; the contract was not relaxed.
- A subsequent bounded nine-case run again completed those three Inactive cases successfully, then stopped making progress on the next case. The evaluation child process retained an established remote HTTPS connection; no local model connection was observed at inspection. Only that owned test child was terminated, after roughly 5m33s total test duration. The test runner's resulting crash/abort is intentional diagnostic cleanup, not a discovered production crash. The interrupted request has an unknown remote completion/usage outcome and was not resubmitted. Six cases have no completed result in that run. There is no nine-case PASS.
- HTTP diagnostics now preserve per-case service calls, and Jev response bodies only in the private opt-in evaluation artifact. They do not change production logging or client validation.

`GroupConversationRepairVoiceLiveTests` evaluates four synthetic generator/regenerator cases with the configured `gpt-6-luna`/medium adapter. It constructs the existing prompt sections and uses the real compiled node response schema; it is a stage probe, not full runtime or enforcement acceptance. There is no Graphiti, sender or real conversation history.

The first four outputs passed output-contract checks but semantic review found unnecessary fiction disclaimers and external-character language in the childhood cases. A single local clause refinement now distinguishes a proposed biography from a lived memory without automatically adding ontological disclaimers or an interview. It does not authorize fabricated experiences.

The next four outputs passed output-contract checks and inline semantic review of the targeted self-reference cases:

| Case | Observed result | Review |
| --- | --- | --- |
| Own planned history | First-person `minha infancia`, no fabricated completed experience | Target met; still somewhat stylized and mentions `personagem` |
| Referent correction | Explicitly recognizes `minha historia` and the earlier third-person error | Target met; explanatory ending remains |
| Less talking | `Voce tem razao. Vou dar espaco.` | Brief acknowledgment; does not itself prove subsequent participation |
| Regeneration | First-person `minha infancia`, no automatic fiction disclaimer or closing interview | Target met |

Mean generation latency was approximately 3.66s for that second four-case sample. Eight paid generation calls were executed across both samples. Monetary cost was not captured; unknown is not zero. These are development examples, not a holdout or a claim that voice is universally solved. The first four and next four records are preserved in order in the private voice artifact; subsequent probes now include explicit run/revision metadata.

Final local candidate SHA-256: `ce356e1335eac1c149f1a2daa7a756c94777f30439e05386e5a4bb0aba03fe6c`.

Deployment remains blocked on a complete valid participation run with the approved provider. No automatic retry policy, probability normalization, fallback model or live model reassignment was introduced. A provider change is a separate decision, not a hidden workaround. There is also no new evidence of full runtime composition acceptance.

## Operational boundary

No live config, credentials, pins, checkpoints, Graphiti data, launcher or process was changed. No test message was sent. The previously authorized conversation service was left running. No commit or push was made. Existing unrelated worktree changes were preserved.

## Completion follow-up (supersedes earlier pending status)

- The final nine-case participation run completed with valid HTTP 200/provider contracts. All three direct questions responded with the deterministic direct route. All six uninvited scenarios avoided response; one Attentive case chose `esperar`, not `calar`. The original strict rubric therefore reported 8/9. Review accepted waiting as non-response, and three offline full-graph tests proved that this waiting route does not reach generation. No failed provider request was counted as a behavioral success.
- The exact compiled release passed four offline and four real-model synthetic full-runtime cases: two invitations produced WOULD_SEND, two uninvited messages produced no send. Replaying each input produced no new execution. Shadow speech was not committed as delivered history. Graphiti was an explicitly valid-empty fixture in these smoke tests, not live memory coverage. Real-model case latencies were 14.996, 12.191, 11.749 and 11.299 seconds. Monetary usage was not captured.
- Frozen operational source plus the bounded patches passed 645 Core tests (10 skipped) and 744 Llm tests (49 skipped). Builds had no warnings/errors. Main's separate Llm suite passed 809 (49 skipped); main Core still has 19 failures in unrelated, previously modified flow/compiler validation work (710 passed, 10 skipped). The release deliberately does not include that unrelated work; the entire main solution is not claimed green.
- Deployment preserves the previously approved App, Memory and OpenClaw binaries. Only Core (exact prompt-revision checkpoint transfer) and Llm (choice instruction preservation) assemblies and symbols changed. The project artifact is the CE356E... revision above. Original artifacts/configuration remain available for rollback, with a private hash manifest.
- The old supervisor was gracefully stopped and its integrated memory worker drained. Migration first correctly refused a stale checkpoint: inbox watermark 525 preceded one admitted input at 526 that ended in `execution_failed` when the situation provider was unavailable. The outbound journal was byte-identical to the checkpoint fingerprint. Audit proved no transition or generation/send nodes for that failed execution.
- A narrowly pinned offline reconciliation validated the old checkpoint against its exact inbox prefix, retained the terminal failure and added its inbound to context/deduplication without re-executing it. Original journals/checkpoint were unchanged. Seven malformed-tail cases were rejected by the utility guard test. A fresh private checkpoint was validated by the existing real hydration validator before revision transfer. This is not a general automatic failed-message retry policy.
- History, participation state, deduplication and the native cursor were transferred; no historical message became a new execution. The new supervisor is `conversation-host-running-9`, using the approved launcher and integrated memory configuration. No synthetic WhatsApp message was sent. No commit or push was made.

The repair is bounded acceptance, not proof that all future phrasing, repetition or participation will be correct. Earlier Jev availability/contract failures remain recorded. Memory backfill remains partial and independently observable; starting the worker does not mean all retained messages have been extracted into knowledge.

### Final activation checks and remaining external-data blocker

The preserved App binary exposed an ABI mismatch before executing its group worker: its 16-argument `ObservedLiveRun` constructor was absent after recompiling Core, whose newer source adds optional participation review. A regression reproduced the missing signature; an explicit compatibility overload forwards to the newer implementation with participation review disabled. No authority or behavior was broadened. The operational Core suite now passes **646 tests, 10 skipped**. The release Core hash manifest was updated. Run 9 was gracefully stopped; its group worker never reached transport preflight or processed messages.

A direct preflight-only command (`/quit`, no Poll) then identified a stale native producer instance revision. Read-only revalidation proved healthy capture/journal/config/dispatch; only the producer instance identifier changed, while config content, registry and isolation identifiers remained identical. An explicit operator revalidation wrote a new private capability artifact and retained the old configs. This is not automatic authorization refresh in the runtime.

The final compiled App/Core composition passes transport preflight. Graphiti resolves the canonical participant and validates response/namespace structure, but refuses startup with `expected_canary_not_found`. Independent read-only checks establish:

- `PRIVATE91` and `PLEX73` are present in their expected control namespaces.
- `DRIFT42` is absent from active recall.
- The exact original DRIFT42 relation still exists in the correct group namespace, but now has `expired_at=2026-09-28T13:49:42.925856+00:00` and `invalid_at=2026-09-25T19:09:39+00:00`.
- Its source is the historical human message declaring that test code. This relation was NOT among the twelve records expired by the earlier authorized maintenance script. The cause/validity of this newer invalidation is not established; it must not be blindly undone.

No canary, Graphiti fact or preflight rule was changed to force acceptance. No post-repair group message was sent. The conversation supervisor and integrated memory worker are stopped, preserving backlog (last observed 58 pending) and rollback artifacts. Releasing the group requires an approved technical-canary repair/reprovisioning or another evidence-backed correction of this data condition, then all normal preflights. Existing human facts must not be resurrected merely to obtain PASS.

### Final activation after explicit operator authorization

The operator subsequently authorized repair of the technical isolation control. A new `DRIFT42` control was provisioned in the exact group namespace, using separate `OperationalControl` entities and an explicitly host-maintenance source episode. The original expired human-attributed fact and all personal facts remained unchanged. The new fact states that it is a technical control, not a person's statement/preference/memory. Private pre-change evidence, exact created UUIDs, verification and scoped rollback instructions are retained under the release's `technical-canary-repair` directory.

The new record's initial text-only insertion was insufficient for the production hybrid recall. Its indexing was completed with the existing local `text-embedding-nomic-embed-text-v2-moe` deployment (one 768-dimensional embedding call), native endpoint fields and canonical UTC date serialization. No synthetic embedding was invented, no model generated a personal memory, and no preflight filter or namespace validation was relaxed. Real recall then found DRIFT42, PRIVATE91 and PLEX73 in their respective expected namespaces; all configured negative-control checks passed.

The exact deployment package subsequently completed transport preflight and all ten Graphiti probe configurations with no failed checks, opened the existing stream, hydrated the preserved checkpoint and completed a preflight-only run with **zero executions and zero send attempts**. The full authorized conversation supervisor was then started as `conversation-host-running-10` using the same package and normal startup guards. It independently passed its preflights and reached `run_started`. Integrated memory ingestion is running; at the first readiness check, 407 of 431 discovered records were persisted, 24 remained pending, and coverage was still explicitly partial. This is not a claim that every historical message or every extracted fact is covered/correct.

No synthetic test message was sent to WhatsApp. Normal replies to legitimate new group inputs remain enabled under the existing authorization. DMs, models, FSM and delivery permissions were not broadened by this repair. The group-only prompt improvements remain bounded tested behavior, not a guarantee against every future stylistic or participation mistake.

### First-poll blocker and bounded protocol repair

Run 10 passed startup but its first Poll encountered native sequence 62: WAProto stub type 2, no content, and the exact provider diagnostic `No session found to decrypt message`. This is not proof of human text, successful reading or model failure. The raw record remains in the native admission journal and a private diagnostic copy. Run 10 was drained without a send.

The reader now recognizes only that exact unavailable-ciphertext notification with matching native message ID and conversation binding. It produces no text or execution and preserves `ciphertext_unavailable_requires_manual_reconciliation` as the durable inbox suppression reason. Unknown diagnostics/stubs and mismatched identity still fail closed. This does **not** recover the lost plaintext or implement automatic retry if decryption later becomes possible; the retained item requires explicit reconciliation. A regression first reproduced the rejection, then proved durable disposition and subsequent valid-text admission. All **179 OpenClaw tests passed**. The exact real batch (59–62) then mapped without errors in a read-only diagnostic; no cursor was advanced by that diagnostic.

The release now also includes the tested OpenClaw adapter DLL/PDB, with original hashes retained in the manifest. App and Memory binaries remain unchanged. The final compiled offline smoke passed all four cases with zero transport/model/Graphiti calls.

Run 11 was stopped after fail-closed detection of another native producer-instance revision change. An explicit repeat revalidation proved the other config/registry/isolation revision components unchanged and all seven provisioning checks green. New approval artifacts and prior configs were retained separately; no runtime auto-approval was introduced. Run 12 was then started normally. Integrated memory reports 431 discovered/persisted, pending 0, but still partial coverage because non-text and unavailable retained payloads are not covered.

### Verified release

Final read-only audit of run 12 confirmed both supervisor and .NET worker alive, no terminal marker, the CE356E... project revision, transport PASS, all ten Graphiti probe configurations with zero failed checks, and **18 completed polling cycles with zero ABORT/FAIL records**. The durable cursor advanced from 58 to 62. The first batch recorded sequence 59 as duplicate/no execution, the two reactions as ignored/no execution, and the ciphertext failure as ignored with the explicit manual-reconciliation diagnostic. Subsequent polls were healthy and empty. No old message was executed or resent during release.

The group is released for authorized normal operation with the corrected configuration and integrated memory worker. No synthetic group message was used to validate this activation, so a new post-deployment human-facing reply is not claimed as observed here. Backup ACL grants access only to Helena and SYSTEM. No commit or push was performed.
