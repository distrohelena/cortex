using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.SystemOne;
using Xunit;
using static Filomena.Llm.Tests.SystemOneClientTests;

namespace Filomena.Llm.Tests;

public sealed class SystemOneFlowCompletionTests
{
    private const string Schema = """
        {"type":"object","properties":{"acao":{"type":"string","enum":["responder","calar"]},"cuidado":{"type":"boolean"}},"required":["acao","cuidado"],"additionalProperties":false}
        """;
    private const string Answer = """
        {"model":"laya-rl-agent","answers":{"acao":{"type":"choice","choice":"responder","probabilities":{"responder":0.8,"calar":0.2},"confidence":0.8},"cuidado":{"type":"noul","noul":0.5}},"usage":{"input_tokens":12,"output_tokens":0}}
        """;
    private static FlowCompletionRequest Request(string schema = Schema, string provider = "laya", string model = "multilingual") =>
        new("situation", "Classify only.", Json("{\"mensagens\":[\"Oi\"]}"), Json(schema), model, provider, null);

    [Fact]
    public async Task Trusted_choice_profile_preserves_ids_and_sends_explicit_semantics()
    {
        using var handler = new Handler(Answer);
        using var http = new HttpClient(handler);
        var adapter = new SystemOneFlowCompletion(new(http, new("http://localhost/v1/systemone"), () => null, false), "laya", 0.5,
            choiceQuestions: new Dictionary<string, SystemOneChoiceQuestion>
            { ["acao"] = new("Choose participation only.", new Dictionary<string, string> { ["responder"] = "Address the speaker", ["calar"] = "Do not participate" }) });
        var output = Json(await adapter.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal("responder", output.GetProperty("acao").GetString());
        var question = handler.Body.GetProperty("questions").GetProperty("acao");
        var instructions = question.GetProperty("instructions");
        Assert.Equal("Choose participation only.", instructions.GetProperty("choiceGuidance").GetString());
        Assert.Equal("Classify only.", instructions.GetProperty("task").GetString());
        Assert.Equal("acao", instructions.GetProperty("field").GetString());
        Assert.Equal("Address the speaker", question.GetProperty("criteria").GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Trusted_profile_cannot_add_an_output_choice_absent_from_schema()
    {
        using var handler = new Handler(Answer);
        using var http = new HttpClient(handler);
        var adapter = new SystemOneFlowCompletion(new(http, new("http://localhost/v1/systemone"), () => null, false), "laya", 0.5,
            choiceQuestions: new Dictionary<string, SystemOneChoiceQuestion>
            { ["acao"] = new("Choose", new Dictionary<string, string> { ["responder"] = "Respond", ["extra"] = "Invalid" }) });
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("laya", "multilingual")]
    [InlineData("jev", "jev-latest")]
    public async Task Maps_typed_fields_without_text_generation_and_keeps_usage(string provider, string model)
    {
        using var handler = new Handler(Answer);
        using var http = new HttpClient(handler);
        SystemOneResult? trace = null;
        var adapter = new SystemOneFlowCompletion(new(http, new("http://localhost/v1/systemone"), () => null, false), provider, 0.5,
            (_, result) => trace = result);
        var result = Json(await adapter.CompleteAsync(Request(provider: provider, model: model), TestContext.Current.CancellationToken));
        Assert.Equal("responder", result.GetProperty("acao").GetString());
        Assert.True(result.GetProperty("cuidado").GetBoolean());
        Assert.Equal("noul", handler.Body.GetProperty("questions").GetProperty("cuidado").GetProperty("type").GetString());
        Assert.Equal("Classify only.", handler.Body.GetProperty("questions").GetProperty("acao").GetProperty("instructions").GetProperty("task").GetString());
        Assert.NotNull(trace);
        Assert.Equal(12, trace.InputTokens);
        Assert.Equal(0.5, trace.Answers.GetProperty("cuidado").GetProperty("noul").GetDouble());
    }

    [Theory]
    [InlineData("{\"type\":\"string\"}")]
    [InlineData("{\"type\":\"number\"}")]
    [InlineData("{\"type\":\"object\"}")]
    [InlineData("{\"type\":\"string\",\"enum\":[\"x\",\"x\"]}")]
    public async Task Rejects_unsupported_or_ambiguous_schema_before_network(string property)
    {
        using var handler = new Handler(Answer);
        using var http = new HttpClient(handler);
        var adapter = new SystemOneFlowCompletion(new(http, new("http://localhost/v1/systemone"), () => null, false), "laya", 0.5);
        var request = Request("{\"type\":\"object\",\"properties\":{\"x\":" + property + "},\"required\":[\"x\"],\"additionalProperties\":false}");
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("laya", "misspelled-model")]
    [InlineData("openai", "multilingual")]
    public async Task Does_not_allow_laya_silent_auto_route_on_bad_provider_or_model(string provider, string model)
    {
        using var handler = new Handler(Answer);
        using var http = new HttpClient(handler);
        var adapter = new SystemOneFlowCompletion(new(http, new("http://localhost/v1/systemone"), () => null, false), "laya", 0.5);
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request(provider: provider, model: model), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }
}
