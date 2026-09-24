# Generator comparison — interrupted checkpoint

## Resume recovery

After the user reloaded Gemma, the read-only preflight confirmed `google/gemma-4-e4b` as the sole loaded model. Recovered the three source files by replaying their original creation/update patches from local session history, not by changing the experiment. All three now contain zero NUL bytes; 49 focused .NET tests and eight report tests passed. The attempted archival copy of the NUL files was denied by filesystem permissions before restoration; their all-zero lengths and timestamps above remain documented. Exact cause of corruption remains unproven. Resumed fixture 6 onward using the original frozen-input fingerprint and skipping completed cells. No valid observation was rerun.

2026-09-10. No new paid requests during this resume attempt.

Confirmed from execution JSONL: Generator Only has 300 valid executions and one preserved transport failure. Independent generator + enforcement has 82 valid completed executions out of 300 planned. This is not a completed benchmark or a continuation of H×10.

Resume preflight failed: the configured LM Studio endpoint `http://127.0.0.1:1234/api/v0/models` was unavailable, including an unsandboxed read-only check. No service was started or reconfigured. Resume only missing cells after availability is restored; do not repeat valid observations.

Source integrity blocker: the three new harness source files contain only NUL bytes: GeneratorComparison.cs (11450 bytes), GeneratorComparisonLiveTests.cs (10099 bytes), GeneratorComparisonTests.cs (3109 bytes). All have modification time 2026-09-10 11:22:54 local. An interrupted formatting operation preceded discovery; causation is not proven. Do not claim the current source builds. Recover and verify these files before rebuilding or resuming. Experimental inputs, JSONL results, saved reviews and the Generator Only checkpoint remain readable. Existing compiled binaries do not substitute for recovering source integrity.

No enforcement/prompt/model changes, closing commit or push. DREAMS SHA-256 remains `65F5D3766E18BBFE68DBD3BC96E0B2235083DA29DCA5857E678506C4E63FA4D1`. Sending and H×10 remain blocked.
