# System 1 support and participation pilot

Status: transport/typed adapter implemented locally; **do not replace live participation models**.
No active Filomena deployment, transport, Graphiti, response generation or outbound was used.

Subsequent hosted Jev results: [Jev participation pilot](jev-participation-experiment-2026-09-23.md).
Latest: [version-bound Jev compatibility and 36-run holdout](jev-holdout-and-compatibility-2026-09-23.md).

## Scope and implementation

- `SystemOneClient`: host-owned endpoint/credential; Jev/Laya `/v1/systemone` protocol.
  Supports choice, score and noul requests. Validates answer IDs/types, option membership,
  distributions, score scale/legend, confidence and usage. Errors remain failures; no fallback.
- `SystemOneFlowCompletion`: existing `IFlowCompletion` boundary; closed required enum/boolean
  schemas only. Enum -> choice; boolean -> noul with an explicitly supplied threshold.
  Experimental threshold: 0.5, ties true. Full probabilities/usage remain available to the observer.
  Free-text generation, implicit numerical scales and ambiguous schemas are rejected before network.
  Explicit Laya model names prevent its server's unknown-name auto-routing in this adapter.
- `--system-one`: standalone development command; returns typed answers and does not construct
  the conversational host. Endpoint and credentials come only from the host environment.
- Participation variants are test-harness projections, never changes to the source project.
  Real compiler, batching, direct-involvement rule and FSM are exercised.

Protocol sources: [TypeSafe API](https://docs.typesafe.ai/api) and the local
[Laya source](https://github.com/NandhaKishorM/laya), revision
`d120d4ba220711b93c171973118753460310e16b`.
The requested codebase-memory graph was consulted; relevant source was checked because its
coverage metadata indicated changes since indexing. Filomena was not indexed and was inspected directly.

## Run a typed decision

From the isolated Filomena checkout, with a Laya server listening locally:

```powershell
$env:FILOMENA_LAYA_ENDPOINT = 'http://127.0.0.1:18081/v1/systemone'
dotnet run --project src/Filomena.App -c Release -- --system-one --provider laya --model multilingual --input docs/systemone-example.json
```

For Jev use `--provider jev --model jev-latest` and supply `TYPESAFE_API_KEY` securely in
the host environment. Default endpoint is `https://api.typesafe.ai/v1/systemone`;
optional override is `FILOMENA_JEV_ENDPOINT`. Do not put credentials in project JSON or command arguments.
Jev live inference was **not executed**; no TypeSafe credential was available in this process.

## Local Laya environment

Dedicated environment: `%USERPROFILE%\.cache\filomena-systemone\venv`.
Reference checkout remains `C:\dev\ref\laya`; no source edits there.
CPU-only torch 2.14.0+cpu, transformers 5.17.0, huggingface_hub 1.32.0,
fastapi 0.141.1, uvicorn 0.53.0. `pip check` passed.
Checkpoint: multilingual, Hub snapshot `5e7b2b1b8ca22ecdd3f2322d94069c9b6ce7e844b`.

The first startup failed creating Windows cache symlinks. The supported
`HF_HUB_DISABLE_SYMLINKS=1` process option resolved that without changing Windows privileges.
Port 8000 was occupied; the experiment used 18081 without touching its owner.

Reproduce startup in a dedicated terminal; stop it with Ctrl+C after testing:

```powershell
$env:PYTHONPATH = 'C:\dev\ref\laya'
$env:LAYA_HOST = '127.0.0.1'
$env:LAYA_PORT = '18081'
$env:LAYA_DEVICE = 'cpu'
$env:LAYA_MODELS = 'multilingual'
$env:LAYA_THREADS = '4'
$env:LAYA_PRELOAD = '1'
$env:HF_HUB_DISABLE_SYMLINKS = '1'
$env:HF_HUB_DISABLE_IMPLICIT_TOKEN = '1'
& "$env:USERPROFILE\.cache\filomena-systemone\venv\Scripts\python.exe" -m laya.serve
```

## Participation experiment

Source artifact: `docs/manual-live-project-v3-gpt6-luna.json`, SHA256
`7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.
Despite the artifact name, its two participation stages still use local
`google/gemma-4-e4b`; Luna generation was not called.
Each projected graph is independently hashed in the result records.

Nine fixtures, one repetition per variant (27 total): direct Inativa/Atenta/Engajada,
indirect continuation, migration, banal exchange, interesting-but-not-involved,
technology word in a banal exchange, explicit opt-out (exploratory).

| Variant | Accepted outcomes | Mean decision time | Situation mean | Presence mean* | Calls |
|---|---:|---:|---:|---:|---:|
| Existing Gemma baseline | 9/9 | 8.443 s | 6.071 s | 2.371 s | 14 Gemma |
| Laya situation + Gemma presence | 6/9 | 4.748 s | 2.164 s | 2.584 s | 9 Laya + 5 Gemma |
| Laya situation + Laya presence | 3/9 | 2.752 s | 2.013 s | 0.738 s | 14 Laya |

*Means include zero for bypassed presence calls. CPU Laya vs the existing baseline service;
not a controlled hardware benchmark. Single samples, not statistically established accuracy.
An accepted outcome includes the expected involvement/FSM behavior, not merely a plausible action.

Observed failures:

- Situation-only: direct Inativa missed and became silence; direct Engajada classified non-direct
  though presence eventually answered; migration incorrectly classified direct and forced a response.
- Both: the same involvement problems, plus replies to banal, interesting-but-not-involved
  and technology-banal conversations.
- The existing direct rule behaved correctly given its input; the experiment did not change it.
- All executions completed once, with no generation/outbound path visited.

### Important confounds and limitations

Laya cannot produce the situation's free-text `resumo`. The experimental projection removes it
and its presence input/edge explicitly, rather than fabricating text or calling a second model.
The original v3 graph and its live memory-query use of that summary remain untouched.
This pilot therefore does **not** prove drop-in end-to-end compatibility with generation or recall.

Laya `common.build_sequence` also clips instruction tokens to its head budget (default 192,
shared with options) and clips state to the checkpoint's context budget. The adapter places
field and description before the full task, but the long original prompt is not guaranteed to survive.
These are results of this schema-to-question translation, **not proof that Laya is intrinsically
incapable of the task**. A next isolated experiment should use short, explicit per-field question
definitions and measure token coverage before judging replacement quality.

There was no provider API bill for this local run. Laya reported 16,585 input tokens for
situation-only and 22,711 for both; 0 generated output tokens. Its usage sums tokenized questions,
so it is not directly comparable to chat-model token billing. Gemma usage/cost was not instrumented
by this harness; local compute/electricity cost was not measured. No hosted cost estimate invented.

Evidence: `TestResults/systemone/20260923-153549-b03248263f284bcaad81ca1bf0d854e4/`.
The opt-in experiment test passes on execution/contract integrity and records quality as `pass`
per case; a green test run does not mean the candidate model passed the quality gate.

```powershell
$env:FILOMENA_SYSTEMONE_EXPERIMENT = '1'
$env:FILOMENA_SYSTEMONE_PROVIDER = 'laya'
$env:FILOMENA_SYSTEMONE_MODEL = 'multilingual'
$env:FILOMENA_SYSTEMONE_ENDPOINT = 'http://127.0.0.1:18081/v1/systemone'
$env:FILOMENA_SYSTEMONE_VARIANTS = 'baseline,situation,both'
$env:FILOMENA_SYSTEMONE_REPETITIONS = '1'
dotnet test tests/Filomena.Llm.Tests -c Release --filter FullyQualifiedName~SystemOneParticipationLiveTests
```

For routine regression tests, remove `FILOMENA_SYSTEMONE_EXPERIMENT` from the shell environment.
Do not build the same test output directory concurrently with a running experiment on Windows.

## Decision

Keep both live participation stages unchanged. Keep System 1 support opt-in for typed decisions
and further bounded evaluation. No live model replacement, memory change or transport deployment.

## Verification and final environment

- Full `dotnet test Filomena.slnx -c Release --no-restore`: 1,128 passed, 48 opt-in/skipped,
  zero failures (Core 595; LLM 416; OpenClaw adapter 117).
- 28 new offline tests cover wire validation, errors, typed mapping, command and real runtime routing.
- Release solution build: zero warnings/errors. Diff whitespace check clean.
- Standalone command reached local Laya and returned probabilities/usage. Its separate short
  English example incorrectly chose `none` for a direct call; this smoke proves plumbing, not quality.
- A concurrent Debug build initially hit the running experiment's Windows executable lock.
  Verification was moved to separate Release output; final full suite/build were clean.
- Experiment-owned Laya process was stopped; port 18081 has no listener. The venv and downloaded
  checkpoint remain for reproducibility. The pre-existing LM Studio service/model was left intact.
- No Jev API call, Graphiti call, conversational generation, WhatsApp call or outbound was made.
- Prior unfinished OpenClaw group work was not modified or retested by this slice.
