using System.Net;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class FlowTargetTests
{
    private static readonly JsonElement TypedSchema = Json("""
        {"type":"object","additionalProperties":false,"properties":{"acao":{"type":"string","enum":["responder","calar"]}},"required":["acao"]}
        """);
    private const string JevResponse = """
        {"model":"jev-1.13.0","answers":{"acao":{"type":"choice","choice":"responder","confidence":0.9,"probabilities":{"responder":0.9,"calar":0.1}}},"usage":{"input_tokens":20,"output_tokens":1}}
        """;

    [Fact]
    public async Task Luna_uses_real_adapter_and_pinned_parameters_without_starting_chat()
    {
        using var transport = new CaptureHandler(OpenAiResponse("{\"acao\":\"responder\"}"));
        using var http = new HttpClient(transport);
        var credentialReads = 0;
        var factory = Factory(http, () => { credentialReads++; return "test-only"; });
        var profile = Profile("openai");
        var completion = factory.Create(profile, Access());
        Assert.Equal(0, credentialReads);

        var output = await completion.CompleteAsync(Request(profile), TestContext.Current.CancellationToken);

        Assert.Equal("responder", Json(output).GetProperty("acao").GetString());
        Assert.Equal(1, credentialReads);
        var body = Assert.Single(transport.Bodies);
        Assert.Equal("high", body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(256, body.GetProperty("max_output_tokens").GetInt32());
        Assert.False(body.GetProperty("store").GetBoolean());
        Assert.Equal("gpt-6-luna", body.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Jev_preserves_actual_choice_override_in_wire_request()
    {
        using var transport = new CaptureHandler(JevResponse);
        using var http = new HttpClient(transport);
        var profile = Profile("jev", questions: [new("acao", "Override explícito", new Dictionary<string, string>
        { ["responder"] = "contribuição útil", ["calar"] = "sem contribuição" })]);
        var output = await Factory(http).Create(profile, Access()).CompleteAsync(Request(profile), TestContext.Current.CancellationToken);

        Assert.Equal("responder", Json(output).GetProperty("acao").GetString());
        var question = Assert.Single(transport.Bodies).GetProperty("questions").GetProperty("acao");
        var instructions = question.GetProperty("instructions");
        Assert.Equal("Override explícito", instructions.GetProperty("choiceGuidance").GetString());
        Assert.Equal(profile.Prompt.Payload.GetProperty("system").GetString(), instructions.GetProperty("task").GetString());
        Assert.Equal("contribuição útil", question.GetProperty("criteria").GetProperty("responder").GetString());
    }

    [Theory]
    [InlineData("model")]
    [InlineData("prompt")]
    [InlineData("schema")]
    [InlineData("effort")]
    [InlineData("tokens")]
    public async Task Request_cannot_override_pinned_profile(string mutation)
    {
        using var transport = new CaptureHandler("{}");
        using var http = new HttpClient(transport);
        var profile = Profile("openai");
        var request = Request(profile);
        request = mutation switch
        {
            "model" => request with { Model = "another" },
            "prompt" => request with { System = "another" },
            "schema" => request with { ResponseSchema = Json("{}") },
            "effort" => request with { ReasoningEffort = "max" },
            _ => request with { MaxOutputTokens = 999 }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => Factory(http).Create(profile, Access()).CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(transport.Bodies);
    }

    [Theory]
    [InlineData("https://untrusted.example/")]
    [InlineData("missing")]
    public void Candidate_cannot_supply_endpoint_or_unknown_alias(string alias)
    {
        using var transport = new CaptureHandler("{}");
        using var http = new HttpClient(transport);
        Assert.Throws<InvalidDataException>(() => Factory(http).Create(Profile("jev", alias: alias), Access()));
        Assert.Empty(transport.Bodies);
    }

    [Fact]
    public void Proposer_cannot_create_execution_adapter()
    {
        using var transport = new CaptureHandler("{}");
        using var http = new HttpClient(transport);
        var proposer = EvaluationAccess.FromHost("proposer", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Proposer);
        Assert.Throws<UnauthorizedAccessException>(() => Factory(http).Create(Profile("jev"), proposer));
    }

    [Fact]
    public async Task Typed_with_summary_uses_both_real_adapters_and_summary_pin()
    {
        using var typedTransport = new CaptureHandler(JevResponse);
        using var summaryTransport = new CaptureHandler(OpenAiResponse("{\"resumo\":\"Uma pergunta direta.\"}"));
        using var typedHttp = new HttpClient(typedTransport);
        using var summaryHttp = new HttpClient(summaryTransport);
        var profile = CompositeProfile();
        var factory = new EvaluationProviderFactory([
            new("jev-host", "jev", new("https://api.typesafe.ai/v1/systemone"), typedHttp, () => "test"),
            new("openai-host", "openai", new("https://api.openai.com/v1/responses"), summaryHttp, () => "test")]);

        var output = Json(await factory.Create(profile, Access()).CompleteAsync(Request(profile), TestContext.Current.CancellationToken));

        Assert.Equal("responder", output.GetProperty("acao").GetString());
        Assert.Equal("Uma pergunta direta.", output.GetProperty("resumo").GetString());
        Assert.Single(typedTransport.Bodies);
        var summaryBody = Assert.Single(summaryTransport.Bodies);
        Assert.Contains("Resumo pinado", summaryBody.GetProperty("instructions").GetString());
        Assert.Single(summaryBody.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public async Task Failed_summary_does_not_return_partial_typed_success()
    {
        using var typedTransport = new CaptureHandler(JevResponse);
        using var summaryTransport = new CaptureHandler("{}", HttpStatusCode.ServiceUnavailable);
        using var typedHttp = new HttpClient(typedTransport);
        using var summaryHttp = new HttpClient(summaryTransport);
        var profile = CompositeProfile();
        var factory = new EvaluationProviderFactory([
            new("jev-host", "jev", new("https://api.typesafe.ai/v1/systemone"), typedHttp, () => "test"),
            new("openai-host", "openai", new("https://api.openai.com/v1/responses"), summaryHttp, () => "test")]);
        await Assert.ThrowsAsync<HttpRequestException>(() => factory.Create(profile, Access()).CompleteAsync(Request(profile), TestContext.Current.CancellationToken));
        Assert.Single(typedTransport.Bodies);
        Assert.Single(summaryTransport.Bodies);
    }

    private static ConfigurationProfile CompositeProfile()
    {
        var full = Json("""
            {"type":"object","additionalProperties":false,"properties":{"acao":{"type":"string","enum":["responder","calar"]},"resumo":{"type":"string"}},"required":["acao","resumo"]}
            """);
        var summarySchema = Json("""
            {"type":"object","additionalProperties":false,"properties":{"resumo":{"type":"string"}},"required":["resumo"]}
            """);
        var summary = new SummaryModelProfile(1, "openai", "gpt-6-luna", "gpt-6-luna", "openai-host",
            DateTimeOffset.UnixEpoch, Json("{\"reasoningEffort\":\"high\",\"maxOutputTokens\":256}"),
            SchemaDocument(summarySchema), new(1, "prompt", Json("{\"system\":\"Resumo pinado\"}")));
        return Profile("jev", schema: full, summary: summary);
    }

    private static EvaluationProviderFactory Factory(HttpClient http, Func<string?>? credential = null) => new([
        new("openai-host", "openai", new("https://api.openai.com/v1/responses"), http, credential ?? (() => "test")),
        new("jev-host", "jev", new("https://api.typesafe.ai/v1/systemone"), http, credential ?? (() => "test"))]);

    private static ConfigurationProfile Profile(string provider, string? alias = null,
        IReadOnlyList<ChoiceQuestionProfile>? questions = null, JsonElement? schema = null, SummaryModelProfile? summary = null) => new(
        1, summary is null ? "completion" : "typed-with-summary", provider,
        provider == "openai" ? "gpt-6-luna" : "jev-1.13.0", alias ?? provider + "-host",
        provider == "openai" ? "gpt-6-luna" : "jev-1.13.0", DateTimeOffset.UnixEpoch,
        Json(provider == "openai" ? "{\"reasoningEffort\":\"high\",\"maxOutputTokens\":256}" : "{}"),
        provider == "openai" ? null : 0.5, questions ?? [], SchemaDocument(schema ?? TypedSchema),
        new(1, "prompt", Json("{\"system\":\"Instrução pinada\"}")), summary);

    private static RevisionDocument SchemaDocument(JsonElement schema) => new(1, "schema",
        JsonSerializer.SerializeToElement(new { contractVersion = 1, responseSchema = schema }));
    private static FlowCompletionRequest Request(ConfigurationProfile profile) => new("case-step",
        profile.Prompt.Payload.GetProperty("system").GetString()!, Json("{\"mensagem\":\"Oi\"}"),
        profile.Schema.Payload.GetProperty("responseSchema"), profile.Model, profile.Provider, null);
    private static EvaluationAccess Access() => EvaluationAccess.FromHost("evaluator", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static string OpenAiResponse(string output) => JsonSerializer.Serialize(new
    {
        status = "completed",
        model = "gpt-6-luna",
        output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = output } } } }
    });

    private sealed class CaptureHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(Json(await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new(status) { Content = new StringContent(response) };
        }
    }
}
