using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

// Harness only. No registration in production or experimental enforcement.
public sealed class PersonalAttributionSpikeLive
{
    internal sealed record Case(string Id, string Candidate, bool Allow, string Category);
    internal sealed record Corpus(string Source, string Context, Case[] Cases);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    internal static async Task<JsonElement> Inputs(string source, string context, string candidate, CancellationToken token)
    {
        const string scope = "whatsapp-dm-5511944558218-s-whatsapp-net";
        var registry = new BoundaryExecutionRegistry(scope);
        var memory = new MemoryItem("synthetic-spike", scope, source, MemoryCategory.Boundary,
            [MemoryIdentity.Person(scope, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact");
        var now = DateTimeOffset.UtcNow;
        var compiled = await registry.CompileAsync(registry.RegisterMemories([memory]), new("v1", "v1", "v1"), now, token);
        var safe = registry.ForModel(compiled, now, new Dictionary<string, string>())[0];
        return JsonSerializer.SerializeToElement(new
        {
            resposta = candidate,
            limite = safe,
            mensagens = new[] { new { remetente = safe.GetProperty("subject"), texto = context } }
        });
    }

    internal static FlowCompletionRequest Request(string strategy, JsonElement inputs) => new(
        strategy == "A" ? "protected-personal-attribution-v1" : "protected-adversarial-verifier-v1",
        PersonalAttributionSpike.Contract(strategy), inputs, PersonalAttributionSpike.Schema(strategy).ToJsonSchema(),
        strategy == "A" ? "gpt-5.6-luna" : "gpt-5.6-terra", "openai", null,
        ReasoningEffort: "medium", MaxOutputTokens: 1024);

    [Fact]
    public async Task Requests_use_real_compiler_and_only_minimized_sources()
    {
        var inputs = await Inputs("Helena pediu para Filomena não brincar nem provocar sobre trabalho.",
            "Preciso corrigir um relatório.", "Vamos resolver.", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "resposta", "limite", "mensagens" }, inputs.EnumerateObject().Select(p => p.Name));
        Assert.Equal("trabalho", inputs.GetProperty("limite").GetProperty("compiled_boundary").GetProperty("topic").GetString());
        Assert.Equal("no_teasing_about_topic:v1", inputs.GetProperty("limite").GetProperty("compiled_boundary").GetProperty("policy_template").GetString());
        Assert.DoesNotContain("synthetic-spike", inputs.GetRawText());
        Assert.DoesNotContain("5511944558218", inputs.GetRawText());
        Assert.DoesNotContain("_boundary", inputs.GetProperty("limite").GetProperty("subject").GetRawText());
        Assert.Equal(Request("A", inputs).Inputs.GetRawText(), Request("C", inputs).Inputs.GetRawText());
    }

    [Fact]
    public async Task Run_isolated_comparison()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_ATTRIBUTION_SPIKE") != "1", "Opt-in synthetic A Luna / C Terra spike only.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY absent; remote spike unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_SPIKE_ROOT") ?? throw new InvalidOperationException("Root required.");
        var token = TestContext.Current.CancellationToken;
        var corpus = JsonSerializer.Deserialize<Corpus>(await File.ReadAllTextAsync(Path.Combine(root, "tests/fixtures/personal-attribution-spike.json"), token), JsonOptions)!;
        var path = Path.Combine(root, "docs", $"personal-attribution-spike-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        await using var log = new StreamWriter(path) { AutoFlush = true };
        var meta = new
        {
            type = "metadata",
            source = corpus.Source,
            context = corpus.Context,
            repetitions = 5,
            fixtures = corpus.Cases.Length,
            models = new { A = "gpt-5.6-luna", C = "gpt-5.6-terra" },
            effort = "medium",
            maxOutputTokens = 1024,
            retries = 0,
            contracts = new[] { "A", "C" }.Select(s => new { strategy = s, prompt = PersonalAttributionSpike.Contract(s), schema = PersonalAttributionSpike.Schema(s).ToJsonSchema() })
        };
        await log.WriteLineAsync(JsonSerializer.Serialize(meta));
        TestContext.Current.TestOutputHelper!.WriteLine("SPIKE_RESULTS=" + path);
        for (var repetition = 1; repetition <= 5; repetition++)
            foreach (var item in corpus.Cases)
            {
                // Identical sources, separate requests/handlers. At most two HTTP calls in flight.
                var inputs = await Inputs(corpus.Source, corpus.Context, item.Candidate, token);
                var order = repetition % 2 == 0 ? new[] { "C", "A" } : new[] { "A", "C" };
                var pair = await Task.WhenAll(order.Select(s => Call(s, item, repetition, inputs, key!, token)));
                foreach (var row in pair) await log.WriteLineAsync(JsonSerializer.Serialize(row));
                // A missing/inaccessible provider stops the spike; never substitute another model.
                if (pair.Any(r => PersonalAttributionResume.Technical(r.Status)))
                    throw new InvalidOperationException("Spike provider/transport unavailable; partial evidence preserved.");
            }
    }

    internal sealed record Row(string Strategy, string Model, string Id, string Category, int Repetition,
        string Candidate, bool ExpectedAllow, string Status, string? Outcome, JsonElement? Features,
        string? RawOutput, string? Error, double Milliseconds, JsonElement? Usage);

    internal static async Task<Row> Call(string strategy, Case item, int repetition, JsonElement inputs, string key, CancellationToken token)
    {
        using var usage = new UsageHandler();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var request = Request(strategy, inputs);
        var completion = new OpenAiFlowCompletion(http, () => key, "medium", 1024);
        string status = "valid";
        string? outcome = null, raw = null, error = null;
        JsonElement? features = null;
        var start = Stopwatch.GetTimestamp();
        try
        {
            raw = await completion.CompleteAsync(request, token);
            using var parsed = JsonDocument.Parse(raw);
            features = parsed.RootElement.Clone();
            outcome = PersonalAttributionSpike.Evaluate(strategy, features.Value);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            status = "invalid_output"; error = e.GetType().Name;
        }
        catch (HttpRequestException e)
        {
            status = e.StatusCode is null ? "transport_failure" : "provider_failure";
            error = $"http_{(int?)e.StatusCode}:{e.HttpRequestError}";
        }
        catch (OperationCanceledException e)
        {
            status = e.InnerException is TimeoutException ? "timeout" : "transport_failure"; error = "transport_cancelled";
        }
        return new(strategy, request.Model!, item.Id, item.Category, repetition, item.Candidate, item.Allow,
            status, outcome, features, raw, error, Stopwatch.GetElapsedTime(start).TotalMilliseconds, usage.Last);
    }

    private sealed class UsageHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Last { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (parsed.RootElement.TryGetProperty("usage", out var usage)) Last = usage.Clone();
            }
            return response;
        }
    }
}
