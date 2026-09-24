# GPT-6 Luna operational revision

Requested target: GPT-6 Luna in Filomena, not the coding agent.
Official model ID confirmed from https://developers.openai.com/api/docs/models/gpt-6-luna
on 2026-09-22. Public search returned no result; direct official-page retrieval
succeeded. No prices were changed or inferred from the user's half-price comment.

New artifact: `manual-live-project-v3-gpt6-luna.json`.
SHA256: `7789735AC766B4FA38A8C1749679E1352BBA8B8F9760D3FA9189F076F89BDB39`.
Source: `manual-live-project-v3-dm-continuity.json`, preserved unchanged.
Only 24 OpenAI model fields changed from `gpt-5.6-luna` to `gpt-6-luna` across
generation, regeneration and detector nodes. Prompts, reasoning effort, token
limits, local Gemma models, graphs, FSM, memory and output schemas are unchanged.
The Responses client is unchanged; it already forwards explicit model/effort and
does not send temperature/top_p. No new model capabilities were assumed.

The project compilation/scope test first failed because the target was missing,
then passed. Exact checkpoint migration first rejected the new revision, then
passed after allowing only this immutable source/target pair. This prepares
history-preserving migration; no private checkpoint was actually transferred.

Future controlled provisioning uses `--one-gpt6-luna-response` in
`diagnostics/prepare-live-one-response.mjs`; prior flags/artifacts remain available
for rollback. The new flag retains maximum one response and exact hash checking.
No active config, Gateway or operational window was changed/opened this round.

Verification: solution tests passed (Core 592, OpenClaw 113, Llm 387; total 1,092
passed / 47 skipped / zero failures), including compilation and model-only scope.
Native preparation script syntax and touched-file whitespace checks passed.
No live API/model invocation, Graphiti operation or transport send was performed.
Account access, API compatibility and behavioral quality on GPT-6 Luna still need
a bounded no-send smoke before the next real response; local tests do not prove
those live properties. Do not silently fall back to GPT-5.6 Luna on API failure.

## Controlled validation and window N

Two tiny GPT-6 Luna Responses calls completed with the current account: text
output (17 input / 6 output tokens) and strict JSON schema output (49 input /
52 output tokens). The first PowerShell request was rejected as `invalid_json`
because the shell sent non-UTF-8 text; explicit UTF-8 bytes fixed the probe.
No WhatsApp send resulted from these probes.

The live role gate contained a separate explicit `gpt-5.6-luna` allowlist.
Window N's first runtime launch failed before execution with
`This pilot permits only Luna medium remotely.` A focused test reproduced this.
The gate now accepts only GPT-5.6 Luna or GPT-6 Luna at medium effort and still
rejects Astra. Temporary local exception diagnostics were removed. The exact
M→N checkpoint import also required rebuilding the already modified import tool;
the first attempt rejected before copying, then imported 16 history entries,
Engajada, 0 executions/sends.

Window N private rollback root: `openclaw-observation-ops-20260922-live-n`.
Stage verified 10,230 original core and 68 WhatsApp files plus 12,039 next core
and 68 next WhatsApp files. Active config/launcher matched M's restored hashes.
The trusted native LID and host authority matched the previously validated M;
rollback validation passed. Gateway PID 29732. Maintenance session 7870 proved
startup capture OFF / journal healthy, then armed exact isolation and capture;
Dispatch Inspection INACTIVE, legacy recovery 2 quarantined and 0 active owners.
Epoch `41980310-53a5-4d40-9257-b83240b20ddf`.

N route preparation found one exact route of five candidates, max one response,
4,000 character ceiling, zero send calls. GPT-6 artifact hash exact. Transport
and Graphiti read-only preflights passed; social memory present, Boundaries valid
empty, isolation canaries passed. Runtime session 11735, run
`23a1087f10bf49a0b477ad3b4c217a10`, 16 restored history entries, cursor 0,
snapshot 0 executions. It is waiting for one NEW manual DM; no automatic poll.
After user confirmation, issue exactly one `/poll` to runtime 11735. Then close
maintenance 7870, disarm and rollback **N**, verify 10,298 originals and service
health/ready. Do not replay M's processed message.

Post gate fix full solution run with existing binaries: Core 592 passed / 10
skipped; OpenClaw 113 passed; Llm 388 passed / 37 skipped. Total 1,093 passed /
47 skipped / zero failures. An initial full build attempt failed because the
running App DLL was locked; it made no code change. The App build and focused
gate test had already succeeded before runtime start. `--no-build` completed the
suite without disrupting the active one-response process.

Window N outcome (2026-09-22): after Helena's new manual DM, exactly one native
event (sequence 1, fromMe=false) was admitted and one runtime execution ran.
The trace records one send-boundary attempt: `sent` / `provider_accepted`, with
an attested receipt account. Generation used `gpt-6-luna`; the existing local
Gemma situation call remained in the flow. Runtime closed after one execution.
Helena subsequently confirmed that the response arrived in the DM. This is
human delivery confirmation in addition to the provider-accepted trace.
Maintenance cleanup reported capture `off`, journal `healthy`, no cleanup
failure. Controlled rollback restored 10,298 original hashes and byte-exact
configs, OpenClaw 2026.8.1. Post-start `/healthz` and `/readyz` returned 200,
ready=true, degraded=false; control status showed capture `off` and journal
`healthy`. No second poll or resend was performed.
