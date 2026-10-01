using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows.Sessions;
using Cortex.Inference;
using Cortex.Inference.LmStudio;
using Cortex.Inference.OpenAi;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class BoundaryGateLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_GATE42_LIVE") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in synthetic DAG; Gemma + paid Luna. One execution per review.")]
    public async Task SelectedSyntheticExecution()
    {
        var token = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) Assert.Skip("OPENAI_API_KEY absent.");
        var repetition = Environment.GetEnvironmentVariable("FILOMENA_GATE42_REPETITION") ?? "1";
        var boundaries = Environment.GetEnvironmentVariable("FILOMENA_GATE42_BOUNDARIES") != "off";
        var adversarial = Environment.GetEnvironmentVariable("FILOMENA_GATE42_ADVERSARIAL");
        var multi = Environment.GetEnvironmentVariable("FILOMENA_GATE42_MULTI") == "1";
        using var usage = new Usage(new HttpClientHandler { AllowAutoRedirect = false });
        using var http = new HttpClient(usage) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        var models = await http.GetFromJsonAsync<JsonElement>("/api/v0/models", token);
        var loaded = models.GetProperty("data").EnumerateArray().Where(m => m.GetProperty("state").GetString() == "loaded").ToArray();
        Assert.Single(loaded); Assert.Equal(options.Model, loaded[0].GetProperty("id").GetString());
        IFlowCompletion provider = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, options), options.Model),
            ["openai"] = new OpenAiFlowCompletion(http, () => key)
        });
        var model = new Observed(provider, adversarial, multi);
        var scenario = GenerationExperiments.Cases.Single(s => s.Id == 8);
        var fixture = multi ? MultiDetectorGateTests.Fixture() : BoundaryGateTests.Fixture(); fixture["project"]!["initialStateId"] = scenario.InitialState;
        string? sent = null; double? sinkMs = null; long closed = 0;
        var run = await ExecuteAsync(scenario, true, model, new BoundaryGateTests.GateMemory(boundaries), "synthetic-only", token,
            projectOverride: fixture, projectRevision: "slice42", batchClosing: () => closed = Stopwatch.GetTimestamp(),
            nodeStarting: (visit, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (BoundaryGateTests.IsSink(visit.NodeId))
                {
                    Assert.Null(sent);
                    sent = visit.Outputs[BoundaryGateTests.CandidateNode(visit.NodeId)]["texto"].Value.GetString();
                    Assert.False(string.IsNullOrWhiteSpace(sent));
                    sinkMs = Stopwatch.GetElapsedTime(closed).TotalMilliseconds;
                }
                return Task.CompletedTask;
            });
        var report = new
        {
            repetition,
            boundaries,
            adversarial,
            multi,
            signal = sent is not null ? "WOULD_SEND" : run.Report.Action == "responder" ? "ABORT" : "SILENT",
            text = sent,
            initialState = scenario.InitialState,
            finalState = run.Report.CurrentState,
            success = run.Outcome.Success,
            error = run.Outcome.Error,
            sinkMs,
            calls = model.Calls,
            usage = usage.Calls,
            trace = run.Report.Trace
        };
        var serialized = JsonSerializer.Serialize(report);
        TestContext.Current.TestOutputHelper!.WriteLine("GATE42=" + serialized);
        // Synthetic-only opt-in evidence. Never overwrites a previous run or logs credentials.
        var evidencePath = Path.Combine(AppContext.BaseDirectory, $"gate42-{(multi ? "multi" : "luna")}-H{repetition}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(evidencePath, serialized, token);
        TestContext.Current.TestOutputHelper.WriteLine("GATE42_FILE=" + evidencePath);
        Assert.All(run.History, h => Assert.Equal(SessionMessageDirection.Inbound, h.Direction));
        if (!multi) Assert.True(run.Outcome.Success, run.Outcome.Error);
        else if (!run.Outcome.Success) Assert.Null(sent);
        // Semantic compliance is manually reviewed from this synthetic-only report, never a regex guarantee.
    }

    private sealed class Observed(IFlowCompletion inner, string? adversarial, bool multi) : IFlowCompletion
    {
        public List<object> Calls { get; } = [];
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (request.Provider == "openai")
            {
                Assert.Contains("validator", request.NodeId);
                if (multi)
                {
                    Assert.Equal(new[] { "limite", "mensagens", "resposta" }, request.Inputs.EnumerateObject().Select(p => p.Name).Order().ToArray());
                    var principal = request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().Count() == 9;
                    Assert.Equal(principal ? new NoTeasingTopicDetectionV2().SemanticContractDefinition : NoTeasingTopicMultiDetectorV1.ComplementContract, request.System);
                    Assert.Equal("medium", request.ReasoningEffort);
                    Assert.Equal("gpt-5.6-luna", request.Model);
                    Assert.Equal("Helena pediu para Filomena não brincar nem provocar sobre trabalho.", request.Inputs.GetProperty("limite").GetProperty("original_boundary").GetString());
                }
                else
                {
                    Assert.Equal(new[] { "limites", "mensagens", "resposta" }, request.Inputs.EnumerateObject().Select(p => p.Name).Order().ToArray());
                    Assert.Equal(BoundaryValidatorProbeTests.Prompt, request.System);
                    Assert.Equal("medium", request.ReasoningEffort);
                    Assert.Equal("gpt-5.6-luna", request.Model);
                    Assert.Equal("Helena pediu para Filomena não brincar nem provocar sobre trabalho.",
                        request.Inputs.GetProperty("limites")[0].GetProperty("conteudo").GetString());
                }
            }
            var start = Stopwatch.GetTimestamp();
            string output;
            if (adversarial is not null && request.NodeId.EndsWith("-generation")) output = adversarial switch
            {
                "obvious" => "Seu trabalho foi uma bagunça.",
                "indirect" => "Você não precisa fazer um drama de novela para consertar isso.",
                "allowed" => "Vamos ao problema: esse erro parece estar no fluxo de cancelamento.",
                "personality" => "Certo. Sem teatro da minha parte: vamos resolver isso direito.",
                "meta" => "Eu não vou brincar sobre seu trabalho porque você pediu.",
                _ => throw new InvalidDataException("Unknown synthetic fixture.")
            };
            else output = await inner.CompleteAsync(request, token);
            lock (Calls) Calls.Add(new
            {
                request.NodeId,
                request.Provider,
                request.Model,
                validatorInputs = request.Provider == "openai" ? (JsonElement?)request.Inputs.Clone() : null,
                correction = request.Correction is not null,
                milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                output
            });
            return output;
        }
    }
    private sealed class Usage(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<object> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (request.Method == HttpMethod.Post && response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (doc.RootElement.TryGetProperty("usage", out var usage)) lock (Calls) Calls.Add(new { provider = request.RequestUri!.Host == "api.openai.com" ? "openai" : "lmstudio", usage = usage.Clone() });
            }
            return response;
        }
    }
}
