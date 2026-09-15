using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Boundaries;
using Filomena.Core.Flows;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class BoundaryInterpreterBenchmarkTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_BOUNDARY_INTERPRETER_BENCHMARK") == "1";

    [Fact]
    public void Corpus_has_literal_gold_and_cannot_enter_interpreter_request()
    {
        var cases = Load();
        Assert.Equal(16, cases.Length);
        foreach (var item in cases)
        {
            var request = Request(item, "gpt-5.6-luna");
            Assert.Equal(new[] { "original_boundary" }, request.Inputs.EnumerateObject().Select(p => p.Name));
            Assert.Equal(request.Inputs.GetRawText(), Request(item, "gpt-5.6-terra").Inputs.GetRawText());
            Assert.Equal(item.GetProperty("expected").GetString(), BoundaryAdmission.Analyze(item.GetProperty("source").GetString()!).Status);
        }
    }

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in paid synthetic interpreter benchmark.")]
    public async Task Luna_and_Terra_same_corpus_three_repetitions()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) Assert.Skip("OPENAI_API_KEY absent.");
        using var usage = new Usage(new HttpClientHandler { AllowAutoRedirect = false });
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key);
        var rows = new List<object>();
        var path = Path.Combine(AppContext.BaseDirectory, "boundary-interpreter-results.json");
        foreach (var repetition in Enumerable.Range(1, 3))
            foreach (var item in Load())
                foreach (var model in new[] { "gpt-5.6-luna", "gpt-5.6-terra" })
                {
                    usage.Last = null;
                    var start = Stopwatch.GetTimestamp();
                    BoundaryProposal? proposal = null;
                    BoundaryAdmissionResult? admission = null;
                    string? error = null;
                    try
                    {
                        proposal = BoundaryInterpreter.Parse(await completion.CompleteAsync(Request(item, model), TestContext.Current.CancellationToken));
                        admission = BoundaryAdmission.Admit(item.GetProperty("source").GetString()!, proposal);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or JsonException)
                    {
                        error = exception.GetType().Name;
                    }
                    // Baseline has no corrective retry: invalid output is counted rather than hidden.
                    rows.Add(new
                    {
                        model,
                        repetition,
                        id = item.GetProperty("id").GetString(),
                        source = item.GetProperty("source").GetString(),
                        expected = item.GetProperty("expected").GetString(),
                        risk = item.TryGetProperty("risk", out var risk) ? risk.GetString() : null,
                        proposal,
                        admission,
                        error,
                        milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        usage = usage.Last
                    });
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
                    TestContext.Current.TestOutputHelper!.WriteLine($"INTERPRETER {model} {item.GetProperty("id").GetString()} {repetition}: {proposal?.Status}/{admission?.Status}");
                }
        TestContext.Current.TestOutputHelper!.WriteLine(path);
    }

    private static JsonElement[] Load() => JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "boundary-interpreter-corpus.json")))!;
    private static FlowCompletionRequest Request(JsonElement item, string model) => new("interpret-boundary", BoundaryInterpreter.Prompt,
        JsonSerializer.SerializeToElement(new { original_boundary = item.GetProperty("source").GetString() }), BoundaryInterpreter.Schema,
        model, "openai", null, "medium", 1024);

    private sealed class Usage(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public JsonElement? Last { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (document.RootElement.TryGetProperty("usage", out var usage)) Last = usage.Clone();
            }
            return response;
        }
    }
}
