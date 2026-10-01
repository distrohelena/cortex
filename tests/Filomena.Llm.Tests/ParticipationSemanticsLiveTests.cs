using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Inference.LmStudio;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class ParticipationSemanticsLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_SEMANTICS") == "1";
    internal static readonly Scenario[] Cases = [.. ParticipationCalibrationLiveTests.Cases.Where(s => s.Id != 8),
        new(11, "atualização técnica banal", Inactive, [new("Helena", "atualizei o Node"), new("Bia", "legal"), new("Helena", "foi de versão 22 para 24")])];

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in loaded Gemma: 30 observational evaluations, no sending or service startup.")]
    public async Task ThreeRepetitionsWithExplicitInterestsAndReasonCodes()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new LmStudioOptions
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("FILOMENA_EXPERIMENT_LM_ENDPOINT") ?? "http://127.0.0.1:1234/v1/"),
            GenerationTemperature = 0.1,
            GenerationMaxTokens = 4096,
            RequestTimeoutSeconds = Timeout.Infinite
        };
        using var usage = new UsageObserver(new HttpClientHandler());
        using var http = new HttpClient(usage) { BaseAddress = options.Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        var models = await http.GetFromJsonAsync<JsonElement>(new Uri(options.Endpoint, "/api/v0/models"), token);
        Assert.Contains(models.GetProperty("data").EnumerateArray(), m => m.GetProperty("id").GetString() == options.Model && m.GetProperty("state").GetString() == "loaded");
        var model = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
        var results = new List<object>();
        var failures = new List<string>();
        var output = TestContext.Current.TestOutputHelper!;
        for (var repetition = 1; repetition <= 3; repetition++)
            foreach (var scenario in Cases)
            {
                output.WriteLine($"START repetition={repetition} scenario={scenario.Id}");
                var before = usage.Calls.Count;
                var run = await ExecuteAsync(scenario, false, model, null, "none", token,
                    projectOverride: ParticipationSemanticsTests.Fixture(scenario.InitialState), projectRevision: "slice37");
                var r = run.Report;
                var usages = usage.Calls.Skip(before).ToArray();
                Assert.Equal(r.LlmCalls, usages.Length);
                var result = new
                {
                    repetition,
                    r.Scenario,
                    r.Name,
                    r.InitialState,
                    r.BatchSize,
                    r.Situation,
                    Decision = r.Presence,
                    FinalState = r.CurrentState,
                    r.Signal,
                    r.Success,
                    r.Error,
                    r.LlmCalls,
                    CorrectiveCalls = r.Calls.Count(c => c.Correction),
                    SituationMs = r.SituationMilliseconds,
                    DecisionMs = r.PresenceMilliseconds,
                    TotalLlmMs = r.Calls.Sum(c => c.Milliseconds),
                    Calls = r.Calls.Select((c, i) => new { c.NodeId, c.Milliseconds, c.Correction, Usage = usages[i] }).ToArray(),
                    Path = r.Trace.Where(t => t.EventName is "node-completed" or "node-failed" or "transition-applied")
                        .Select(t => new { t.EventName, t.NodeId, t.Exit, t.BatchStateId, t.BatchStateVersion, t.CurrentSessionStateId, t.CurrentSessionStateVersion }).ToArray()
                };
                results.Add(result);
                output.WriteLine("SEMANTICS_CASE=" + JsonSerializer.Serialize(result));
                if (!r.Success) failures.Add($"{scenario.Id}/{repetition}: {r.Error}");
            }
        output.WriteLine("SEMANTICS_REPORT=" + JsonSerializer.Serialize(new
        {
            model = options.Model,
            temperature = options.GenerationTemperature,
            maxTokens = options.GenerationMaxTokens,
            repetitions = 3,
            noSending = true,
            noMemory = true,
            results
        }));
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // Test-only usage telemetry. Never records requests, raw responses, model reasoning or credentials.
    internal sealed class UsageObserver(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal sealed record Usage(long? PromptTokens, long? CompletionTokens, long? ReasoningTokens);
        internal List<Usage> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                // ReadAsByteArray buffers content so the production client receives the same unmodified body.
                using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
                var root = json.RootElement;
                var usage = root.TryGetProperty("usage", out var u) ? u : default;
                var details = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens_details", out var d) ? d : default;
                Calls.Add(new(Number(usage, "prompt_tokens"), Number(usage, "completion_tokens"), Number(details, "reasoning_tokens")));
            }
            return response;
        }

        private static long? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var number) ? number : null;
    }
}
