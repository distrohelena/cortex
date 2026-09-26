using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class IronyReplayExperimentLiveTests
{
    [Fact]
    public async Task Compare_initial_and_later_context_without_runtime_or_transport()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_IRONY_REPLAY") != "1", "Explicit private offline-history model experiment.");
        var inputFile = Environment.GetEnvironmentVariable("FILOMENA_IRONY_INPUT") ?? throw new InvalidOperationException("Private corpus required.");
        var outputDir = Environment.GetEnvironmentVariable("FILOMENA_IRONY_OUTPUT") ?? throw new InvalidOperationException("Private output required.");
        Assert.True(Path.IsPathFullyQualified(inputFile) && Path.IsPathFullyQualified(outputDir) && Directory.Exists(outputDir));
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        var bytes = await File.ReadAllBytesAsync(inputFile, TestContext.Current.CancellationToken);
        var corpusHash = Convert.ToHexString(SHA256.HashData(bytes));
        var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(IronyReplayExperiment.Prompt)));
        using var corpus = JsonDocument.Parse(bytes);
        Assert.Equal("irony-replay:v1", corpus.RootElement.GetProperty("version").GetString());
        var cases = corpus.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(4, cases.Length);
        using var usage = new Usage();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key);
        var report = Path.Combine(outputDir, $"replay-{Guid.NewGuid():N}.jsonl");
        await using var stream = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        foreach (var fixture in cases)
            foreach (var revisit in new[] { false, true })
            {
                var input = IronyReplayExperiment.Input(fixture, revisit);
                var request = new FlowCompletionRequest("irony-replay-only", IronyReplayExperiment.Prompt, input,
                    IronyReplayExperiment.Schema, "gpt-6-luna", "openai", null, "medium", 2048);
                var started = Stopwatch.GetTimestamp();
                usage.Value = null;
                var output = await completion.CompleteAsync(request, TestContext.Current.CancellationToken);
                // Preserve before asserting. No automatic retry or selection of a preferred answer.
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    corpusHash,
                    promptHash,
                    id = fixture.GetProperty("id").GetString(),
                    revisit,
                    model = request.Model,
                    effort = request.ReasoningEffort,
                    input,
                    output,
                    milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    usage = usage.Value,
                    transportCalls = 0,
                    graphitiCalls = 0,
                    memoryWrites = 0,
                    runtimeExecutions = 0,
                    cost = (decimal?)null,
                    costNote = "No unverified price inferred."
                }));
                IronyReplayExperiment.Validate(output, input);
            }
        TestContext.Current.TestOutputHelper!.WriteLine($"Private replay report: {report}");
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (doc.RootElement.TryGetProperty("usage", out var usage)) Value = usage.Clone();
            }
            return response;
        }
    }
}
