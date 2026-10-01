using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Inference.LmStudio;
using Cortex.Inference.OpenAi;
using Filomena.Llm.SystemOne;
using Cortex.Inference.SystemOne;
using Cortex.Inference;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class SystemOneParticipationLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_EXPERIMENT") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Explicit isolated participation experiment; no generation, Graphiti or transport.")]
    public async Task Compare_participation_with_pinned_experimental_projection()
    {
        var token = TestContext.Current.CancellationToken;
        var provider = Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_PROVIDER") ?? "laya";
        if (provider is not ("laya" or "jev")) throw new InvalidDataException("Choose laya or jev.");
        var model = Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_MODEL") ?? (provider == "laya" ? "multilingual" : "jev-latest");
        var variants = (Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_VARIANTS") ?? "baseline,situation,both").Split(',');
        var repetitions = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_REPETITIONS") ?? "1");
        Assert.InRange(repetitions, 1, 5);
        var endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_ENDPOINT") ??
            (provider == "laya" ? "http://127.0.0.1:8000/v1/systemone" : "https://api.typesafe.ai/v1/systemone"));
        if (provider == "laya" && !endpoint.IsLoopback) throw new InvalidDataException("Local Laya experiment requires loopback.");
        var semanticOptions = Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_SEMANTIC_OPTIONS") == "1";
        var focusedInvolvement = Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_FOCUSED_INVOLVEMENT") == "1";
        if (focusedInvolvement && !semanticOptions) throw new InvalidDataException("Focused variant requires semantic options.");
        using var wire = new WireObserver(new HttpClientHandler { AllowAutoRedirect = false }, semanticOptions, focusedInvolvement);
        using var http = new HttpClient(wire) { Timeout = TimeSpan.FromMinutes(2) };
        var policy = Environment.GetEnvironmentVariable("FILOMENA_JEV_PROBABILITY_POLICY") switch
        {
            null => SystemOneProbabilityPolicy.LayaFourDecimals,
            "jev-1.13-two-decimal-v1" when provider == "jev" => SystemOneProbabilityPolicy.Jev113TwoDecimals,
            _ => throw new InvalidDataException("Unknown or mismatched probability policy.")
        };
        var systemOne = new SystemOneClient(http, endpoint,
            () => Environment.GetEnvironmentVariable(provider == "jev" ? "TYPESAFE_API_KEY" : "LAYA_API_KEY"), provider == "jev", policy);
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192 };
        using var localHttp = new HttpClient { BaseAddress = options.Endpoint };
        var calls = new List<object>();
        var completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            [provider] = new SystemOneFlowCompletion(systemOne, provider, 0.5, (node, result) => calls.Add(new { node, result })),
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(localHttp, options), options.Model),
            ["openai"] = new OpenAiFlowCompletion(http, () => Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
        });
        var preserveSummary = Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_PRESERVE_SUMMARY") == "1";
        if (preserveSummary && variants.Any(v => v != "both")) throw new InvalidDataException("Summary composition currently compares both typed stages only.");
        var composed = preserveSummary ? new SystemOneSummaryComposition(
            new SystemOneFlowCompletion(systemOne, provider, 0.5, (node, result) => calls.Add(new { node, result })), completion, provider, model) : null;
        var sourceBytes = File.ReadAllBytes(Path.Combine(ParticipationGateV1Tests.Root, SystemOneParticipationTests.SourceArtifact));
        var sourceRevision = Convert.ToHexString(SHA256.HashData(sourceBytes));
        var directory = Path.Combine(ParticipationGateV1Tests.Root, "TestResults", "systemone", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var contrastBase = DirectParticipationRuleLiveTests.Corpus.Single(f => f.Scenario.Id == 7);
        var corpus = DirectParticipationRuleLiveTests.Corpus.Concat(new[]
        {
            contrastBase with { Scenario = new(10, "direct addressee minimal pair", Inactive, [new("Gabriel", "Filomena, você prefere RAII ou coleta de lixo?")]), ExpectedActions = ["responder"], RequireDirect = true },
            contrastBase with { Scenario = new(11, "other addressee minimal pair", Inactive, [new("Gabriel", "Bia, você prefere RAII ou coleta de lixo?")]) },
            DirectParticipationRuleLiveTests.Corpus[0] with { Scenario = new(12, "summary quantity preservation", Inactive, [new("Helena", "Filomena, o sistema apagou meu relatório duas vezes. Perdi duas horas no total entre as duas falhas. Como evito refazer tudo?")]) }
        });
        if (Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_CORPUS") == "holdout") corpus = SystemOneHoldoutCorpus.Cases;
        foreach (var variant in variants)
        foreach (var fixture in corpus)
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            if (Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_CASE") is { } selected && !selected.Split(',').Select(int.Parse).Contains(fixture.Scenario.Id)) continue;
            calls.Clear();
            wire.Calls.Clear();
            composed?.TypedRequests.Clear();
            composed?.SummaryCalls.Clear();
            var project = preserveSummary
                ? DirectParticipationRuleTests.ParticipationProject(fixture.Scenario.InitialState, sourceProject: SystemOneSummaryCompositionTests.Source())
                : SystemOneParticipationTests.Project(fixture.Scenario.InitialState, variant, provider, model);
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ToJsonString())));
            var run = await ExecuteAsync(fixture.Scenario, false, new ParticipationOnly((IFlowCompletion?)composed ?? completion), null, "none", token, fixture.Kind, project, revision);
            var direct = DirectParticipationRuleTests.Direct(run);
            var action = direct ? "responder" : run.Report.Action;
            var pass = run.Report.Success && fixture.ExpectedActions.Contains(action!) && (!fixture.RequireDirect || direct)
                && (fixture.Scenario.Id != 5 || run.Report.CurrentState == Attentive)
                && (fixture.Scenario.Id != 4 || run.Report.Situation!.Value.GetProperty("envolvimento").GetString() != "nenhum");
            var involvementPass = fixture.Scenario.Id < 20 || run.Report.Success &&
                SystemOneHoldoutCorpus.Involvement(fixture.Scenario.Id).Contains(run.Report.Situation!.Value.GetProperty("envolvimento").GetString()!);
            pass = pass && involvementPass && (fixture.Scenario.Id != 27 || run.Report.CurrentState == Attentive);
            var record = new { provider, model, variant, semanticOptions, focusedInvolvement, sourceRevision, revision, fixture.Scenario.Id, fixture.Scenario.Name, repetition,
                summaryOmitted = !preserveSummary && variant != "baseline", summaryCalls = composed?.SummaryCalls,
                booleanThreshold = 0.5, direct, action, pass, involvementPass, probabilityPolicy = policy.ToString(),
                run.Report.Success, run.Report.Error, run.Report.Situation, run.Report.Presence, run.Report.CurrentState,
                run.Report.SituationMilliseconds, run.Report.PresenceMilliseconds, run.Report.Calls, systemOneCalls = calls,
                generationCalls = 0, graphitiCalls = 0, transportCalls = 0,
                wireCalls = wire.Calls,
                cost = (decimal?)null, costNote = "No price inferred; local inference has no API bill. Hosted price requires verified rates." };
            await File.WriteAllTextAsync(Path.Combine(directory, $"{variant}-{fixture.Scenario.Id:D2}-{repetition}.json"), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }), token);
            // Quality misses are experimental results; contract/runtime failures stop the experiment.
            Assert.True(run.Report.Success, run.Report.Error);
            Assert.Single(run.Report.Trace, t => t.EventName == "execution-started");
            Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "node-outbound");
            if (direct) Assert.DoesNotContain(run.Requests, r => r.NodeId.EndsWith("-presence", StringComparison.Ordinal));
            if (preserveSummary)
            {
                var summary = run.Report.Situation!.Value.GetProperty("resumo").GetString();
                Assert.False(string.IsNullOrWhiteSpace(summary));
                Assert.Single(composed!.SummaryCalls);
                if (!direct) Assert.Equal(summary, Assert.Single(composed.TypedRequests, r => r.NodeId.EndsWith("-presence")).Inputs.GetProperty("resumo_situacao").GetString());
            }
        }
    }

    // Synthetic experiment payloads only. No request/response headers or credentials are recorded.
    private sealed class WireObserver(HttpMessageHandler inner, bool semanticOptions, bool focusedInvolvement) : DelegatingHandler(inner)
    {
        public List<object> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var text = await request.Content!.ReadAsStringAsync(token);
            if (semanticOptions)
            {
                var modified = JsonNode.Parse(text)!;
                if (modified["questions"]?["envolvimento"] is JsonObject involvement)
                {
                    if (focusedInvolvement)
                        involvement["instructions"] = "Qual é a relação da troca atual em `state.mensagens` com Filomena, considerando `state.historico`? Identifique a quem as pessoas se dirigem, não apenas se existe uma pergunta. Classifique somente envolvimento; interesse pelo tema não conta como envolvimento.";
                    involvement["criteria"] = new JsonObject
                    {
                        ["nenhum"] = "Filomena é apenas observadora da troca atual, mesmo se o assunto puder lhe interessar. Uma pergunta dirigida a outra pessoa não é dirigida a Filomena.",
                        ["indireto"] = "Não chamam Filomena diretamente, mas há continuidade clara com sua participação recente, sua ideia ou ação, ou falam sobre ela sem solicitar resposta.",
                        ["direto"] = "A troca atual se dirige claramente a Filomena: chamada pelo nome, mention, reply à mensagem dela, pergunta ou pedido de opinião dirigido a ela."
                    };
                    request.Content.Dispose();
                    text = modified.ToJsonString();
                    request.Content = new StringContent(text, Encoding.UTF8, "application/json");
                }
            }
            var payload = JsonDocument.Parse(text);
            using (payload)
            {
                var response = await base.SendAsync(request, token);
                JsonElement? body = null;
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                        body = parsed.RootElement.Clone();
                    }
                    catch (JsonException) { /* The real client must reject malformed JSON. */ }
                }
                Calls.Add(new { status = (int)response.StatusCode, request = payload.RootElement.Clone(), response = body });
                return response;
            }
        }
    }

    private sealed class ParticipationOnly(IFlowCompletion inner) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!request.NodeId.EndsWith("-situation", StringComparison.Ordinal) && !request.NodeId.EndsWith("-presence", StringComparison.Ordinal))
                throw new InvalidOperationException("Experiment forbids generation/enforcement.");
            return inner.CompleteAsync(request, token);
        }
    }
}
