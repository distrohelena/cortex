# Cortex

**Dynamic graph engineering for configurable, stateful AI systems.**

Cortex is a .NET foundation for designing, validating, and running graphs that
bring together typed data, control flow, state, memory, and model inference.
Its focus is giving developers room to shape how a system works: which steps
run, what information they exchange, how decisions choose a path, and how
execution changes with the context of a session.

The graph makes those choices explicit. Nodes define operations, typed ports
describe their inputs and outputs, and separate data and control connections
describe how information and execution move through the system. Project
revisions, session state, validation, and execution traces give that flexibility
a structure that can be inspected and tested.

Cortex is built for experimentation with control over the details. Developers
can compose model calls with deterministic processing, select providers and
model settings, define structured outputs, connect memory adapters, and
configure validation and fallback policies. The evaluation workbench supports
comparing those choices against explicit cases and criteria.

## What you can shape

- **Graph structure:** compose typed inputs, transformations, model steps,
  branches, and state transitions with explicit data and execution paths.
- **Session behavior:** carry context and state across executions, work with
  project revisions, and inspect the steps taken within a session.
- **Inference:** choose local or remote providers, configure model profiles,
  prompts and output schemas, and use bounded repair and fallback policies.
- **Memory and effects:** supply adapters and host-owned permissions for
  reading memory, invoking models, and interacting with external systems.
- **Evaluation:** define cases, compare candidate configurations, and inspect
  measurements and execution evidence before adopting a change.

Configuration is part of the engineering surface. Types, schemas, revisions,
and host authority make the relationships between those options explicit.

## Building blocks

| Module | Responsibility |
| --- | --- |
| [`Cortex.Core`](src/Cortex.Core) | Graph contracts, compilation, typed execution, sessions, and host authority. |
| [`Cortex.Inference`](src/Cortex.Inference) | Model provider adapters, structured completion, and validated fallback. |
| [`Cortex.Memory`](src/Cortex.Memory) | Memory read adapters and response validation. |
| [`Cortex.OpenClaw`](src/Cortex.OpenClaw) | Optional transport and host integration. |
| [`Cortex.Evaluation`](src/Cortex.Evaluation) | Evaluation contracts, experiments, comparison, and persistence. |
| [`Cortex.Evaluation.Cli`](src/Cortex.Evaluation.Cli) | Command-line access to the evaluation workbench. |
| [`flow-graph`](packages/flow-graph) | Reusable TypeScript editor for nodes, ports, and data/control connections. |

The editor exposes the graph visually; the host supplies its node definitions,
validation, persistence, and execution. Applications compose the Cortex modules
they need. Filomena is one consumer, maintained in the independent sibling
Filo repository.

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

The directory is now `C:/dev/cortex`. The authorized physical rename completed
on 2026-10-01 after a graceful host drain. Preserved operational
files on disk are not part of this repository's current tracked application.
Historical commits still contain the original monorepo.

No private conversations, credentials, database files, runtime state or DREAMS
may be committed. Existing historical test identifiers require a separate
history review before any public publication.
