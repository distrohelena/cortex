using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Filomena.App.Playground;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class LocalFlowTests
{
    [Fact]
    public async Task SelectedEventEntryRunsOnlyItsPathAndSharedPromptsDoNotShareMessages()
    {
        var graph = JsonNode.Parse("""
          {"version":1,"nodes":[
           {"id":"dm","kind":"dm","x":0,"y":0},
           {"id":"group","kind":"group","x":0,"y":200},
           {"id":"groupOnly","kind":"prompt","prompt":"Group only","x":200,"y":200},
           {"id":"shared","kind":"prompt","prompt":"Shared","x":400,"y":0},
           {"id":"out","kind":"output","x":600,"y":0}],
           "edges":[{"source":"dm","target":"shared"},{"source":"group","target":"groupOnly"},
                    {"source":"groupOnly","target":"shared"},{"source":"shared","target":"out"}]}
          """);
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        foreach (var (entry, message, expected) in new[] {
            ("dm", "Private", new[] { "dm", "shared", "out" }),
            ("group", "Public", new[] { "group", "groupOnly", "shared", "out" }),
            ("dm", "Fresh", new[] { "dm", "shared", "out" }) })
        {
            using var response = await browser.PostAsJsonAsync("/api/flow/run", new { graph, message, entryNodeId = entry }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            Assert.Equal(expected, body["steps"]!.AsArray().Select(s => s!["nodeId"]!.GetValue<string>()));
        }
        Assert.Equal(new[] { "Shared|Private", "Group only|Public", "Shared|Answer 2", "Shared|Fresh" }, model.Inputs);
        foreach (var entry in new string?[] { null, "shared", "missing" })
        {
            using var response = await browser.PostAsJsonAsync("/api/flow/run", new { graph, message = "No", entryNodeId = entry }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(4, model.Inputs.Count);
    }

    [Fact]
    public async Task EachPromptUsesItsOwnModelWithoutChangingTheDefault()
    {
        var graph = Draft();
        graph["nodes"]![3]!["provider"] = "lmstudio";
        graph["nodes"]![3]!["model"] = "model-one";
        graph["nodes"]![1]!["model"] = "model-two";
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        using var result = await browser.PostAsJsonAsync("/api/flow/run", new { graph, message = "Hello" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var plain = await browser.PostAsJsonAsync("/api/test", new { prompt = "p", message = "m" }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "model-one", "model-two", "google/gemma-4-e4b" }, model.Models);
    }

    [Fact]
    public async Task UnknownProviderIsValidDesignButExecutionRejectsItBeforeAnyInference()
    {
        var graph = Draft();graph["nodes"]![1]!["provider"] = "future-provider";
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        using var validation = await browser.PostAsJsonAsync("/api/flow/validate", graph, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        using var execution = await browser.PostAsJsonAsync("/api/flow/run", new { graph, message = "Hello" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, execution.StatusCode);
        Assert.Contains("p1", await execution.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(model.Inputs);
    }

    [Fact]
    public async Task CancellationStopsAnInflightModelAndDoesNotStartTheNextPrompt()
    {
        using var model = new WaitingProvider();
        using var http = new HttpClient(model);
        var client = new LmStudioClient(http, new());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var graph = Draft().Deserialize<LocalFlowDraft>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var run = LocalFlowRunner.RunAsync(graph, "Start", client, cancellation.Token);
        await model.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task EditorAndItsModulesAreServedWithoutCallingTheModel()
    {
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        foreach (var path in new[] { "/flow", "/flow.css", "/flow.js", "/flow-model.mjs", "/flow-layout.mjs", "/flow-camera.mjs", "/flow-catalog.mjs" })
        {
            using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        Assert.Empty(model.Inputs);
    }

    private static JsonObject Draft() => JsonNode.Parse("""
        {"version":1,"nodes":[
          {"id":"out","kind":"output","x":900,"y":100},
          {"id":"p1","kind":"prompt","prompt":"Second instruction","x":600,"y":100},
          {"id":"in","kind":"input","x":0,"y":100},
          {"id":"p2","kind":"prompt","prompt":"First instruction","x":300,"y":100}],
         "edges":[{"source":"in","target":"p2"},{"source":"p2","target":"p1"},{"source":"p1","target":"out"}]}
        """)!.AsObject();

    [Fact]
    public async Task WiresDetermineExecutionAndEachPromptReceivesOnlyPreviousOutput()
    {
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        using var response = await browser.PostAsJsonAsync("/api/flow/run", new { graph = Draft(), message = "Start" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal("Answer 2", result["output"]!.GetValue<string>());
        Assert.Equal(new[] { "in", "p2", "p1", "out" }, result["steps"]!.AsArray().Select(s => s!["nodeId"]!.GetValue<string>()));
        Assert.Equal("Answer 1", result["steps"]![2]!["input"]!.GetValue<string>());
        Assert.Equal(new[] { "First instruction|Start", "Second instruction|Answer 1" }, model.Inputs);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("disconnected")]
    [InlineData("fork")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("unknown")]
    [InlineData("null-node")]
    [InlineData("null-edge")]
    [InlineData("oversize")]
    public async Task InvalidGraphNeverReachesModel(string defect)
    {
        var graph = Draft();
        var nodes = graph["nodes"]!.AsArray();
        var edges = graph["edges"]!.AsArray();
        switch (defect)
        {
            case "cycle": edges[2]!["target"] = "p2"; break;
            case "disconnected": edges.RemoveAt(1); break;
            case "fork": edges.Add(JsonNode.Parse("""{"source":"in","target":"p1"}""")); break;
            case "duplicate": nodes[0]!["id"] = "p1"; break;
            case "missing": edges[0]!["source"] = "absent"; break;
            case "blank": nodes[1]!["prompt"] = " "; break;
            case "unknown": nodes[1]!["kind"] = "send"; break;
            case "null-node": nodes[1] = null; break;
            case "null-edge": edges[1] = null; break;
            case "oversize": nodes[1]!["prompt"] = new string('x', 16001); break;
        }
        using var model = new Provider();
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        using var response = await browser.PostAsJsonAsync("/api/flow/run", new { graph, message = "Start" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(model.Inputs);
    }

    [Fact]
    public async Task FailureKeepsPriorTraceAndDoesNotExecuteLaterNodes()
    {
        using var model = new Provider { FailAt = 2 };
        using var http = new HttpClient(model);
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = Browser(app.Urls.Single());
        using var response = await browser.PostAsJsonAsync("/api/flow/run", new { graph = Draft(), message = "Start" }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var result = JsonNode.Parse(body)!;
        Assert.False(result["success"]!.GetValue<bool>());
        Assert.Equal(3, result["steps"]!.AsArray().Count);
        Assert.Equal("Answer 1", result["steps"]![1]!["output"]!.GetValue<string>());
        Assert.Equal("p1", result["steps"]![2]!["nodeId"]!.GetValue<string>());
        Assert.NotNull(result["steps"]![2]!["error"]);
        Assert.DoesNotContain("secret-provider-body", body);
        Assert.Equal(2, model.Inputs.Count);
    }

    private static HttpClient Browser(string address)
    {
        var browser = new HttpClient { BaseAddress = new Uri(address) };
        browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
        return browser;
    }

    private sealed class Provider : HttpMessageHandler
    {
        public List<string> Inputs { get; } = [];
        public List<string> Models { get; } = [];
        public int FailAt { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var messages = json.RootElement.GetProperty("messages");
            Models.Add(json.RootElement.GetProperty("model").GetString()!);
            Inputs.Add(messages[0].GetProperty("content").GetString() + "|" + messages[1].GetProperty("content").GetString());
            return new HttpResponseMessage(Inputs.Count == FailAt ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(Inputs.Count == FailAt ? "secret-provider-body" : JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = $"Answer {Inputs.Count}" } } }
                }))
            };
        }
    }

    private sealed class WaitingProvider : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation");
        }
    }
}
