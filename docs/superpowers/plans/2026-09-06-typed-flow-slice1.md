# Typed flow slice 1 implementation plan

**Goal:** Execute fixture message → lazy property → typed instruction → switch locally with validated flat structured output.
**Architecture:** Adapt playground graph v3 to existing Core FlowType/GraphDocument/FlowNode contracts. A bounded compiler/runtime handles only entry, property, prompt and switch; old DM executor stays unchanged. Editor owns dynamic contracts; generic canvas owns no domain behavior.
**Stack:** Installed .NET 10 / C#, browser ES modules, existing TypeScript canvas; xUnit and node:test.
**Spec:** User-approved design and implementation acceptance criteria in this conversation, 2026-09-06.

## Constraints

No Graphiti, transport, operational FSM, real batching, send, join/parallel, or personality changes. No Gateway process changes or DREAMS edits. No implicit conversion or partial structured outputs. Stable opaque port IDs. Save incomplete drafts without deleting invalid wires. Existing dirty work must not be swept into commit.

## Steps

- [x] Core contract/compiler: existing FlowType algebra and GraphDocument; validate ports, types, cycles, single control successor and producer dominance through lazy dependencies. Node/port issues.
- [x] Runtime: execution-local values, lazy projection, awaited instructions, exact Switch; atomic flat output validation and bounded retry. Fake tests cover all three branches and failure paths.
- [x] LLM boundary: generic structured completion, configured model, real response_format, HTTP-fake verification. No real model called.
- [x] Editor: v3 dynamic contracts, stable port/case IDs, invalid wires preserved, panel and preview.
- [x] Local API: typed validate/fake run and draft store, save/restart/load tests.
- [x] Verification executed: scoped tests/build/format pass. Full solution tests/format expose pre-existing failures documented in docs/typed-flow-slice1.md; not claimed green.
- [x] Selective staged candidate verified independently of unrelated worktree changes. Scope/build/format/fake tests pass; pre-existing global failures reproduced in baseline and documented in docs/typed-flow-commit-audit.md. Ready for the authorized single local commit; no push.

## Acceptance fixtures

Input `{texto:"oi",remetente:{nome:"Helena",stableId:"fixture:helena"},canal:{nome:"Teste",nativeId:"fixture:dm"},reply:null}`.
Property texto outputs StringType; remetente outputs NullableType(Person); array projection preserves two nullable slots.
Fake instruction returns `{acao:"responder",proximo_estado:"engajada",motivo:"foi chamada"}`; only matching control branch executes and reason remains a typed string input. Repeat calar/esperar. Invalid first output followed by valid output performs exactly one correction. Two invalid outputs publish nothing and end failed.

## Commit boundary

Repository begins with substantial uncommitted playground implementation and unrelated conversation/transport changes. Preserve all. Use explicit paths and inspect staged diff; never add workspace/DREAMS.md, runtime state or saved project documents.
