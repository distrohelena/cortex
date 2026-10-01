using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextFidelityCompositionLiveTests
{
    [Fact]
    public async Task One_B_execution_with_frozen_enforcement()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_CONTEXT_FIDELITY_COMPOSITION") != "1", "Explicit synthetic paid opt-in only.");
        var token = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_CONTEXT_FIDELITY_ROOT")!;
        var id = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_CONTEXT_FIDELITY_CASE")!);
        var repetition = int.Parse(Environment.GetEnvironmentVariable("FILOMENA_CONTEXT_FIDELITY_REPETITION")!);
        Assert.Contains(id, new[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.InRange(repetition, 1, 2);
        var raw = await File.ReadAllTextAsync(Path.Combine(root, "docs/context-fidelity-composition-inputs.json"), token);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        using var data = JsonDocument.Parse(raw);
        var d = data.RootElement;
        var inputs = d.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == id).GetProperty("inputs").Clone();
        var prior = new List<JsonElement>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "context-fidelity-composition-attempts-*.jsonl"))
            foreach (var line in await File.ReadAllLinesAsync(file, token))
            {
                using var parsed = JsonDocument.Parse(line);
                if (parsed.RootElement.GetProperty("type").GetString() == "execution") prior.Add(parsed.RootElement.Clone());
            }
        Assert.All(prior, p => Assert.Equal(fingerprint, p.GetProperty("fingerprint").GetString()));
        var cell = prior.Where(p => p.GetProperty("id").GetInt32() == id).ToArray();
        Assert.False(cell.Any(p => p.GetProperty("repetition").GetInt32() == repetition && p.GetProperty("status").GetString() == "valid"), "Already completed; no duplicate paid run.");
        Assert.True(cell.Count(p => p.GetProperty("status").GetString() != "valid") < 3, "Technical failure ceiling.");
        var executionId = Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, "docs", $"context-fidelity-composition-attempts-{id}-{repetition}-{executionId}.jsonl");
        await using var writer = new StreamWriter(path) { AutoFlush = true };
        using var gate = new SemaphoreSlim(1);
        async Task Write(object row)
        {
            await gate.WaitAsync(token);
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(row)); }
            finally { gate.Release(); }
        }
        var measured = new Measured(key!, Write, executionId);
        var start = Stopwatch.GetTimestamp();
        GroundingComposition.Result? result = null;
        var status = "valid"; string? error = null;
        try
        {
            result = await GroundingComposition.Run(id, inputs, d.GetProperty("generationPrompt").GetString()!,
                d.GetProperty("regenerationPrompt").GetString()!, d.GetProperty("clause").GetString()!,
                d.GetProperty("maxOutputTokens").GetInt32(), measured, token);
            if (result.Final is "EnforcementFailure" or "GenerationFailure")
                status = measured.Failures.LastOrDefault() ?? "invalid_output";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            status = GeneratorComparisonLiveTests.Classify(e); error = e.GetType().Name;
        }
        await Write(new
        {
            type = "execution",
            executionId,
            fingerprint,
            id,
            repetition,
            status,
            error,
            result,
            milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds
        });
        TestContext.Current.TestOutputHelper!.WriteLine("GROUNDING_COMPOSITION_FILE=" + path);
        Assert.True(status == "valid", "Attempt preserved; explicit technical resume only.");
    }

    private sealed class Measured(string key, Func<object, Task> write, string executionId) : IFlowCompletion
    {
        internal ConcurrentQueue<string> Failures = new();
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Assert.Equal("openai", request.Provider);
            Assert.Equal("gpt-5.6-luna", request.Model);
            Assert.Equal("medium", request.ReasoningEffort);
            using var usage = new Usage();
            using var http = new HttpClient(usage) { Timeout = TimeSpan.FromSeconds(60) };
            var completion = new OpenAiFlowCompletion(http, () => key);
            var start = Stopwatch.GetTimestamp();
            var status = "valid"; string? output = null; string? error = null;
            try
            {
                output = await completion.CompleteAsync(request, token);
                if (string.IsNullOrWhiteSpace(output)) throw new InvalidDataException("Empty output.");
                return output;
            }
            catch (Exception e)
            {
                status = GeneratorComparisonLiveTests.Classify(e);
                error = e is HttpRequestException h ? $"HTTP:{(int?)h.StatusCode}" : e.GetType().Name;
                Failures.Enqueue(status); throw;
            }
            finally
            {
                await write(new
                {
                    type = "call",
                    executionId,
                    node = request.NodeId,
                    status,
                    error,
                    output,
                    model = request.Model,
                    effort = request.ReasoningEffort,
                    budget = request.MaxOutputTokens,
                    system = request.System,
                    inputs = request.Inputs,
                    schema = request.ResponseSchema,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    usage = usage.Value
                });
            }
        }
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var d = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (d.RootElement.TryGetProperty("usage", out var u)) Value = u.Clone();
            }
            return response;
        }
    }
}


