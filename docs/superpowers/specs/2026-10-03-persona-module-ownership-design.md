# Optional persona module: corrected ownership and offline relocation

Date: 2026-10-03. Status: design and implementation plan approved by Helena; relocation implemented and verified offline.

Plan: [Optional Cortex.Personas relocation](../plans/2026-10-03-persona-module-ownership.md).

Acceptance: [Relocation report](../../personas-relocation-report.md). No production attachment or activation is implied.

## Intent and authority

Helena assigned this agent to Filomena/Filo and Cortex. The other agent owns Damma and document analysis. Personality is an Actors domain; Damma may use shared infrastructure but does not own persona formation.

Continue the approved evidence-based persona work without new writes to Damma, without losing the accepted offline behavior, and without activating Filomena. The previous slice is an implementation baseline, not permission to implement a persona database or connect the conversation host.

This is a small architectural relocation: move ownership of an existing optional library and its tests, not redesign the engine. Written approval precedes an implementation plan; this document alone does not authorize product-code changes.

## Observed repository state

- Damma source baseline: local commit `7d3053c`, adding `personas/` and three documents. `f8f4768` preserves the storage proposal with an explicit ownership correction. Both are historical sources, not new Damma roadmap items.
- Cortex inspected HEAD: `bf62b11`, including a newer inference change by another workflow. Preserve it; do not pin or reset to this agent's earlier `ca1dd36` baseline.
- Cortex working tree: existing untracked `.tmp/`; leave it untouched.
- Filo inspected HEAD: `8d2eaed`. Operator feedback is modified and many evaluation artifacts plus a memory-isolation plan are untracked. Preserve all of them; this slice does not edit those paths.
- `Cortex.slnx` uses explicit `src/Cortex.*` and `tests/Cortex.*` project entries. `Cortex.Core.csproj` has no project references.
- Filo's production projects reference Core, Inference, Memory, OpenClaw, and Filo's Llm. They do not reference the new persona library.
- `tests/architecture/cortex-foundation.test.mjs` checks project ownership, lack of application dependencies, and the frozen compatibility artifacts. The split manifest describes a historical checkpoint; do not rewrite it as a current-file inventory.

Cortex and Filo are not listed in the available codebase-memory index. The statements above come from bounded direct-source inspection, not an exhaustive graph audit. No tests or operational actions were executed for this design.

## Selected boundary and alternatives

Recommend `Cortex.Personas`, a separate optional library in the Cortex repository. The generic evidence/revision/context machinery can serve multiple characters; its placement does not make persona behavior mandatory in `Cortex.Core` or in Damma.

| Option | Consequence |
| --- | --- |
| Separate `Cortex.Personas` module — recommended | Reuses the foundation repository and naming/build conventions; Core stays independent, and consumers opt in explicitly. |
| Put the library inside Filo | Avoids extending the foundation repository but gives generic character machinery application ownership and creates a later extraction problem. |
| Create an Actors repository now | Clear domain ownership, but adds repository/dependency management without a second implemented actor consumer. Defer. |

Filo owns the actual Filomena persona revision, corpus, authored decisions, experience sources, and application-specific composition. Cortex owns reusable contracts and pure compilation. Neither location gives models new authority.

## Exact relocation scope

Proposed destination:

```text
Cortex/
  src/Cortex.Personas/             pure library, namespace Cortex.Personas
  tests/Cortex.Personas.Tests/     synthetic tests, namespace Cortex.Personas.Tests
  Personas.slnx                   delimited offline build/test entrypoint
  docs/personas-offline.md        usage, evidence and limits
```

The implementation will port the seven existing source files: `PersonaEvidence.cs`, `BehaviorPattern.cs`, `PersonaSpec.cs`, `PersonaRevisionCodec.cs`, `PersonaSituation.cs`, `PersonaContextCompiler.cs`, and the library project. Preserve the eight test/fixture source files and test project, renaming namespaces and adjusting project references only where relocation requires it.

The library remains `net10.0`, nullable, warnings-as-errors, with no package, database, Core, Filo, or Damma project dependency. The test project references the optional library and the existing local `Cortex.Core`; keep the previously accepted test package versions. Use Cortex's existing SDK pin rather than another repository-level override.

Add the library/test projects to `Cortex.slnx` using the established naming convention and provide `Personas.slnx` for targeted acceptance. Do not add a dependency from Core or Filo's production projects. No new service, provider, endpoint, background worker, or authorization registry is introduced.

Import from the named committed Damma tree read-only. Record source commit/path provenance in a relocation report; do not cherry-pick an entire Damma commit into Cortex. Do not copy `.git`, build outputs, local execution caches, private sources, credentials, live state, or unrelated documentary code.

Old Damma code remains inert until its owner coordinates cleanup. This is not permission to delete/revert it or rewrite either repository's history. State clearly that the port is the continuation of one slice, not two independently evolving implementations.

## Preserved behavioral contracts

- Keep JSON contract version 1, field names, enum names, strict schema admission, exact source hashing, and offset semantics.
- Keep immutable snapshots, scope checks, contextual matching, unknown-condition/exception behavior, origin deduplication, and local supersession validation.
- Keep observation separate from interpretation and generated draft separate from observed experience.
- Namespace/assembly renaming must not silently alter encoded bundle bytes. Freeze synthetic source-baseline artifact hashes before the port and compare the relocated codec's output against them.
- Keep projection data separate from host System/profile settings. Existing grant revocation, wrong-scope, failure/cancellation, and invalid-output behavior remains covered through the actual Cortex authority and a fake adapter.
- Never interpret a successful fake response as personality fidelity, live memory coverage, or production integration.

No persona retriever, extractor, consolidation algorithm, database adapter, feedback UI, generator change, or runtime attachment is part of this relocation. Existing prompts, model assignments, FSM, memory isolation work, transport, and outbound behavior remain untouched.

## Acceptance and review

After implementation is approved, run the full relocated 84-case persona suite, its 12 controlled smoke cases, and the delimited Cortex authority/context regressions. Inspect targets and adapters before execution; use only offline fixtures and local dependency cache.

Also verify build with zero warnings, scoped formatting, the existing Cortex architecture checks, and an explicit dependency test proving Core/Filo production projects do not reference `Cortex.Personas` and the optional library references no other project. Database/model/sender activity is forbidden.

Compare source/test changes against the committed baseline: only namespace, project/path, usage documentation, and explicit packaging/architecture-test changes are expected. Any behavioral difference is a failed relocation, not an opportunity to tune personality.

Self-review will check ownership, dependency direction, hashes, preserved negative tests, future optional loading, and absence of operational wiring. No independent review or subagent is authorized by this document. No push or automatic commit of implementation is implied.

## What follows this slice

Once ownership is corrected and the offline baseline is accepted in Cortex, the next useful formation experiment is source passages to candidate behavioral evidence, with a small fictional/authorized corpus, attribution/provenance checks, and explicit model/usage authorization if inference is needed. It can produce local candidate artifacts without first deploying a persona database; it must not silently publish a current personality.

Durable revision publication, autonomous consolidation, and authorial feedback remain later separately scoped work. The superseded proposal for a Damma-owned persona database must not be implemented. A future persistence choice belongs to the Actors/Filo deployment boundary and should reuse infrastructure only after ownership and authorization are explicit.
