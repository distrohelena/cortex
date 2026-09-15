# WhatsApp Shadow — five real manual messages, 2026-09-15

Scope: explicitly authorized five-message window, real WhatsApp inbound, frozen
brain v3, live read-only Graphiti, real Gemma/Luna, WOULD_SEND only. No DM Live.

## Evidence

- Private native operation: `openclaw-observation-ops-20260915-g`.
- Private runtime report: `filomena-native-shadow-v3/native-shadow-7686171ba8bb4150834a045320db123e.jsonl`.
- Project SHA-256 unchanged:
  `47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E`.
- Fresh transport provisioning passed with capture OFF and journal healthy.
- Native isolation used the effective snapshot proof from this process. All ten
  paths were effectively excluded; raw active/unknown evidence remained visible.
- Graphiti preflight: 36/36 checks passed; expected namespace/canonical subject,
  PRIVATE91 positive control and foreign canary exclusion passed.
- OpenObservation anchored sequence zero with zero snapshot executions.
- Five real native IDs, ordered sequences 1–5, all `fromMe=false`; five unique
  runtime execution IDs. Native legacy queue attempts were zero for every ID.
- Final controlled replay returned those same five IDs, sequences and epoch.
  Effective inspection revision remained identical through the window.
- Runner closed after exactly five executions. No continuous poller was started.
- Every outbound result was `shadowed`; runner has no transport sender.

## Results

| # | Scenario | Involvement / decision | Result | Total |
|---|---|---|---|---:|
| 1 | Direct greeting | direct rule / responder | WOULD_SEND | 10.82 s |
| 2 | Three concurrent requests in C# | direct rule / responder | WOULD_SEND | 11.92 s |
| 3 | Two deletions, one hour total; care | direct rule / responder | WOULD_SEND | 14.20 s |
| 4 | Coffee without a meeting | indirect / social opportunity / responder | WOULD_SEND | 20.03 s |
| 5 | What do you actually remember? | direct rule / responder | WOULD_SEND | 14.37 s |

Four direct paths made zero Decidir Presença calls. The indirect path used the
unchanged probabilistic decision. No participation state transition was applied;
Engajada remained hypothetical. WOULD_SEND was not inserted as sent speech.

Full candidates and model responses are retained only in the private homologation
report. Candidate 3 explicitly preserved one hour **total**, not per occurrence.
Candidate 5 recalled PRIVATE91 and the user's earlier inbound statements without
claiming that a Shadow response had been received. No clear material fidelity or
unsupported-attribution error was found in final candidates.

## Coverage and limitations

- Each interaction recalled one social fact from Graphiti.
- Each Boundary recall returned valid empty. This run did **not** exercise either
  Boundary detector; previous controlled-fixture coverage is separate.
- All five chose responder. The fourth was a silence candidate, but chose social
  engagement; no claim of live silence/migration coverage is made.
- No self event occurred. Self suppression remains locally tested, not newly
  demonstrated by a real self event in this window.
- Response 2 is a minimal SemaphoreSlim example; sharing its instance across the
  guarded requests is implicit, not explained. Brain/config were not tuned.
- No outgoing speech was delivered; conversational continuity after receiving an
  actual response remains outside this Shadow milestone.

## Latency and usage

Mean pipeline latency: 14.265 s (10.816–20.027 s). Direct mean: 12.825 s;
single non-direct interaction: 20.027 s. These are descriptive observations,
not a controlled performance comparison.

11 completion calls: 5 Gemma situation, 1 Gemma presence, 5 Luna-medium generation.
No regeneration or ABORT. Luna usage: 8,309 input tokens (5,376 cached), 557 output
tokens, 8,866 total. Monetary cost is not asserted: this run's evidence does not
include an authoritative billing rate. Graphiti embedding work is not included
in the completion-call count.

## Cleanup

Capture and isolation were explicitly disarmed after the runner exited. Journal
remained healthy; the epoch was closed with tail 5 and records preserved.
Original artifacts/config/launcher were restored using the approved rollback.
Final verification passed: 10,298 original artifact hashes (10,230 core + 68
WhatsApp), byte-exact host/config/launcher, OpenClaw 2026.8.1 / ea806575 base,
healthz=200, readyz=200 with ready=true and a healthy event-loop sample, three
original cron definitions/enabled flags restored. Capture OFF, journal healthy,
epoch closed with tail 5. The restored process is PID 39188. No memory writes,
real sends, brain modifications or push.

Conclusion: **WHATSAPP SHADOW PASS — bounded five-message window**.
This does not authorize DM Live or validate an actual sent-response feedback loop.
