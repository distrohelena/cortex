using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Cortex.Core.Flows;
using Cortex.Inference.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class TypedFlowIntegrationTests
{
    private static async Task<JsonNode> Project() => JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice1.json"), TestContext.Current.CancellationToken))!;
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);

    [Fact]
    public async Task ModelAdapterSendsRealStrictSchemaAndNamedJsonToFakeHttp()
    {
        var graph = (await Project())["project"]!["states"]![0]!["graph"]!;
        var flow = TypedFlowCompiler.Compile(Json(graph), "in");
        var handler = new FakeHttp(); using var http = new HttpClient(handler);
        var client = new FlowModelCompletion(new LmStudioClient(http, new()), "model-fixture");
        var schema = TypedFlowRunner.ResponseSchema(flow.Nodes["ai"]);
        await client.CompleteAsync(new("ai", "Responsabilidade", JsonSerializer.SerializeToElement(new { mensagem = "oi" }), schema, null, "lmstudio", "Corrija enum"), TestContext.Current.CancellationToken);
        var payload = handler.Body;
        Assert.Equal("model-fixture", payload.GetProperty("model").GetString());
        Assert.Equal("json_schema", payload.GetProperty("response_format").GetProperty("type").GetString());
        var jsonSchema = payload.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.True(JsonElement.DeepEquals(schema!.Value, jsonSchema.GetProperty("schema")));
        Assert.Contains("Corrija enum", payload.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Contains(schema.Value.GetRawText(), payload.GetProperty("messages")[0].GetProperty("content").GetString());
        using var inputs = JsonDocument.Parse(payload.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal("oi", inputs.RootElement.GetProperty("mensagem").GetString());
    }

    [Fact]
    public async Task HttpFakeExecutionAndDraftRoundTripNeverCallExternalServices()
    {
        var project = await Project(); var graph = project["project"]!["states"]![0]!["graph"]!;
        var directory = Directory.CreateTempSubdirectory("filomena-typed-test-");
        var handler = new FakeHttp(); using var http = new HttpClient(handler);
        try
        {
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                var request = new { graph = Json(graph), entryNodeId = "in", message = new { texto = "oi", remetente = new { nome = "Helena", stableId = "private-id" }, canal = new { nome = "DM", nativeId = "private-conversation" }, reply = (object?)null }, responses = new Dictionary<string, string[]> { ["ai"] = ["""{"acao":"responder","proximo_estado":"engajada","motivo":"foi chamada"}"""], ["done"] = ["""{"texto":"foi chamada"}"""] } };
                Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/flow/typed/run", request, TestContext.Current.CancellationToken)).StatusCode);
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                var validated = await (await browser.PostAsJsonAsync("/api/flow/typed/validate", request, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                Assert.True(validated.GetProperty("valid").GetBoolean());
                var result = await (await browser.PostAsJsonAsync("/api/flow/typed/run", request, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                Assert.True(result.GetProperty("success").GetBoolean());
                Assert.Equal("responder", result.GetProperty("outputs").GetProperty("ai").GetProperty("o1").GetProperty("value").GetString());
                Assert.DoesNotContain("private-id", result.GetRawText()); Assert.Equal(0, handler.Count);
                graph["nodes"]![2]!["inputs"]![0]!["name"] = "";
                graph["edges"]![1]!["targetPort"] = "removed-port";
                (await browser.PostAsJsonAsync("/api/flow/project", project, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            }
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(Json(project), loaded));
                foreach (var path in new[] { "/flow-contract.mjs", "/flow-typed-panel.mjs" })
                    (await browser.GetAsync(path, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            }
        }
        finally { directory.Delete(true); }
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }
        public int Count { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++; using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); Body = document.RootElement.Clone();
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"message":{"content":"{}"}}]}""") };
        }
    }
}
