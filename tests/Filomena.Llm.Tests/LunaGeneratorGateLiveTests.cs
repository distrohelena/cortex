using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference;
using Cortex.Inference.LmStudio;
using Cortex.Inference.OpenAi;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

// Synthetic evidence only. No runtime routing override, transport send, or semantic auto-judge.
public sealed class LunaGeneratorGateLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_LUNA_GENERATOR_H") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in one fresh H run, manually reviewed before next run.")]
    public async Task One_fresh_H_through_current_session_DAG()
    {
        var token = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) Assert.Skip("OPENAI_API_KEY unavailable.");
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_LUNA_GENERATOR_H_RUN") ?? "1");
        var evidenceRoot = Environment.GetEnvironmentVariable("FILOMENA_LUNA_GENERATOR_H_EVIDENCE")
            ?? throw new InvalidOperationException("Explicit evidence directory required.");
        var calls = new List<object>();
        var revision = Environment.GetEnvironmentVariable("FILOMENA_LUNA_GENERATOR_H_REVISION") ?? "luna-generator-H-frozen-v1";
        var visits = new List<object>();
        var role = new AsyncLocal<string?>();
        using var meter = new Meter(new HttpClientHandler { AllowAutoRedirect = false }, role);
        using var http = new HttpClient(meter) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var options = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        var models = await http.GetFromJsonAsync<JsonElement>("/api/v0/models", token);
        var loaded = models.GetProperty("data").EnumerateArray().Where(m => m.GetProperty("state").GetString() == "loaded").ToArray();
        Assert.Single(loaded);
        Assert.Equal(options.Model, loaded[0].GetProperty("id").GetString());
        var provider = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, options), options.Model),
            ["openai"] = new OpenAiFlowCompletion(http, () => key)
        });
        var observed = new Observed(provider, role, calls);
        var fixture = MultiDetectorGateTests.Fixture();
        var scenario = GenerationExperiments.Cases.Single(s => s.Id == 8);
        fixture["project"]!["initialStateId"] = scenario.InitialState;
        long closed = 0;
        string? sent = null;
        double? sinkMs = null;
        var run = await ExecuteAsync(scenario, true, observed, new BoundaryGateTests.GateMemory(true),
            "synthetic GateMemory; no Graphiti", token, projectOverride: fixture,
            projectRevision: revision,
            batchClosing: () => closed = Stopwatch.GetTimestamp(),
            nodeStarting: (visit, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                visits.Add(new { visit.NodeId, milliseconds = Stopwatch.GetElapsedTime(closed).TotalMilliseconds });
                if (BoundaryGateTests.IsSink(visit.NodeId))
                {
                    Assert.Null(sent);
                    sent = visit.Outputs[BoundaryGateTests.CandidateNode(visit.NodeId)]["texto"].Value.GetString();
                    Assert.False(string.IsNullOrWhiteSpace(sent));
                    sinkMs = Stopwatch.GetElapsedTime(closed).TotalMilliseconds;
                }
                return Task.CompletedTask;
            });
        var totalMs = Stopwatch.GetElapsedTime(closed).TotalMilliseconds;
        var signal = sent is not null ? "WOULD_SEND"
            : !run.Outcome.Success && run.Report.Trace.Any(t => t.NodeId?.Contains("validator") == true && t.EventName == "node-failed")
                ? "EnforcementFailure" : run.Report.Action == "responder" ? "ABORT" : "silêncio/no-response";
        var evidence = new
        {
            experiment = revision,
            repetition,
            boundary = "Helena pediu para Filomena não brincar nem provocar sobre trabalho.",
            scenario,
            run.Report,
            run.Outcome,
            signal,
            candidateFinal = sent,
            sinkMs,
            totalMs,
            calls,
            usage = meter.Calls,
            visits
        };
        var path = Path.Combine(evidenceRoot, $"luna-generator-H{repetition}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence), token);
        TestContext.Current.TestOutputHelper!.WriteLine("LUNA_H_FILE=" + path);
        Assert.All(run.History, h => Assert.Equal(Cortex.Core.Flows.Sessions.SessionMessageDirection.Inbound, h.Direction));
        if (!run.Outcome.Success) Assert.Null(sent);
    }

    private sealed class Observed(IFlowCompletion inner, AsyncLocal<string?> role, List<object> calls) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            var detector = request.ResponseSchema is { } schema && request.NodeId.Contains("validator");
            var roleName = detector
                ? request.ResponseSchema!.Value.GetProperty("properties").TryGetProperty("relevante_ao_topico", out _)
                    ? "semantic-v2" : "implicit-depreciation-v1"
                : request.NodeId;
            if (request.NodeId.EndsWith("-generation") || request.NodeId.EndsWith("-regeneration"))
            {
                Assert.Equal("openai", request.Provider);
                Assert.Equal("gpt-5.6-luna", request.Model);
                Assert.Equal("medium", request.ReasoningEffort);
                Assert.Null(request.MaxOutputTokens);
            }
            role.Value = request.NodeId + "/" + roleName;
            var start = Stopwatch.GetTimestamp();
            string? output = null;
            string? failure = null;
            try { return output = await inner.CompleteAsync(request, token); }
            catch (Exception e) { failure = e.GetType().Name; throw; }
            finally
            {
                lock (calls) calls.Add(new
                {
                    request.NodeId,
                    role = roleName,
                    request.Provider,
                    request.Model,
                    request.ReasoningEffort,
                    request.MaxOutputTokens,
                    request.System,
                    request.Inputs,
                    request.ResponseSchema,
                    correction = request.Correction is not null,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    status = failure is null ? "valid" : "technical_failure",
                    failure,
                    output
                });
                role.Value = null;
            }
        }
    }

    private sealed class Meter(HttpMessageHandler inner, AsyncLocal<string?> role) : DelegatingHandler(inner)
    {
        public List<object> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Post) return await base.SendAsync(request, token);
            var function = role.Value;
            var start = Stopwatch.GetTimestamp();
            JsonElement? usage = null;
            int? status = null;
            string? failure = null;
            try
            {
                var response = await base.SendAsync(request, token);
                status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    if (json.RootElement.TryGetProperty("usage", out var value)) usage = value.Clone();
                }
                return response;
            }
            catch (Exception e) { failure = e.GetType().Name; throw; }
            finally
            {
                lock (Calls) Calls.Add(new
                {
                    function,
                    provider = request.RequestUri!.Host == "api.openai.com" ? "openai" : "lmstudio",
                    status,
                    failure,
                    usage,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds
                });
            }
        }
    }
}
