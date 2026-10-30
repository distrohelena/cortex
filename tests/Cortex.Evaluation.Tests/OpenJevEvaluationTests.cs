using System.Net;
using System.Text;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class OpenJevEvaluationTests
{
    private const string Model = "minicpm5-2b";
    private static readonly Uri Base = new("http://127.0.0.1:8080/");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://api.typesafe.ai/")]
    [InlineData("http://192.0.2.1:8080/")]
    [InlineData("ftp://127.0.0.1/")]
    [InlineData("http://user@127.0.0.1:8080/")]
    [InlineData("http://127.0.0.1:8080/?x=1")]
    [InlineData("http://127.0.0.1:8080/v1")]
    public void Openjev_endpoint_must_be_loopback(string endpoint)
    {
        using var http = new HttpClient();
        Assert.Throws<InvalidDataException>(() => new EvaluationProviderFactory([
            new("openjev-host", "openjev", new(endpoint), http, () => null)]));
        _ = new EvaluationProviderFactory([new("openjev-host", "openjev", Base, http, () => null)]);
        _ = new EvaluationProviderFactory([new("openjev-host", "openjev", new("http://localhost:8080/"), http, () => null)]);
    }

    [Fact]
    public async Task Openjev_handler_journals_tokenize_and_chat_and_rejects_other_paths()
    {
        var journal = new Journal();
        using var server = new FakeLlamaServer();
        server.Chats.Enqueue(Chat(-0.1, -2.4));
        using var http = new HttpClient(new EvaluationHttpHandler("openjev", Base, journal) { InnerHandler = server });

        using (await http.PostAsync(new Uri(Base, "tokenize"), Body($"{{\"model\":\"{Model}\",\"content\":\"A\"}}"), Token)) { }
        using (await http.PostAsync(new Uri(Base, "v1/chat/completions"), Body("{}"), Token)) { }
        await Assert.ThrowsAsync<InvalidDataException>(() => http.PostAsync(Base, Body("{}"), Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => http.PostAsync(new Uri(Base, "v1/completions"), Body("{}"), Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => http.GetAsync(new Uri(Base, "tokenize"), Token));

        Assert.Equal(2, server.Calls);
        Assert.All(journal.Intents, intent => Assert.Equal("openjev", intent.Provider));
        Assert.Equal(2, journal.Intents.Count);
        // Tokenization runs no inference: zero usage, attributed to the alias the client requested.
        Assert.Equal(new ProviderTokenUsage(0, 0, null, null), journal.Outcomes[0].Observation!.Usage);
        Assert.Equal("available", journal.Outcomes[0].Observation!.UsageStatus);
        Assert.Equal(Model, journal.Outcomes[0].Observation!.Model);
        Assert.Equal(new ProviderTokenUsage(40, 1, null, null), journal.Outcomes[1].Observation!.Usage);
        Assert.Equal(Model, journal.Outcomes[1].Observation!.Model);
    }

    [Fact]
    public async Task Openjev_profile_creates_typed_completion()
    {
        var journal = new Journal();
        using var server = new FakeLlamaServer();
        server.Chats.Enqueue(Chat(-2.4, -0.1));
        using var http = new HttpClient(new EvaluationHttpHandler("openjev", Base, journal) { InnerHandler = server });
        var factory = new EvaluationProviderFactory([new("openjev-host", "openjev", Base, http, () => null)]);
        var schema = Json("""
            {"type":"object","additionalProperties":false,"properties":{"acao":{"type":"string","enum":["responder","calar"]}},"required":["acao"]}
            """);
        var profile = new ConfigurationProfile(1, "completion", "openjev", Model, "openjev-host", Model,
            DateTimeOffset.UnixEpoch, Json("{}"), 0.5, [], new(1, "schema",
                JsonSerializer.SerializeToElement(new { contractVersion = 1, responseSchema = schema })),
            new(1, "prompt", Json("{\"system\":\"Instrução pinada\"}")), null);

        var output = Json(await factory.Create(profile, Access()).CompleteAsync(new("case-step", "Instrução pinada",
            Json("{\"mensagem\":\"Oi\"}"), schema, Model, "openjev", null), Token));

        Assert.Equal("calar", output.GetProperty("acao").GetString());
        // Two enum options: exactly two label tokenizations plus one decision.
        Assert.Equal(3, journal.Intents.Count);
        Assert.Equal(1, server.ChatCalls);
    }

    [Fact]
    public async Task Openjev_trial_aggregates_tokens_and_projects_zero_local_cost()
    {
        var journal = new Journal();
        using var server = new FakeLlamaServer();
        server.Chats.Enqueue(Chat(-2.4, -0.1));
        server.Chats.Enqueue(Chat(-0.2, -1.9));
        using var http = new HttpClient(new EvaluationHttpHandler("openjev", Base, journal) { InnerHandler = server });
        var factory = new EvaluationProviderFactory([new("openjev-host", "openjev", Base, http, () => null)]);
        var schema = Json("""
            {"type":"object","additionalProperties":false,"properties":{
              "acao":{"type":"string","enum":["responder","calar"]},
              "urgente":{"type":"boolean"}},"required":["acao","urgente"]}
            """);
        var profile = new ConfigurationProfile(1, "completion", "openjev", Model, "openjev-host", Model,
            DateTimeOffset.UnixEpoch, Json("{}"), 0.5, [], new(1, "schema",
                JsonSerializer.SerializeToElement(new { contractVersion = 1, responseSchema = schema })),
            new(1, "prompt", Json("{\"system\":\"Instrução pinada\"}")), null);
        await factory.Create(profile, Access()).CompleteAsync(new("case-step", "Instrução pinada",
            Json("{\"mensagem\":\"Oi\"}"), schema, Model, "openjev", null), Token);

        // Both fields share the alias's two labels: two tokenizations, two decisions.
        Assert.Equal(4, journal.Intents.Count);
        var trial = Guid.NewGuid();
        var at = DateTimeOffset.UnixEpoch.AddYears(56);
        var attempts = journal.Intents.Zip(journal.Outcomes, (intent, outcome) =>
        {
            var attempt = new PhysicalAttemptMeasurement(Guid.NewGuid(), trial, "observed", outcome, null)
                { Provider = intent.Provider, RequestSha256 = intent.RequestSha256, StartedAt = at };
            var terms = new AttemptPriceTerms(1, "openjev", Model, null, intent.RequestSha256, at.AddDays(-1), at.AddDays(1),
                0, 100_000, "USD", "https://local.invalid/openjev", at, 0m, 0m, 0m, true);
            var price = new RevisionRef(Guid.NewGuid(), RevisionHasher.Hash(terms.ToRevisionDocument()));
            return attempt with { Cost = AttemptPriceProjection.Calculate(attempt, price, terms) };
        }).ToArray();
        Assert.All(attempts, attempt => Assert.Equal("list_price_estimate", attempt.Cost!.Status));

        var measurement = TrialMeasurement.Aggregate(trial, TimeSpan.FromSeconds(1), attempts);

        Assert.Equal(4, measurement.PhysicalCalls);
        Assert.Equal(80, measurement.InputTokens);
        Assert.Equal(2, measurement.OutputTokens);
        Assert.Equal(0m, measurement.EstimatedCost);
        Assert.Equal("list_price_estimate", measurement.CostStatus);
        Assert.Equal("USD", measurement.Currency);
    }

    private static string Chat(double a, double b)
    {
        var top = new[] { new { id = 1000, token = "A", logprob = a }, new { id = 1001, token = "B", logprob = b } };
        var first = a >= b ? top[0] : top[1];
        return JsonSerializer.Serialize(new
        {
            model = Model,
            choices = new[] { new { index = 0, finish_reason = "length", message = new { role = "assistant", content = first.token },
                logprobs = new { content = new[] { new { first.id, first.token, first.logprob, top_logprobs = top } } } } },
            usage = new { prompt_tokens = 40, completion_tokens = 1 }
        });
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");
    private static EvaluationAccess Access() => EvaluationAccess.FromHost("evaluator", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private sealed class Journal : IEvaluationCallJournal
    {
        public List<PhysicalCallIntent> Intents { get; } = [];
        public List<PhysicalCallOutcome> Outcomes { get; } = [];
        public Task<Guid> BeginAsync(PhysicalCallIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            return Task.FromResult(Guid.NewGuid());
        }
        public Task FinishAsync(Guid attempt, PhysicalCallOutcome outcome, CancellationToken cancellationToken)
        {
            Outcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLlamaServer : HttpMessageHandler
    {
        public Queue<string> Chats { get; } = new();
        public int Calls { get; private set; }
        public int ChatCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/tokenize")
            {
                var label = Json(await request.Content!.ReadAsStringAsync(token)).GetProperty("content").GetString()!;
                return Reply(JsonSerializer.Serialize(new { tokens = new[] { 1000 + label[0] - 'A' } }));
            }
            Assert.Equal("/v1/chat/completions", path);
            ChatCalls++;
            return Reply(Chats.Dequeue());
        }
        private static HttpResponseMessage Reply(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
