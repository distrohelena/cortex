using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Inference;
using Cortex.Inference.OpenJev;
using Xunit;
using static Cortex.TestSupport.SystemOneTestSupport;

namespace Cortex.Inference.Tests;

/// <summary>Fake llama-server: answers /tokenize from a label table and /v1/chat/completions from a queue.</summary>
internal sealed class FakeLlamaServer : HttpMessageHandler
{
    public Dictionary<string, int[]> Tokens { get; } = Enumerable.Range(0, 20)
        .ToDictionary(i => ((char)('A' + i)).ToString(), i => new[] { 1000 + i });
    public Queue<(HttpStatusCode Status, string Body)> Chats { get; } = new();
    public List<(string Path, JsonElement Body)> Requests { get; } = [];
    public int ChatCalls => Requests.Count(r => r.Path == "/v1/chat/completions");
    public int TokenizeCalls => Requests.Count(r => r.Path == "/tokenize");
    public JsonElement LastChat => Requests.Last(r => r.Path == "/v1/chat/completions").Body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        var body = Json(await request.Content!.ReadAsStringAsync(token));
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((path, body));
        if (path == "/tokenize")
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { tokens = Tokens[body.GetProperty("content").GetString()!] }));
        if (path == "/v1/chat/completions") { var (status, text) = Chats.Dequeue(); return Reply(status, text); }
        return Reply(HttpStatusCode.NotFound, "{}");
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A llama-server style chat completion whose first token carries the given label logprobs.</summary>
    public static string Chat(string model, IReadOnlyList<(string Label, double Logprob)> labels, int prompt = 50, int completion = 1,
        Func<string, string>? token = null)
    {
        var top = labels.Select(l => new { id = 1000 + l.Label[0] - 'A', token = token?.Invoke(l.Label) ?? l.Label,
            bytes = new[] { (int)l.Label[0] }, logprob = l.Logprob }).ToArray();
        var first = top[0];
        return JsonSerializer.Serialize(new
        {
            model, @object = "chat.completion",
            choices = new[] { new { index = 0, finish_reason = "length", message = new { role = "assistant", content = first.token },
                logprobs = new { content = new[] { new { first.id, first.token, first.bytes, first.logprob, top_logprobs = top } } } } },
            usage = new { prompt_tokens = prompt, completion_tokens = completion, total_tokens = prompt + completion }
        });
    }
}

public sealed class OpenJevClientTests
{
    private const string Model = "qwen3-0.6b";
    private static readonly Uri Base = new("http://127.0.0.1:8080/");
    private static readonly (string, double)[] Three = [("A", -0.1), ("B", -2.3), ("C", -4.0)];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (FakeLlamaServer Server, OpenJevClient Client, HttpClient Http) Create(params string[] chats)
    {
        var server = new FakeLlamaServer();
        foreach (var chat in chats) server.Chats.Enqueue((HttpStatusCode.OK, chat));
        var http = new HttpClient(server);
        return (server, new OpenJevClient(http, Base), http);
    }

    [Fact]
    public async Task Sends_openjev_prompt_grammar_bias_and_sampling_exactly()
    {
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, Three));
        using var __ = http;
        server.Tokens["B"] = [7777];
        await client.DecideAsync(Model, "{\"x\":1}", "Pick one.", ["north", "south", "stay"], null, Token);
        foreach (var (path, body) in server.Requests.Where(r => r.Path == "/tokenize"))
        {
            Assert.Equal(3, body.EnumerateObject().Count());
            Assert.Equal(Model, body.GetProperty("model").GetString());
            Assert.False(body.GetProperty("add_special").GetBoolean());
        }
        var chat = server.LastChat;
        var expected = Json("""
            {"model":"qwen3-0.6b","messages":[
              {"role":"system","content":"Make the requested decision from the supplied state. Follow the output format exactly."},
              {"role":"user","content":"State:\n{\"x\":1}\n\nQuestion:\nPick one.\n\nAllowed options:\nA. north\nB. south\nC. stay\n\nReply with exactly one option letter from: A, B, C."}],
             "max_tokens":1,"temperature":1,"top_k":0,"top_p":1,"logprobs":true,"top_logprobs":20,
             "logit_bias":{"1000":100,"7777":100,"1002":100},
             "grammar":"root ::= \"A\" | \"B\" | \"C\"",
             "cache_prompt":false,"chat_template_kwargs":{"enable_thinking":false}}
            """);
        Assert.True(JsonElement.DeepEquals(expected, chat), chat.GetRawText());
    }

    [Fact]
    public async Task Without_label_bias_omits_logit_bias_and_tokenize()
    {
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, Three));
        using var __ = http;
        await client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token, applyLabelBias: false);
        Assert.Equal(0, server.TokenizeCalls);
        Assert.False(server.LastChat.TryGetProperty("logit_bias", out _));
    }

    [Fact]
    public async Task Softmax_over_label_logprobs_matches_reference()
    {
        var (_, client, http) = Create(FakeLlamaServer.Chat(Model, Three, prompt: 61, completion: 1));
        using var __ = http;
        var decision = await client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token);
        double[] logits = [-0.1, -2.3, -4.0];
        var exps = logits.Select(v => Math.Exp(v - logits.Max())).ToArray();
        var reference = exps.Select(e => e / exps.Sum()).ToArray();
        Assert.Equal(["A", "B", "C"], decision.Options.Select(o => o.Label));
        Assert.Equal(["a", "b", "c"], decision.Options.Select(o => o.Text));
        Assert.Equal(logits, decision.Options.Select(o => o.Logprob));
        for (var i = 0; i < 3; i++) Assert.Equal(reference[i], decision.Options[i].Probability, 1e-12);
        Assert.Equal(1, decision.Options.Sum(o => o.Probability), 1e-12);
        Assert.Equal(Model, decision.Model);
        Assert.Equal(61, decision.InputTokens);
        Assert.Equal(1, decision.OutputTokens);
    }

    [Fact]
    public async Task Label_matched_by_single_byte_when_token_text_differs()
    {
        var (_, client, http) = Create(FakeLlamaServer.Chat(Model, [("A", -0.5), ("B", -1.0)], token: label => " " + label));
        using var __ = http;
        var decision = await client.DecideAsync(Model, "s", "q", ["yes", "no"], null, Token);
        Assert.Equal(-0.5, decision.Options[0].Logprob);
        Assert.Equal(-1.0, decision.Options[1].Logprob);
    }

    [Fact]
    public async Task Missing_label_logprob_is_rejected()
    {
        var (_, client, http) = Create(FakeLlamaServer.Chat(Model, [("A", -0.1), ("B", -2.3)]));
        using var __ = http;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token));
        Assert.Equal("read_only_model_response_rejected", error.Data["failureCategory"]);
    }

    [Theory]
    [InlineData("-1e999")]
    [InlineData("null")]
    [InlineData("\"-0.5\"")]
    public async Task Non_finite_logprob_is_rejected(string value)
    {
        var chat = FakeLlamaServer.Chat(Model, [("A", -0.1), ("B", -0.25)]).Replace("-0.25", value, StringComparison.Ordinal);
        var (_, client, http) = Create(chat);
        using var __ = http;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b"], null, Token));
        Assert.Equal("read_only_model_response_rejected", error.Data["failureCategory"]);
    }

    [Fact]
    public async Task Model_mismatch_is_rejected()
    {
        var (_, client, http) = Create(FakeLlamaServer.Chat("another-model.gguf", Three));
        using var __ = http;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token));
        Assert.Equal("read_only_model_response_rejected", error.Data["failureCategory"]);
    }

    [Theory]
    [InlineData("[1001,1002]")]
    [InlineData("[]")]
    [InlineData("[1000]")]
    public async Task Multi_token_label_is_rejected_before_chat(string tokens)
    {
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, Three));
        using var __ = http;
        server.Tokens["B"] = JsonSerializer.Deserialize<int[]>(tokens)!;
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token));
        Assert.Equal(0, server.ChatCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_400_reports_provider_failure_without_retry(bool capture)
    {
        const string body = "{\"error\":{\"code\":400,\"message\":\"the request exceeds the available context size\"}}";
        var server = new FakeLlamaServer();
        server.Chats.Enqueue((HttpStatusCode.BadRequest, body));
        using var http = new HttpClient(server);
        var client = new OpenJevClient(http, Base);
        HttpRequestException? failure = null;
        var completion = new Probe(async request =>
        {
            failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.DecideAsync(Model, "s", "q", ["a", "b"], request.Diagnostics, Token));
            throw failure;
        });
        var request = new FlowCompletionRequest("decide", "", Json("{}"), null, Model, "openjev", null);
        var events = new List<InferenceAttemptObservation>();
        var authority = new HostInferenceAuthority(observe: events.Add);
        var profile = new HostInferenceProfile("p", "1", completion, request, _ => { })
            { DiagnosticOptions = new() { CaptureRawResponses = capture, RedactResponse = value => value } };
        var scope = new InferenceScope("test");
        var bound = authority.Bind(authority.Grant(scope, "test", [profile]), scope, "p", new("run", "decide", "1"));
        await Assert.ThrowsAnyAsync<Exception>(() => bound.CompleteAsync(request, Token));
        Assert.NotNull(failure);
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.Equal("read_only_model_response_rejected", failure.Data["failureCategory"]);
        Assert.Equal(1, server.ChatCalls);
        var observation = Assert.Single(events);
        if (capture) Assert.Equal(body, observation.CapturedResponse);
        else { Assert.Null(observation.CapturedResponse); Assert.DoesNotContain("context size", failure.Message); }
    }

    [Theory]
    [InlineData("http://192.168.1.10:8080/")]
    [InlineData("https://example.com/")]
    [InlineData("http://user:pw@127.0.0.1:8080/")]
    [InlineData("http://127.0.0.1:8080/?x=1")]
    [InlineData("http://127.0.0.1:8080/#f")]
    [InlineData("ftp://127.0.0.1/")]
    [InlineData("http://127.0.0.1:8080/base")]
    public async Task Non_loopback_endpoint_is_rejected(string endpoint)
    {
        var server = new FakeLlamaServer();
        using var http = new HttpClient(server);
        var client = new OpenJevClient(http, new Uri(endpoint));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b"], null, Token));
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(1)]
    public async Task More_than_twenty_options_is_rejected(int count)
    {
        var (server, client, http) = Create();
        using var __ = http;
        var options = Enumerable.Range(0, count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", options, null, Token));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Two_option_question_tokenizes_exactly_two_labels()
    {
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, [("A", -0.1), ("B", -2.3)]));
        using var __ = http;
        await client.DecideAsync(Model, "s", "q", ["yes", "no"], null, Token);
        Assert.Equal(["A", "B"], TokenizedLabels(server));
        Assert.Equal(1, server.ChatCalls);
    }

    [Fact]
    public async Task Tokenize_is_cached_per_model_and_extended_only_for_new_labels()
    {
        (string, double)[] five = [("A", -0.1), ("B", -2.3), ("C", -4.0), ("D", -5.0), ("E", -6.0)];
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, Three), FakeLlamaServer.Chat(Model, Three),
            FakeLlamaServer.Chat(Model, five), FakeLlamaServer.Chat("other", Three));
        using var __ = http;
        await client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token);
        await client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token);
        Assert.Equal(["A", "B", "C"], TokenizedLabels(server));
        await client.DecideAsync(Model, "s", "q", ["a", "b", "c", "d", "e"], null, Token);
        Assert.Equal(["A", "B", "C", "D", "E"], TokenizedLabels(server));
        Assert.Equal("{\"1000\":100,\"1001\":100,\"1002\":100,\"1003\":100,\"1004\":100}",
            server.LastChat.GetProperty("logit_bias").GetRawText());
        await client.DecideAsync("other", "s", "q", ["a", "b", "c"], null, Token);
        Assert.Equal(8, server.TokenizeCalls);
        Assert.Equal(4, server.ChatCalls);
    }

    [Fact]
    public async Task Extended_label_sharing_a_cached_token_is_rejected_before_chat()
    {
        var (server, client, http) = Create(FakeLlamaServer.Chat(Model, [("A", -0.1), ("B", -2.3)]), FakeLlamaServer.Chat(Model, Three));
        using var __ = http;
        await client.DecideAsync(Model, "s", "q", ["a", "b"], null, Token);
        server.Tokens["C"] = [1000];
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync(Model, "s", "q", ["a", "b", "c"], null, Token));
        Assert.Equal(1, server.ChatCalls);
    }

    private static string[] TokenizedLabels(FakeLlamaServer server) =>
        server.Requests.Where(r => r.Path == "/tokenize").Select(r => r.Body.GetProperty("content").GetString()!).ToArray();

    private sealed class Probe(Func<FlowCompletionRequest, Task<string>> complete) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => complete(request);
    }
}
