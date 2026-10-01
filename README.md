# Cortex

Shared .NET foundation for typed flows, sessions, host authority, inference,
memory/transport adapters and the evaluation workbench. Filomena is a consumer
in the independent sibling Filo repository; it is not required to build Cortex.

## Local development

- Solution: `Cortex.slnx`.
- Projects: `src/Cortex.*`; reusable editor: `packages/flow-graph`.
- `dotnet build Cortex.slnx`
- Offline test selection:
  `dotnet test Cortex.slnx --filter "Category!=Postgres&Category!=LocalSocket&FullyQualifiedName!~DatabaseLifecycleTests"`
- `node --test tests/architecture/*.test.mjs packages/flow-graph/tests/*.test.mjs`

Database/live acceptance is separate. Do not enable services or inference merely
to build. Use an available local NuGet cache for offline restore.

## Repository separation

See [split status](docs/repository-split-status.md), the approved
[plan](docs/superpowers/plans/2026-10-01-cortex-filo-repository-split.md) and
[ownership manifest](docs/repository-split-manifest.json).

The current directory remains `C:/dev/filomena` temporarily because the active
launcher uses it. The destination is `C:/dev/cortex`. Preserved operational
files on disk are not part of this repository's current tracked application.
Historical commits still contain the original monorepo.

No private conversations, credentials, database files, runtime state or DREAMS
may be committed. Existing historical test identifiers require a separate
history review before any public publication.
