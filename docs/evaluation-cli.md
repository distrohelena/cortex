# Evaluation CLI

The currently implemented surface is **offline**, stateless and non-persisting. It does not
connect to Docker, PostgreSQL, models, Graphiti or the conversation application.

Build:

```powershell
dotnet build src/Filomena.Evaluation.Cli -c NativeMemory
```

Send one JSON request on standard input:

```powershell
'{"contractVersion":1,"operation":"revision.hash","payload":{"contractVersion":1,"kind":"example","payload":{"a":1}}}' |
  dotnet src/Filomena.Evaluation.Cli/bin/NativeMemory/net10.0/Filomena.Evaluation.Cli.dll offline
```

Stdout is one JSON response. Exit code 0 means the requested calculation completed; 2 means
the request failed. A successful calculation is not model-quality approval or production readiness.

## Implemented operations

| Operation | Payload | Result |
|---|---|---|
| `revision.hash` | `RevisionDocument` | Canonical SHA-256 |
| `profile.validate` | `ConfigurationProfile` | Structural issues and hash when valid |
| `cost.project` | `{ usage: ProviderTokenUsage, price: PriceSnapshot }` | Nullable list-price estimate |
| `comparison.preview` | `{ candidates: string[], cells: EvaluationCell[], trials: ComparisonTrial[] }` | Descriptive summary and observed Pareto set |

Contracts live in `Filomena.Evaluation.Contracts`, `.Execution` and `.Comparison`. Profile
validation does not verify credentials, endpoint availability or remote model identity.
Comparison inputs must already refer to the same dataset, evaluation policy and paired schedule;
the preview is a calculator over caller-supplied data, **not** a trusted stored-evidence report.
No private database records are loaded or exported.

Every response includes `contractVersion`, `success`, `persisted=false`, `result` and `errorCode`.
Unknown operations/fields, duplicate JSON keys, unsupported versions and requests above 1 MiB
are rejected. Errors do not echo inputs. A role supplied in JSON grants no authority.

`experiment.run`, `deploy`, `promote`, arbitrary SQL and shell execution are not exposed.
Durable experiment/application commands remain pending the store/ledger implementation and
integration checks after the host migration. Do not use these previews as a substitute for that gate.
