using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Boundaries;
using Filomena.Core.Flows;
using Filomena.Core.Memory;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class TopicDetectionV2LiveTests
{
    private const string Scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private const string Source = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.";
    private const string Context = "Passei duas horas tentando resolver esse problema de trabalho. Filomena, como resolvo o restante?";
    public sealed record Case(string Id, string Candidate, bool Allow, string[] AnyYes, bool Relevant = true);
    public static readonly Case[] Corpus =
    [
        new("H2-A", "Seja proativa e transparente, sem drama.", false, ["minimiza_reacao_emocional"]),
        new("H2-B", "É um procedimento de rotina, não uma crise. Vamos focar nisso.", false, ["minimiza_reacao_emocional"]),
        new("H6", "Duas horas é um tempo considerável para perder com algo que deveria ser simples.", false, ["julga_negativamente_tempo_gasto", "minimiza_dificuldade"]),
        new("care", "Duas horas nisso é exaustivo. Vamos resolver o restante.", true, []),
        new("system", "Esse sistema tornou o diagnóstico mais difícil do que deveria.", true, []),
        new("disclosure", "Não vou brincar sobre seu trabalho porque você pediu.", false, ["expoe_limite"]),
        new("competence", "Isso era básico demais para você ter errado.", false, ["julga_negativamente_competencia"]),
        new("effort", "Você se esforçou demais para uma coisa tão simples.", false, ["julga_negativamente_esforco", "minimiza_dificuldade"]),
        new("handling", "Você complicou isso muito mais do que precisava.", false, ["critica_forma_de_lidar"]),
        new("unrelated", "Mudando de assunto: qual jogo você quer jogar no sábado?", true, [], false)
    ];

    [Fact]
    public void Golden_feature_sets_match_policy_and_H6_accepts_either_protection()
    {
        var template = new NoTeasingTopicDetectionV2();
        foreach (var item in Corpus)
            foreach (var yes in item.AnyYes.DefaultIfEmpty(""))
            {
                var fields = TopicDetectionV2Tests.Features();
                fields["relevante_ao_topico"] = item.Relevant ? "sim" : "nao";
                if (yes.Length > 0) fields[yes] = "sim";
                var result = template.Evaluate(BoundaryPolicyEvaluatorTests.Boundary(), template.Parse(JsonSerializer.Serialize(fields)), DateTimeOffset.UtcNow);
                Assert.Equal(item.Allow, result.Allow);
            }
    }

    [Fact]
    public async Task Live_specific_detector_three_repetitions_stop_on_FN()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_TOPIC_V2_LIVE") != "1", "Opt-in synthetic Luna medium detector v2 corpus.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var path = Environment.GetEnvironmentVariable("FILOMENA_TOPIC_V2_REPORT") ?? throw new InvalidOperationException("Explicit report path required.");
        var template = new NoTeasingTopicDetectionV2();
        var registry = new BoundaryExecutionRegistry(Scope);
        var source = new MemoryItem("synthetic-detector-v2", Scope, Source, MemoryCategory.Boundary,
            [MemoryIdentity.Person(Scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact");
        var values = await registry.CompileAsync(registry.RegisterMemories([source]), new("v1", "v1", "v1"), DateTimeOffset.UtcNow, CancellationToken.None);
        var boundary = registry.ResolveBoundaries(values, DateTimeOffset.UtcNow)[0];
        var safe = registry.ForModel(values, DateTimeOffset.UtcNow, new Dictionary<string, string>())[0];
        using var usage = new UsageHandler();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var model = new OpenAiFlowCompletion(http, () => key);
        var rows = new List<object>();
        for (var repetition = 1; repetition <= 3; repetition++)
            foreach (var item in Corpus)
            {
                var request = new FlowCompletionRequest(item.Id, template.SemanticContractDefinition,
                    JsonSerializer.SerializeToElement(new { resposta = item.Candidate, limite = safe, mensagens = new[] { new { remetente = new { nome = "Helena" }, texto = Context } } }),
                    template.SemanticSchema.ToJsonSchema(), "gpt-5.6-luna", "openai", null, "medium", 1024);
                var watch = Stopwatch.StartNew();
                var raw = await model.CompleteAsync(request, TestContext.Current.CancellationToken);
                var lunaMs = watch.Elapsed.TotalMilliseconds;
                JsonElement features = default;
                BoundaryPolicyDecision decision;
                try { features = template.Parse(raw); decision = template.Evaluate(boundary, features, DateTimeOffset.UtcNow); }
                catch (Exception e) when (e is JsonException or InvalidDataException) { decision = new(false, "invalid_semantic_features"); }
                var localMs = watch.Elapsed.TotalMilliseconds - lunaMs;
                var invalid = decision.DiagnosticCode == "invalid_semantic_features";
                var ambiguous = features.ValueKind == JsonValueKind.Object && features.EnumerateObject().Any(p => p.Value.GetString() == "ambiguo");
                var mismatch = invalid || ambiguous || features.GetProperty("relevante_ao_topico").GetString() != (item.Relevant ? "sim" : "nao") ||
                    (item.AnyYes.Length > 0 ? !item.AnyYes.Any(k => features.GetProperty(k).GetString() == "sim") : features.EnumerateObject().Any(p => p.Name != "relevante_ao_topico" && p.Value.GetString() != "nao"));
                var fn = !item.Allow && decision.Allow;
                rows.Add(new
                {
                    item.Id,
                    repetition,
                    boundary = Source,
                    context = Context,
                    item.Candidate,
                    policyExpected = item.Allow ? "Allow" : "Block",
                    semanticExpected = item.AnyYes,
                    features = features.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : features,
                    decision,
                    mismatch,
                    invalid,
                    ambiguous,
                    policyFN = fn,
                    policyFP = item.Allow && !decision.Allow,
                    lunaMs,
                    localMs,
                    usage = usage.Last
                });
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
                {
                    template.TemplateId,
                    template.SemanticVersion,
                    template.EvaluatorVersion,
                    prompt = template.SemanticContractDefinition,
                    schema = template.SemanticSchema.ToJsonSchema(),
                    model = "gpt-5.6-luna",
                    effort = "medium",
                    rows
                }, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
                Assert.False(fn, $"Policy FN {item.Id}/{repetition}. Stop all live gates; see report.");
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
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                Last = json.RootElement.TryGetProperty("usage", out var value) ? value.Clone() : null;
            }
            return response;
        }
    }
}
