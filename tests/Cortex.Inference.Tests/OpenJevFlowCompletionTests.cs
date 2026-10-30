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
        Assert.Equal("State:\n{\"mensagens\":[\"Oi\"]}\n\nQuestion:\nClassify only.\n\nWhat to do next\n\nAllowed options:\nA. responder\nB. calar\n\nReply with exactly one option letter from: A, B.", chats[0]);
        Assert.Equal("State:\n{\"mensagens\":[\"Oi\"]}\n\nQuestion:\nClassify only.\n\ncuidado\n\nAllowed options:\nA. Yes\nB. No\n\nReply with exactly one option letter from: A, B.", chats[1]);
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
        Assert.Equal("State:\n{\"mensagens\":[\"Oi\"]}\n\nQuestion:\nClassify only.\n\nWhat to do next\n\nChoose participation only.\n\nAllowed options:\nA. responder: Address the speaker\nB. calar\n\nReply with exactly one option letter from: A, B.", first);
    }

    [Theory]
    [InlineData("""{"type":"boolean","description":"Does the email ask for credentials?"}""", "Does the email ask for credentials?")]
    [InlineData("""{"type":"boolean","description":""}""", "asks_for_credentials")]
    [InlineData("""{"type":"boolean"}""", "asks_for_credentials")]
    public async Task Boolean_question_is_task_then_description_or_field_name_with_yes_no_options(string property, string line)
    {
        var server = Server(Binary(0.7));
        using var http = new HttpClient(server);
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.6);
        var output = Json(await adapter.CompleteAsync(Request(
            $$"""{"type":"object","properties":{"asks_for_credentials":{{property}}},"required":["asks_for_credentials"],"additionalProperties":false}"""), Token));
        Assert.True(output.GetProperty("asks_for_credentials").GetBoolean());
        Assert.Equal($"State:\n{{\"mensagens\":[\"Oi\"]}}\n\nQuestion:\nClassify only.\n\n{line}\n\nAllowed options:\nA. Yes\nB. No\n\nReply with exactly one option letter from: A, B.",
            server.LastChat.GetProperty("messages")[1].GetProperty("content").GetString());
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

    private const string ThreeOptionSchema = """
        {"type":"object","properties":{"canal":{"type":"string","enum":["a","b","c"]}},"required":["canal"],"additionalProperties":false}
        """;
    private const string BooleanSchema = """
        {"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}
        """;

    private static string Ternary(double pA, double pB, double pC, int prompt) =>
        FakeLlamaServer.Chat(Model, [("A", Math.Log(pA)), ("B", Math.Log(pB)), ("C", Math.Log(pC))], prompt: prompt);

    private static string[] ChatContents(FakeLlamaServer server) => server.Requests.Where(r => r.Path == "/v1/chat/completions")
        .Select(r => r.Body.GetProperty("messages")[1].GetProperty("content").GetString()!).ToArray();

    [Fact]
    public async Task Rotations_ask_every_cyclic_order_and_average_back_onto_the_original_options()
    {
        // Position-biased replies: the first label is always favored, whichever option sits there.
        var server = Server(Ternary(0.5, 0.3, 0.2, 11), Ternary(0.5, 0.4, 0.1, 12), Ternary(0.6, 0.1, 0.3, 13));
        using var http = new HttpClient(server);
        SystemOneResult? trace = null;
        var decisions = new List<(string Node, string Field, OpenJevDecision Decision)>();
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5, (_, result) => trace = result,
            observeDecision: (node, field, decision) => decisions.Add((node, field, decision)), optionOrder: OpenJevOptionOrder.AverageRotations);

        var output = Json(await adapter.CompleteAsync(Request(ThreeOptionSchema), Token));

        Assert.Equal(3, server.ChatCalls);
        var chats = ChatContents(server);
        Assert.EndsWith("Allowed options:\nA. a\nB. b\nC. c\n\nReply with exactly one option letter from: A, B, C.", chats[0]);
        Assert.EndsWith("Allowed options:\nA. b\nB. c\nC. a\n\nReply with exactly one option letter from: A, B, C.", chats[1]);
        Assert.EndsWith("Allowed options:\nA. c\nB. a\nC. b\n\nReply with exactly one option letter from: A, B, C.", chats[2]);
        // a = (0.5+0.1+0.1)/3, b = (0.3+0.5+0.3)/3, c = (0.2+0.4+0.6)/3: the fixed order alone would have chosen a.
        Assert.Equal("c", output.GetProperty("canal").GetString());
        Assert.NotNull(trace);
        Assert.Equal("OpenJevLabelSoftmaxRotationAverage", trace.ProbabilityPolicy);
        var canal = trace.Answers.GetProperty("canal");
        Assert.Equal("c", canal.GetProperty("choice").GetString());
        Assert.Equal(["a", "b", "c"], canal.GetProperty("probabilities").EnumerateObject().Select(p => p.Name));
        Assert.Equal(0.7 / 3, canal.GetProperty("probabilities").GetProperty("a").GetDouble(), 1e-12);
        Assert.Equal(1.1 / 3, canal.GetProperty("probabilities").GetProperty("b").GetDouble(), 1e-12);
        Assert.Equal(1.2 / 3, canal.GetProperty("probabilities").GetProperty("c").GetDouble(), 1e-12);
        Assert.Equal(1.2 / 3, canal.GetProperty("confidence").GetDouble(), 1e-12);
        Assert.Equal(36, trace.InputTokens);
        Assert.Equal(3, trace.OutputTokens);

        var (node, field, decision) = Assert.Single(decisions);
        Assert.Equal(("situation", "canal"), (node, field));
        Assert.Equal(Model, decision.Model);
        Assert.Equal(["A", "B", "C"], decision.Options.Select(o => o.Label));
        Assert.Equal(["a", "b", "c"], decision.Options.Select(o => o.Text));
        double[] means = [0.7 / 3, 1.1 / 3, 1.2 / 3];
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(means[i], decision.Options[i].Probability, 1e-12);
            Assert.Equal(Math.Log(means[i]), decision.Options[i].Logprob, 1e-12);
        }
        Assert.Equal(36, decision.InputTokens);
        Assert.Equal(3, decision.OutputTokens);
        Assert.Equal(trace.Elapsed, decision.Elapsed);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.65, false)]
    public async Task Rotations_average_boolean_yes_before_the_threshold(double threshold, bool expected)
    {
        // Yes first: P(Yes) = 0.9. No first: P(No) = 0.7, so P(Yes) = 0.3. Mean 0.6.
        var server = Server(Binary(0.9, prompt: 20), Binary(0.7, prompt: 21));
        using var http = new HttpClient(server);
        SystemOneResult? trace = null;
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), threshold, (_, result) => trace = result,
            optionOrder: OpenJevOptionOrder.AverageRotations);

        var output = Json(await adapter.CompleteAsync(Request(BooleanSchema), Token));

        Assert.Equal(expected, output.GetProperty("ok").GetBoolean());
        var chats = ChatContents(server);
        Assert.Equal(2, chats.Length);
        Assert.EndsWith("Allowed options:\nA. Yes\nB. No\n\nReply with exactly one option letter from: A, B.", chats[0]);
        Assert.EndsWith("Allowed options:\nA. No\nB. Yes\n\nReply with exactly one option letter from: A, B.", chats[1]);
        Assert.Equal(0.6, trace!.Answers.GetProperty("ok").GetProperty("noul").GetDouble(), 1e-12);
        Assert.Equal(41, trace.InputTokens);
        Assert.Equal(2, trace.OutputTokens);
    }

    [Fact]
    public async Task Rotations_sum_usage_across_every_field_and_call()
    {
        var server = Server(Binary(0.2, prompt: 3), Binary(0.4, prompt: 5), Binary(0.6, prompt: 7), Binary(0.8, prompt: 11));
        using var http = new HttpClient(server);
        SystemOneResult? trace = null;
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5, (_, result) => trace = result,
            optionOrder: OpenJevOptionOrder.AverageRotations);

        await adapter.CompleteAsync(Request(), Token);

        Assert.Equal(4, server.ChatCalls);
        Assert.Equal(26, trace!.InputTokens);
        Assert.Equal(4, trace.OutputTokens);
        // acao: responder = (0.2 + 0.6) / 2. cuidado: P(Yes) = (0.6 + (1 - 0.8)) / 2.
        Assert.Equal(0.4, trace.Answers.GetProperty("acao").GetProperty("probabilities").GetProperty("responder").GetDouble(), 1e-12);
        Assert.Equal(0.4, trace.Answers.GetProperty("cuidado").GetProperty("noul").GetDouble(), 1e-12);
    }

    [Fact]
    public async Task Fixed_order_makes_one_call_per_field_under_the_single_call_policy()
    {
        var server = Server(Binary(0.2), Binary(0.6));
        using var http = new HttpClient(server);
        SystemOneResult? trace = null;
        var adapter = new OpenJevFlowCompletion(new OpenJevClient(http, new("http://localhost:8080/")), 0.5, (_, result) => trace = result,
            optionOrder: OpenJevOptionOrder.Fixed);
        await adapter.CompleteAsync(Request(), Token);
        Assert.Equal(2, server.ChatCalls);
        Assert.Equal("OpenJevLabelSoftmax", trace!.ProbabilityPolicy);
    }
}
