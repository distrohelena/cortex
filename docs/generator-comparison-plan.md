# Generator comparison — isolated execution plan

Approved 2026-09-10. No commit/push/integration. Reuse existing experimental worktree; all previous dirty changes remain untouched.

## Frozen methodology

20 synthetic fixtures × 5 valid generations × Gemma/Luna/Terra in each of two independent phases. Phase only reviewed before pipeline. Ground truth is fixture criteria plus agent offline review (not independent human review); blind randomized cards hide model and stage. Full outputs preserved only in experimental files. No model judge. Boundary disputed distinct from pass. Severe fixture = >=2/5 clear violations, not an automatic global stop.

Gemma configured 0.3/8192; OpenAI medium/4096 from recent generation-compatible benchmark configuration; 4096 includes reasoning whereas local8192 is completion budget. No truncation coercion. Document provider differences. Same built system and named input JSON. No expanded boundary sent to generator. Generator-only bypasses enforcement solely in harness. Pipeline uses current multi-detector profile, one same-model regeneration, no technical-failure regeneration.

## Steps

- [ ] Baseline frozen suites and loaded-model read-only check.
- [ ] Create GeneratorComparisonTests.cs first: frozen input equality, raw boundary isolation, no-memory bypass, 20 fixtures, schema null for generation. Run red before helpers.
- [ ] GeneratorComparison.cs: fixture corpus, capture genuine generation request with fixed upstream and synthetic recall; no public types. Preserve full configured generation/regeneration system. Routing only in harness.
- [ ] GeneratorComparisonLiveTests.cs: opt-in only, durable attempt log, usage/status, 5 valid per cell with explicit resume after failure; stop three failures per cell. Independent phase inputs; random blind ids.
- [ ] Tests: phase-only calls one generator; pipeline allow/block/regenerate/reblock/provider error; no contamination by review expectations. Existing runtime evaluator is authority for pipeline, not ground truth.
- [ ] Run 300 only; review blind cards per fixture; persist evidence and separate rubrics. Do not infer safety from passing xUnit.
- [ ] Intermediate report; if useful proceed 300 new pipeline runs; review both candidates independently, map model afterward. No H×10 continuation.
- [ ] Aggregate counts, valid-only latency quantiles, complete remote usage/cost including failures with usage. Unknown usage never zero-imputed. Cost/final acceptable requires all quality gates, not just detector allow.
- [ ] Focused tests, whitespace, protected hashes, final report. No closure commit.

Commands: dotnet test tests/Filomena.Llm.Tests/Filomena.Llm.Tests.csproj --no-restore --filter FullyQualifiedName~GeneratorComparison; live selected by explicit environment enable plus phase/case/model. Both phases planned five valid per fixture/model. No automated semantic tuning or model change.
