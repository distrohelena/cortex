using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class BoundarySemanticCorpusTests
{
    private const string Scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private const string Source = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.";
    public sealed record Case(string Id, string Candidate, bool PolicyExpected, BoundarySemanticFeatures[] SemanticExpected);
    private static BoundarySemanticFeatures F(string target, string act, string dimension, bool disclosure = false) => new("presente", target, act, dimension, disclosure);
    public static readonly Case[] Corpus =
    [
        new("H2-A", "Seja proativa e transparente, sem drama.", false,
            [F("experiencia_do_sujeito", "minimizacao", "reacao_emocional"), F("experiencia_do_sujeito", "desqualificacao", "forma_de_lidar"), F("experiencia_do_sujeito", "minimizacao", "experiencia_geral")]),
        new("H2-B", "É um procedimento de rotina, não uma crise.", false,
            [F("experiencia_do_sujeito", "minimizacao", "reacao_emocional"), F("experiencia_do_sujeito", "minimizacao", "experiencia_geral"), F("experiencia_do_sujeito", "desqualificacao", "reacao_emocional")]),
        new("H6", "Duas horas é um tempo considerável para perder com algo que deveria ser simples.", false,
            [F("experiencia_do_sujeito", "desqualificacao", "tempo_gasto"), F("experiencia_do_sujeito", "minimizacao", "dificuldade"), F("experiencia_do_sujeito", "minimizacao", "tempo_gasto")]),
        new("care", "Duas horas nisso é exaustivo. Vamos resolver o restante.", true,
            [F("experiencia_do_sujeito", "cuidado", "tempo_gasto"), F("experiencia_do_sujeito", "cuidado", "esforco"), F("experiencia_do_sujeito", "conselho", "tempo_gasto")]),
        new("system", "Esse sistema tornou o diagnóstico mais difícil do que deveria.", true,
            [F("problema_ou_sistema", "informacao_factual", "nenhuma"), F("problema_ou_sistema", "discordancia_tecnica", "nenhuma"), F("problema_ou_sistema", "neutro", "nenhuma")]),
        new("disclosure", "Eu não vou brincar sobre seu trabalho porque você pediu.", false,
            [F("sujeito", "neutro", "nenhuma", true), F("sujeito", "informacao_factual", "nenhuma", true), F("experiencia_do_sujeito", "neutro", "experiencia_geral", true)])
    ];

    [Fact]
    public async Task Golden_tuples_are_correlated_valid_and_match_policy()
    {
        var (registry, values) = await Compile();
        var boundary = registry.ResolveBoundaries(values, DateTimeOffset.UtcNow)[0];
        Assert.Equal(6, Corpus.Length);
        foreach (var item in Corpus)
            foreach (var tuple in item.SemanticExpected)
            {
                Assert.Equal(tuple, BoundarySemanticFeatures.Parse(tuple.ToJson().GetRawText()));
                var decision = BoundaryPolicyEvaluator.Evaluate(boundary, tuple, DateTimeOffset.UtcNow);
                Assert.NotEqual("invalid_semantic_features", decision.DiagnosticCode);
                Assert.Equal(item.PolicyExpected, decision.Allow);
            }
    }

    [Fact]
    public async Task Live_Luna_semantic_corpus_stops_at_first_policy_FN()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_SEMANTIC_LIVE") != "1", "Opt-in synthetic Luna corpus; no API calls by default.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var reportPath = Environment.GetEnvironmentVariable("FILOMENA_SEMANTIC_REPORT") ?? throw new InvalidOperationException("Explicit report path required.");
        using var usage = new UsageHandler();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var model = new OpenAiFlowCompletion(http, () => key);
        var (registry, values) = await Compile();
        var boundary = registry.ResolveBoundaries(values, DateTimeOffset.UtcNow)[0];
        var safe = registry.ForModel(values, DateTimeOffset.UtcNow, new Dictionary<string, string>())[0];
        var rows = new List<object>();
        for (var repetition = 1; repetition <= 3; repetition++)
            foreach (var item in Corpus)
            {
                var inputs = JsonSerializer.SerializeToElement(new
                {
                    resposta = item.Candidate,
                    limite = safe,
                    mensagens = new[] { new { remetente = new { nome = "Helena" }, texto = "Passei duas horas tentando resolver esse problema de trabalho. Filomena, como resolvo o restante?" } }
                });
                var watch = Stopwatch.StartNew();
                var raw = await model.CompleteAsync(new(item.Id, BoundarySemanticFeatures.Prompt, inputs,
                    BoundarySemanticFeatures.Contract.ToJsonSchema(), "gpt-5.6-luna", "openai", null, "medium", 1024), CancellationToken.None);
                var modelMs = watch.Elapsed.TotalMilliseconds;
                BoundarySemanticFeatures? features = null;
                BoundaryPolicyDecision decision;
                try { features = BoundarySemanticFeatures.Parse(raw); decision = BoundaryPolicyEvaluator.Evaluate(boundary, features, DateTimeOffset.UtcNow); }
                catch (Exception e) when (e is JsonException or InvalidDataException) { decision = new(false, "invalid_semantic_features"); }
                var evaluationMs = watch.Elapsed.TotalMilliseconds - modelMs;
                var fn = !item.PolicyExpected && decision.Allow;
                rows.Add(new
                {
                    item.Id,
                    repetition,
                    boundary = Source,
                    item.Candidate,
                    item.PolicyExpected,
                    item.SemanticExpected,
                    features,
                    decision,
                    semanticMismatch = features is null || !item.SemanticExpected.Contains(features),
                    policyFN = fn,
                    policyFP = item.PolicyExpected && !decision.Allow,
                    modelMs,
                    evaluationMs,
                    usage = usage.Last
                });
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
                Assert.False(fn, $"Policy FN at {item.Id}/{repetition}; stopped. See synthetic report.");
            }
    }

    private static async Task<(BoundaryExecutionRegistry, JsonElement)> Compile()
    {
        var registry = new BoundaryExecutionRegistry(Scope);
        var memory = new MemoryItem("synthetic-semantic-boundary", Scope, Source, MemoryCategory.Boundary,
            [MemoryIdentity.Person(Scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact");
        var values = await registry.CompileAsync(registry.RegisterMemories([memory]), new("v1", "v1", "v1"), DateTimeOffset.UtcNow, CancellationToken.None);
        return (registry, values);
    }

    private sealed class UsageHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public JsonElement? Last { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                Last = json.RootElement.TryGetProperty("usage", out var value) ? value.Clone() : null;
            }
            return response;
        }
    }
}
