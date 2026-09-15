using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

internal sealed record HardCase(string Id, string Candidate, string Expected, string Rationale, string? Context);

internal sealed record ValidatorHardSet(string Boundary, string Context, HardCase[] Cases)
{
    public static ValidatorHardSet Load() => JsonSerializer.Deserialize<ValidatorHardSet>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "validator-hardset.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public FlowCompletionRequest Request(HardCase item, string model) => new(
        "validator", BoundaryValidatorProbeTests.Prompt,
        JsonSerializer.SerializeToElement(new
        {
            resposta = item.Candidate,
            limites = new[] { new { sobre = new[] { new { nome = "Helena", @ref = "p1" } },
                categoria = "limite", conteudo = Boundary } },
            mensagens = new[] { new { texto = item.Context ?? Context, remetente = new { nome = "Helena", @ref = "p1" } } }
        }), BoundaryValidatorProbeTests.Contract.ToJsonSchema(), model, "openai", null);
}

public sealed class ValidatorHardSetLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_HARDSET") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in synthetic paid OpenAI hard-set benchmark.")]
    public async Task LunaVersusTerra()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) Assert.Skip("OPENAI_API_KEY absent; OpenAI unavailable.");
        var token = TestContext.Current.CancellationToken;
        var suite = ValidatorHardSet.Load();
        using var handler = new UsageHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new OpenAiFlowCompletion(http, () => key, "medium", 1024);
        // New file per run: partial results survive interruption. Only synthetic inputs and usage.
        var path = Path.Combine(AppContext.BaseDirectory, $"validator-hardset-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        TestContext.Current.TestOutputHelper!.WriteLine("HARDSET_RESULTS=" + path);
        var sequence = 0;
        for (var repetition = 1; repetition <= 5; repetition++)
        {
            foreach (var item in suite.Cases)
            {
                var models = (sequence++ % 2 == 0)
                    ? new[] { "gpt-5.6-luna", "gpt-5.6-terra" }
                    : new[] { "gpt-5.6-terra", "gpt-5.6-luna" };
                foreach (var model in models)
                {
                    handler.Usage = null;
                    var start = Stopwatch.GetTimestamp();
                    string? actual = null, explanation = null, failure = null;
                    var status = "valid";
                    try
                    {
                        var raw = await completion.CompleteAsync(suite.Request(item, model), token);
                        using var json = JsonDocument.Parse(raw);
                        FlowDomain.Check(json.RootElement, BoundaryValidatorProbeTests.Contract);
                        if (json.RootElement.EnumerateObject().Count() != 2) throw new InvalidDataException();
                        actual = json.RootElement.GetProperty("resultado").GetString();
                        explanation = json.RootElement.GetProperty("explicacao").GetString();
                    }
                    catch (HttpRequestException e)
                    {
                        // Access/transport errors are not model quality and must not trigger fallback.
                        await log.WriteLineAsync(JsonSerializer.Serialize(new { model, status = "unavailable", httpStatus = (int?)e.StatusCode }));
                        throw;
                    }
                    catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                    {
                        status = "invalid";
                        failure = e.GetType().Name;
                    }
                    await log.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        model,
                        fixture = item.Id,
                        repetition,
                        candidate = item.Candidate,
                        expected = item.Expected,
                        actual,
                        explanation,
                        status,
                        failure,
                        milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        usage = handler.Usage,
                        reasoningEffort = "medium",
                        maxOutputTokens = 1024
                    }));
                }
            }
        }
    }

    private sealed class UsageHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        public JsonElement? Usage { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (parsed.RootElement.TryGetProperty("usage", out var usage)) Usage = usage.Clone();
            }
            return response;
        }
    }
}
