using System.Text.Json;
using Filomena.Core.Boundaries;
using Filomena.Core.Memory;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class MultiDetectorLive
{
    [Fact]
    public async Task Luna_composition_corpus_then_hardset()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_MULTI_LIVE") != "1", "Opt-in synthetic Luna benchmark.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_MULTI_ROOT") ?? throw new InvalidOperationException("Root required");
        var token = TestContext.Current.CancellationToken;
        const string scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
        const string source = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.";
        var registry = new BoundaryExecutionRegistry(scope);
        var memory = new MemoryItem("synthetic-multi", scope, source, MemoryCategory.Boundary,
            [MemoryIdentity.Person(scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact");
        var values = await registry.CompileAsync(registry.RegisterMemories([memory]), new("v1", "v1", "v1"), DateTimeOffset.UtcNow, token);
        var boundary = registry.ResolveBoundaries(values, DateTimeOffset.UtcNow)[0];
        var safe = registry.ForModel(values, DateTimeOffset.UtcNow, new Dictionary<string, string>())[0];
        var template = new NoTeasingTopicDetectionV2();
        DetectorDefinition[] definitions = [
            new(template.SemanticVersion, template.SemanticContractDefinition, template.SemanticSchema, features =>
            {
                var decision = template.Evaluate(boundary, features, DateTimeOffset.UtcNow);
                var outcome = decision.DiagnosticCode is "invalid_boundary" or "invalid_semantic_features"
                    ? DetectionOutcome.EnforcementFailure : decision.Allow ? DetectionOutcome.Allow : DetectionOutcome.Block;
                return new(outcome, decision.DiagnosticCode, outcome == DetectionOutcome.Block ? template.BuildRegenerationFeedback(features, decision) : "");
            }, "openai", "gpt-5.6-luna", "medium", 1024),
            new("no_teasing_about_topic:implicit-depreciation-v1", DetectionStrategies.ComplementPrompt, DetectionStrategies.ComplementSchema,
                features => features.EnumerateObject().All(p => p.Value.GetString() == "nao")
                    ? new(DetectionOutcome.Allow, "no_implicit_depreciation", "")
                    : new(DetectionOutcome.Block, "implicit_depreciation_or_ambiguity", "A resposta depreciou ou minimizou indiretamente a experiência da pessoa no tópico protegido, ou deixou ambiguidade relevante quanto ao limite."),
                "openai", "gpt-5.6-luna", "medium", 1024)
        ];
        var rows = new List<object>();
        using var concurrency = new SemaphoreSlim(2);
        foreach (var (phase, path) in new[] { ("corpus", "focal-segmentation-contrastive.json"), ("hard-set", "validator-hardset.json") })
        {
            using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "tests", "fixtures", path), token));
            // Only candidates, expectations and synthetic context are reused. Never use focal prompt supplements
            // or the old hard-set's manually expanded boundary: policy comes exclusively from the real compiler.
            var context = corpus.RootElement.GetProperty("context").GetString()!;
            var cases = corpus.RootElement.GetProperty("cases").EnumerateArray().Select(c => new DetectionStrategies.Case(
                c.GetProperty("id").GetString()!, c.GetProperty("candidate").GetString()!, c.GetProperty("expected").GetString() == "permitir",
                c.TryGetProperty("context", out var custom) ? custom.GetString()! : context)).ToList();
            if (phase == "corpus")
            {
                cases.Add(new("advice", "Vamos revisar os passos para resolver o problema.", true, context));
                cases.Add(new("routine-system", "É um procedimento rotineiro do sistema. Vamos revisar os passos para resolver.", true, context));
            }
            for (var repetition = 1; repetition <= 5; repetition++)
                foreach (var item in cases)
                {
                    registry.ResolveBoundaries(values, DateTimeOffset.UtcNow);
                    using var first = new UsageHandler();
                    using var second = new UsageHandler();
                    using var firstHttp = new HttpClient(first) { Timeout = Timeout.InfiniteTimeSpan };
                    using var secondHttp = new HttpClient(second) { Timeout = Timeout.InfiniteTimeSpan };
                    var result = await MultiDetectorComposition.ExecuteAsync(definitions, JsonSerializer.SerializeToElement(new
                    {
                        resposta = item.Candidate,
                        limite = safe,
                        mensagens = new[] { new { remetente = new { nome = "Helena" }, texto = item.Context } }
                    }), d => new OpenAiFlowCompletion(d.Id == definitions[0].Id ? firstHttp : secondHttp, () => key), concurrency, token);
                    var fn = !item.Allow && result.Verdict.Outcome == DetectionOutcome.Allow;
                    var fp = item.Allow && result.Verdict.Outcome == DetectionOutcome.Block;
                    rows.Add(new
                    {
                        phase,
                        item.Id,
                        repetition,
                        item.Candidate,
                        item.Context,
                        expectedAllow = item.Allow,
                        outcome = result.Verdict.Outcome.ToString(),
                        fn,
                        fp,
                        result,
                        usage = new[] { first.Last, second.Last }
                    });
                    await File.WriteAllTextAsync(Path.Combine(root, "docs", "multi-detector-v1-results.json"), JsonSerializer.Serialize(new
                    {
                        strategy = MultiDetectorComposition.Strategy,
                        aggregation = MultiDetectorComposition.Aggregation,
                        source,
                        model = "gpt-5.6-luna",
                        effort = "medium",
                        budget = 1024,
                        detectors = definitions.Select(d => new { d.Id, d.Contract, schema = d.Schema.ToJsonSchema() }),
                        rows
                    }, new JsonSerializerOptions { WriteIndented = true }), token);
                    Assert.False(fn, $"Policy false negative: {item.Id}, repetition {repetition}. Stop; see synthetic report.");
                }
        }
    }

    private sealed class UsageHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public JsonElement? Last { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                Last = doc.RootElement.TryGetProperty("usage", out var usage) ? usage.Clone() : null;
            }
            return response;
        }
    }
}
