using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GeneratorComparisonLiveTests
{
    [Fact]
    public async Task Prepare_frozen_requests_without_network()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_GENERATOR_PREPARE") != "1", "Explicit snapshot preparation only.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT")!;
        var path = Path.Combine(root, "docs/generator-comparison-inputs.json");
        Assert.False(File.Exists(path), "Frozen inputs already exist; never overwrite during benchmark.");
        var cases = new List<object>();
        foreach (var c in GeneratorComparison.Cases)
        {
            var request = await GeneratorComparison.Capture(c.Scenario.Id, TestContext.Current.CancellationToken);
            cases.Add(new { id = c.Scenario.Id, c.Scenario.Name, c.Boundary, c.Social, c.Expected, request });
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Selected_fixture_all_three_generators_five_valid()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_GENERATOR_LIVE") != "1", "Paid synthetic comparison opt-in only.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT")!;
        var phase = Environment.GetEnvironmentVariable("FILOMENA_GENERATOR_PHASE") ?? "only";
        Assert.Contains(phase, new[] { "only", "pipeline" });
        var id = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_GENERATOR_CASE")!);
        var token = TestContext.Current.CancellationToken;
        var snapshot = await File.ReadAllTextAsync(Path.Combine(root, "docs/generator-comparison-inputs.json"), token);
        using var inputDoc = JsonDocument.Parse(snapshot);
        var request = inputDoc.RootElement.EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == id).GetProperty("request").Deserialize<FlowCompletionRequest>()!;
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot)));
        var previous = new List<JsonElement>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "generator-comparison-attempts-*.jsonl"))
            foreach (var line in await File.ReadAllLinesAsync(file, token))
            {
                using var d = JsonDocument.Parse(line);
                if (d.RootElement.GetProperty("type").GetString() == "execution") previous.Add(d.RootElement.Clone());
            }
        Assert.All(previous, p => Assert.Equal(fingerprint, p.GetProperty("fingerprint").GetString()));
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var loaded = await probe.GetFromJsonAsync<JsonElement>("http://127.0.0.1:1234/api/v0/models", token);
        var active = loaded.GetProperty("data").EnumerateArray().Where(m => m.GetProperty("state").GetString() == "loaded").ToArray();
        Assert.Single(active);
        Assert.Equal("google/gemma-4-e4b", active[0].GetProperty("id").GetString());
        var path = Path.Combine(root, "docs", $"generator-comparison-attempts-{phase}-{id}-{Guid.NewGuid():N}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        using var logLock = new SemaphoreSlim(1);
        async Task Write(object value)
        {
            await logLock.WaitAsync(token);
            try { await log.WriteLineAsync(JsonSerializer.Serialize(value)); }
            finally { logLock.Release(); }
        }
        await Write(new { type = "metadata", fingerprint, phase, id, gemmaTemperature = 0.3, gemmaMaxTokens = 8192, remoteEffort = "medium", remoteMaxTokens = 4096, automaticRetry = 0 });
        for (var repetition = 1; repetition <= 5; repetition++)
            foreach (var model in new[] { "gemma", "luna", "terra" }.OrderBy(m => HashCode.Combine(id, repetition, m)))
            {
                var cell = previous.Where(p => p.GetProperty("phase").GetString() == phase && p.GetProperty("id").GetInt32() == id && p.GetProperty("model").GetString() == model).ToArray();
                if (cell.Any(p => p.GetProperty("repetition").GetInt32() == repetition && p.GetProperty("status").GetString() == "valid")) continue;
                Assert.True(cell.Count(p => p.GetProperty("status").GetString() != "valid") < 3, "Three technical failures in cell; stop.");
                var executionId = Guid.NewGuid().ToString("N");
                var calls = new Measured(model, key!, Write, executionId, phase, id, repetition);
                var start = Stopwatch.GetTimestamp();
                string status = "valid", final = "CANDIDATE_ONLY", error = ""; string? text = null; object[] decisions = [];
                try
                {
                    if (phase == "only") text = await calls.CompleteAsync(request, token);
                    else
                    {
                        var result = await GeneratorComparison.RunPipeline(id, request, calls, token);
                        final = result.Final; text = result.Text; decisions = result.Decisions;
                        if (final is "EnforcementFailure" or "GenerationFailure") status = calls.Failures.LastOrDefault() ?? "invalid_output";
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    if (token.IsCancellationRequested) throw;
                    status = Classify(e); error = e.GetType().Name;
                }
                await Write(new { type = "execution", fingerprint, executionId, phase, id, model, repetition, status, error, final, text, decisions, milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds });
                Assert.True(status == "valid", "Attempt logged; explicit resume needed, no automatic technical retry.");
            }
    }

    internal static string Classify(Exception e) => e switch
    {
        HttpRequestException h => h.StatusCode is null ? "transport_failure" : "provider_failure",
        OperationCanceledException c => c.InnerException is TimeoutException ? "timeout" : "transport_failure",
        InvalidDataException or JsonException => "invalid_output",
        _ => "internal_error"
    };

    private sealed class Measured(string generator, string key, Func<object, Task> write, string executionId, string phase, int id, int repetition) : IFlowCompletion
    {
        internal ConcurrentQueue<string> Failures = new();
        public async Task<string> CompleteAsync(FlowCompletionRequest source, CancellationToken token)
        {
            var detector = source.NodeId.Contains("validator");
            var request = detector ? source : GeneratorComparison.ForModel(source, generator);
            var blindId = Guid.NewGuid().ToString("N")[..12];
            using var usage = new Usage();
            using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
            IFlowCompletion completion;
            if (request.Provider == "openai") completion = new OpenAiFlowCompletion(http, () => key, "medium", 4096);
            else
            {
                var options = new LmStudioOptions { Endpoint = new Uri("http://127.0.0.1:1234/v1/"), GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
                http.BaseAddress = options.Endpoint;
                completion = new FlowModelCompletion(new LmStudioClient(http, options), options.Model);
            }
            var start = Stopwatch.GetTimestamp(); string status = "valid", error = ""; string? output = null;
            try
            {
                output = await completion.CompleteAsync(request, token);
                if (string.IsNullOrWhiteSpace(output)) throw new InvalidDataException("Empty generation");
                return output;
            }
            catch (Exception e)
            {
                status = Classify(e); error = e is HttpRequestException h ? $"HTTP:{(int?)h.StatusCode}" : e.GetType().Name;
                Failures.Enqueue(status); throw;
            }
            finally
            {
                await write(new
                {
                    type = "call",
                    executionId,
                    blindId,
                    phase,
                    id,
                    repetition,
                    generator,
                    model = request.Model,
                    node = request.NodeId,
                    detector,
                    status,
                    error,
                    output,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    usage = usage.Value,
                    system = request.System,
                    inputs = request.Inputs,
                    schema = request.ResponseSchema
                });
            }
        }
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t)
        {
            var response = await base.SendAsync(r, t);
            if (response.IsSuccessStatusCode)
            {
                using var d = JsonDocument.Parse(await response.Content.ReadAsStringAsync(t));
                if (d.RootElement.TryGetProperty("usage", out var u)) Value = u.Clone();
            }
            return response;
        }
    }
}
