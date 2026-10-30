using System.Net;
using Cortex.Core.Flows;
using Cortex.Inference.OpenJev;
using Cortex.Inference.SystemOne;
using Xunit;
using static Cortex.TestSupport.SystemOneTestSupport;

namespace Cortex.Inference.Tests;

public sealed class OpenJevFlowCompletionTests
{
    private const string Model = "gemma-3-1b";
    private const string Schema = """
        {"type":"object","properties":{"acao":{"type":"string","enum":["responder","calar"],"description":"What to do next"},"cuidado":{"type":"boolean"}},"required":["acao","cuidado"],"additionalProperties":false}
        """;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static FlowCompletionRequest Request(string schema = Schema, string provider = "openjev", string model = Model) =>
        new("situation", "Classify only.", Json("{\"mensagens\":[\"Oi\"]}"), Json(schema), model, provider, null);

    /// <summary>Two-option chat whose softmax gives P(A) = pA exactly enough for threshold tests.</summary>
    private static string Binary(double pA, int prompt = 10) =>
        FakeLlamaServer.Chat(Model, [("A", Math.Log(pA)), ("B", Math.Log(1 - pA))], prompt: prompt);

    private static FakeLlamaServer Server(params string[] chats)
    {
        var server = new FakeLlamaServer();
        foreach (var chat in chats) server.Chats.Enqueue((HttpStatusCode.OK, chat));
        return server;
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.7, false)]
    public async Task Maps_enum_and_boolean_fields_with_threshold(double threshold, bool expected)
    {
        var server = Server(Binary(0.2), Binary(0.6));
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), threshold);
        var output = Json(await adapter.CompleteAsync(Request(), Token));
        Assert.Equal("calar", output.GetProperty("acao").GetString());
        Assert.Equal(expected, output.GetProperty("cuidado").GetBoolean());
        Assert.Equal(2, server.ChatCalls);
        var chats = server.Requests.Where(r => r.Path == "/v1/chat/completions").Select(r => r.Body.GetProperty("messages")[1]
            .GetProperty("content").GetString()).ToArray();
        Assert.Equal("State:\n{\"mensagens\":[\"Oi\"]}\n\nQuestion:\nClassify only.\n\nField: acao\nWhat to do next\n\nAllowed options:\nA. responder\nB. calar\n\nReply with exactly one option letter from: A, B.", chats[0]);
        Assert.Equal("State:\n{\"mensagens\":[\"Oi\"]}\n\nQuestion:\nClassify only.\n\nField: cuidado\ncuidado\n\nAllowed options:\nA. true\nB. false\n\nReply with exactly one option letter from: A, B.", chats[1]);
    }

    [Fact]
    public async Task String_state_is_sent_verbatim()
    {
        var server = Server(Binary(0.9));
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5);
        var request = Request("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""")
            with { Inputs = Json("\"plain text\\nstate\"") };
        await adapter.CompleteAsync(request, Token);
        Assert.StartsWith("State:\nplain text\nstate\n\nQuestion:", server.LastChat.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Structured_state_is_compact_json_without_escaping_non_ascii_or_quotes()
    {
        var server = Server(Binary(0.9));
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5);
        var request = Request("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""")
            with { Inputs = Json("{ \"m\" : \"ação \\\"x\\\" <b>&'\" }") };
        await adapter.CompleteAsync(request, Token);
        Assert.StartsWith("State:\n{\"m\":\"ação \\\"x\\\" <b>&'\"}\n\nQuestion:",
            server.LastChat.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Undefined_state_is_rejected_without_calls()
    {
        var server = Server();
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5);
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request() with { Inputs = default }, Token));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Choice_profile_descriptions_become_option_text()
    {
        var server = Server(Binary(0.8), Binary(0.5));
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5,
            choiceQuestions: new Dictionary<string, SystemOneChoiceQuestion>
            { ["acao"] = new("Choose participation only.", new Dictionary<string, string> { ["responder"] = "Address the speaker", ["calar"] = "calar" }) });
        await adapter.CompleteAsync(Request(), Token);
        var first = server.Requests.First(r => r.Path == "/v1/chat/completions").Body.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.Contains("Question:\nClassify only.\n\nField: acao\nWhat to do next\n\nChoose participation only.\n\nAllowed options:\nA. responder: Address the speaker\nB. calar\n\n", first);
    }

    [Fact]
    public async Task Observation_uses_jev_answer_shape_and_policy()
    {
        var server = Server(Binary(0.75, prompt: 30), Binary(0.4, prompt: 12));
        using var http = new HttpClient(server);
        SystemOneResult? trace = null;
        var decisions = new List<(string Node, string Field, OpenJevDecision Decision)>();
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5, (_, result) => trace = result,
            observeDecision: (node, field, decision) => decisions.Add((node, field, decision)));
        await adapter.CompleteAsync(Request(), Token);
        Assert.NotNull(trace);
        Assert.Equal("OpenJevLabelSoftmax", trace.ProbabilityPolicy);
        Assert.Equal(Model, trace.Model);
        Assert.Equal(42, trace.InputTokens);
        Assert.Equal(2, trace.OutputTokens);
        Assert.Equal(decisions.Aggregate(TimeSpan.Zero, (sum, d) => sum + d.Decision.Elapsed), trace.Elapsed);
        var expected = Json("""
            {"acao":{"type":"choice","choice":"responder","probabilities":{"responder":0.75,"calar":0.25},"confidence":0.75},
             "cuidado":{"type":"noul","noul":0.4}}
            """);
        Assert.Equal(["acao", "cuidado"], trace.Answers.EnumerateObject().Select(p => p.Name));
        var acao = trace.Answers.GetProperty("acao");
        Assert.Equal(["type", "choice", "probabilities", "confidence"], acao.EnumerateObject().Select(p => p.Name));
        Assert.Equal("choice", acao.GetProperty("type").GetString());
        Assert.Equal("responder", acao.GetProperty("choice").GetString());
        Assert.Equal(0.75, acao.GetProperty("probabilities").GetProperty("responder").GetDouble(), 1e-12);
        Assert.Equal(0.25, acao.GetProperty("probabilities").GetProperty("calar").GetDouble(), 1e-12);
        Assert.Equal(0.75, acao.GetProperty("confidence").GetDouble(), 1e-12);
        var cuidado = trace.Answers.GetProperty("cuidado");
        Assert.Equal(["type", "noul"], cuidado.EnumerateObject().Select(p => p.Name));
        Assert.Equal("noul", cuidado.GetProperty("type").GetString());
        Assert.Equal(0.4, cuidado.GetProperty("noul").GetDouble(), 1e-12);
        Assert.Equal([("situation", "acao"), ("situation", "cuidado")], decisions.Select(d => (d.Node, d.Field)));
        Assert.Equal(expected.GetProperty("acao").GetProperty("choice").GetString(), decisions[0].Decision.Options[0].Text);
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}},\"required\":[\"x\"],\"additionalProperties\":false}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"boolean\"},\"y\":{\"type\":\"boolean\"}},\"required\":[\"x\"],\"additionalProperties\":false}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10}},\"required\":[\"score\"],\"additionalProperties\":false}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\",\"enum\":[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\",\"j\",\"k\",\"l\",\"m\",\"n\",\"o\",\"p\",\"q\",\"r\",\"s\",\"t\",\"u\"]}},\"required\":[\"x\"],\"additionalProperties\":false}")]
    public async Task Unsupported_schema_is_rejected_without_calls(string schema)
    {
        var server = Server();
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5);
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request(schema), Token));
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("laya", Model, 0.5)]
    [InlineData("openjev", "", 0.5)]
    [InlineData("openjev", Model, 0)]
    [InlineData("openjev", Model, 1)]
    [InlineData("openjev", Model, double.NaN)]
    public async Task Requires_openjev_provider_model_and_threshold(string provider, string model, double threshold)
    {
        var server = Server();
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), threshold);
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.CompleteAsync(Request(provider: provider, model: model), Token));
        Assert.Empty(server.Requests);
    }
}
