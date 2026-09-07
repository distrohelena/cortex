using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class SessionFlowIntegrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Prefix = "/api/flow/typed/sessions";

    private static JsonNode Project()
    {
        // HTTP composition contract deliberately small; the complete acceptance fixture is tested separately.
        var graph = JsonNode.Parse("""
            {"version":3,"nodes":[
              {"id":"in","kind":"group","x":0,"y":0},
              {"id":"acc","kind":"accumulate","x":200,"y":0,"settings":{"quietSeconds":null,"maxMessages":2,"maxWaitSeconds":null}},
              {"id":"history","kind":"history","x":400,"y":0,"settings":{"maxMessages":20,"maxAgeMinutes":15,"maxCharacters":6000,"includeCharacterMessages":true,"mode":"before-current-batch"}},
              {"id":"ai","kind":"prompt","x":600,"y":0,"prompt":"Fixture only","inputs":[{"id":"batch","name":"mensagens","type":"message[]","required":true},{"id":"old","name":"historico","type":"message[]","required":true}],"responseFields":[{"id":"state","name":"estado","type":"enum","values":["engajada"]}]},
              {"id":"exit","kind":"exit","x":800,"y":0,"settings":{"mode":"switch","stateMapping":{"engajada":"engajada"}}}
            ],"edges":[
              {"source":"in","sourcePort":"message","target":"acc","targetPort":"message"},
              {"source":"acc","sourcePort":"messages","target":"ai","targetPort":"batch"},
              {"source":"history","sourcePort":"messages","target":"ai","targetPort":"old"},
              {"source":"ai","sourcePort":"state","target":"exit","targetPort":"state"}
            ],"controlEdges":[
              {"source":"in","sourcePort":"then","target":"acc","targetPort":"exec"},
              {"source":"acc","sourcePort":"then","target":"history","targetPort":"exec"},
              {"source":"history","sourcePort":"then","target":"ai","targetPort":"exec"},
              {"source":"ai","sourcePort":"then","target":"exit","targetPort":"exec"}
            ]}
            """)!;
        return new JsonObject
        {
            ["format"] = "filomena-local-project",
            ["project"] = new JsonObject
            {
                ["version"] = 1,
                ["stateScope"] = "session",
                ["initialStateId"] = "inativa",
                ["states"] = new JsonArray(new[] { "inativa", "engajada" }.Select(id => (JsonNode)new JsonObject
                {
                    ["id"] = id,
                    ["name"] = id,
                    ["graph"] = graph.DeepClone()
                }).ToArray())
            }
        };
    }

    private static object Session(string nativeId) => new { channel = "whatsapp", accountId = "fixture", nativeId, kind = "group" };
    private static object Message(string id, string nativeId) => new
    {
        messageId = id,
        timestamp = DateTimeOffset.Parse("2026-09-07T12:00:00Z"),
        direction = "inbound",
        value = new { texto = "fixture private text", remetente = new { nome = "Helena", stableId = "whatsapp:fixture" }, canal = new { nome = "Fixture", nativeId }, reply = (object?)null }
    };

    [Fact]
    public async Task FakeHttpSessionBatchesTransitionsAndKeepsDesignSeparateFromRuntime()
    {
        var directory = Directory.CreateTempSubdirectory("filomena-session-http-");
        using var external = new HttpClient(new NeverExternal());
        try
        {
            await using var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(Token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var start = new { project = Project(), responses = new Dictionary<string, string[]> { ["ai"] = ["{\"estado\":\"engajada\"}"] } };
            Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync(Prefix, start, Token)).StatusCode);
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var created = await browser.PostAsJsonAsync(Prefix, start, Token);
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("runtimeId").GetString();
            const string drift = "5511981042361-1589823314@g.us";
            foreach (var messageId in new[] { "m1", "m1", "m2" })
                (await browser.PostAsJsonAsync($"{Prefix}/{id}/receive", new { session = Session(drift), message = Message(messageId, drift) }, Token)).EnsureSuccessStatusCode();
            var drained = await browser.PostAsJsonAsync($"{Prefix}/{id}/drain", new { session = Session(drift) }, Token);
            drained.EnsureSuccessStatusCode();
            var status = await browser.GetFromJsonAsync<JsonElement>($"{Prefix}/{id}", Token);
            Assert.Equal("engajada", status.GetProperty("sessions")[0].GetProperty("currentStateId").GetString());
            Assert.Equal(1, status.GetProperty("executions").GetArrayLength());
            var transition = Assert.Single(status.GetProperty("trace").EnumerateArray(),
                item => item.GetProperty("eventName").GetString() == "transition-applied");
            Assert.Equal("inativa", transition.GetProperty("batchStateId").GetString());
            Assert.Equal(0, transition.GetProperty("batchStateVersion").GetInt64());
            Assert.Equal("engajada", transition.GetProperty("currentSessionStateId").GetString());
            Assert.Equal(1, transition.GetProperty("currentSessionStateVersion").GetInt64());
            Assert.DoesNotContain("fixture private text", status.GetRawText());
            Assert.DoesNotContain("Fixture only", status.GetRawText());
            (await browser.PostAsJsonAsync("/api/flow/project", Project(), Token)).EnsureSuccessStatusCode();
            var saved = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", Token);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Project()), saved));
            Assert.DoesNotContain("currentStateId", saved.GetRawText());
            (await browser.PostAsJsonAsync($"{Prefix}/{id}/stop", new { }, Token)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Prefix}/{id}", Token)).StatusCode);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task InvalidDefaultCanBeSavedAsDraftButCannotStartRuntime()
    {
        var directory = Directory.CreateTempSubdirectory("filomena-session-draft-");
        using var external = new HttpClient(new NeverExternal());
        try
        {
            await using var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(Token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var project = Project(); project["project"]!["initialStateId"] = "missing";
            (await browser.PostAsJsonAsync("/api/flow/project", project, Token)).EnsureSuccessStatusCode();
            var validation = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project }, Token);
            validation.EnsureSuccessStatusCode();
            Assert.False((await validation.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("valid").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync(Prefix, new { project }, Token)).StatusCode);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task MismatchedMessageIsRejectedAndConfirmedOutboundOnlyEntersHistory()
    {
        var directory = Directory.CreateTempSubdirectory("filomena-session-identity-");
        using var external = new HttpClient(new NeverExternal());
        try
        {
            await using var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(Token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var start = await browser.PostAsJsonAsync(Prefix, new { project = Project() }, Token);
            start.EnsureSuccessStatusCode();
            var id = (await start.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("runtimeId").GetString();
            const string conversation = "fixture:conversation-a";
            var invalid = await browser.PostAsJsonAsync($"{Prefix}/{id}/receive", new
            {
                session = Session(conversation),
                message = Message("mismatch", "fixture:conversation-b")
            }, Token);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var outbound = JsonSerializer.SerializeToNode(Message("confirmed-outbound", conversation))!;
            outbound["direction"] = "outbound";
            (await browser.PostAsJsonAsync($"{Prefix}/{id}/receive", new { session = Session(conversation), message = outbound }, Token)).EnsureSuccessStatusCode();
            var status = await browser.GetFromJsonAsync<JsonElement>($"{Prefix}/{id}", Token);
            Assert.Equal(1, status.GetProperty("sessions")[0].GetProperty("historyCount").GetInt32());
            Assert.Empty(status.GetProperty("executions").EnumerateArray());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task RealSliceFixtureRoundTripsAndRestartResetsOnlyOperationalState()
    {
        var project = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice2.json"), Token))!;
        var directory = Directory.CreateTempSubdirectory("filomena-session-fixture-");
        using var external = new HttpClient(new NeverExternal());
        const string plex = "120363318973251043@g.us";
        try
        {
            await using (var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(Token);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                (await browser.PostAsJsonAsync("/api/flow/project", project, Token)).EnsureSuccessStatusCode();
                var start = await browser.PostAsJsonAsync(Prefix, new { project, responses = new Dictionary<string, string[]> { ["ai"] = ["""{"acao":"responder","proximo_estado":"engajada","motivo":"fixture"}"""] } }, Token);
                start.EnsureSuccessStatusCode();
                var id = (await start.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("runtimeId").GetString();
                foreach (var messageId in new[] { "a", "b", "c" })
                    (await browser.PostAsJsonAsync($"{Prefix}/{id}/receive", new { session = Session(plex), message = Message(messageId, plex) }, Token)).EnsureSuccessStatusCode();
                (await browser.PostAsJsonAsync($"{Prefix}/{id}/drain", new { session = Session(plex) }, Token)).EnsureSuccessStatusCode();
                var status = await browser.GetFromJsonAsync<JsonElement>($"{Prefix}/{id}", Token);
                Assert.Equal("engajada", status.GetProperty("sessions")[0].GetProperty("currentStateId").GetString());
                Assert.True(status.GetProperty("executions")[0].GetProperty("success").GetBoolean());
                Assert.True(status.GetProperty("executions")[0].GetProperty("transitionApplied").GetBoolean());
            }
            await using (var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(Token);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", Token);
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(project), loaded));
                var start = await browser.PostAsJsonAsync(Prefix, new { project = loaded }, Token);
                start.EnsureSuccessStatusCode();
                var id = (await start.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("runtimeId").GetString();
                (await browser.PostAsJsonAsync($"{Prefix}/{id}/receive", new { session = Session(plex), message = Message("d", plex) }, Token)).EnsureSuccessStatusCode();
                var status = await browser.GetFromJsonAsync<JsonElement>($"{Prefix}/{id}", Token);
                Assert.Equal("inativa", status.GetProperty("sessions")[0].GetProperty("currentStateId").GetString());
                Assert.Empty(status.GetProperty("executions").EnumerateArray());
            }
        }
        finally { directory.Delete(true); }
    }

    private sealed class NeverExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Session playground must never call external services.");
    }
}
