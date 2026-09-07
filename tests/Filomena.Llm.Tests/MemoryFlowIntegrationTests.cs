using System.Net.Http.Json;
using System.Text.Json;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class MemoryFlowIntegrationTests
{
    [Fact]
    public async Task FixtureRoundtripsAndExecutesWithFakeMemoryOnly()
    {
        var token = TestContext.Current.CancellationToken;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "typed-flow-slice3.json"), token));
        var directory = Directory.CreateTempSubdirectory("filomena-memory-http-");
        using var external = new HttpClient(new NeverExternal());
        try
        {
            await using var app = LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var project = document.RootElement;
            (await browser.PostAsJsonAsync("/api/flow/project", project, token)).EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(project, loaded));
            const string ns = "whatsapp-dm-5511944558218-s-whatsapp-net";
            const string native = "5511944558218@s.whatsapp.net";
            const string actor = "whatsapp:5511944558218@s.whatsapp.net";
            var start = await browser.PostAsJsonAsync("/api/flow/typed/sessions", new
            {
                project,
                responses = new Dictionary<string, string[]> { ["ai"] = ["{\"ok\":true}"] },
                memories = new[] {
                    new { id = "b", conversationNamespace = ns, content = "não brincar com X", category = "limite", actors = new[] { actor } },
                    new { id = "f", conversationNamespace = ns, content = "gosta de Persona 4 Golden", category = "fato", actors = new[] { actor } },
                    new { id = "other", conversationNamespace = "whatsapp-group-120363318973251043-g-us", content = "PLEX73", category = "fato", actors = new[] { actor } }
                }
            }, token);
            start.EnsureSuccessStatusCode();
            var id = (await start.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("runtimeId").GetString();
            var session = new { channel = "whatsapp", accountId = "test", nativeId = native, kind = "dm" };
            foreach (var messageId in new[] { "m1", "m2" })
                (await browser.PostAsJsonAsync($"/api/flow/typed/sessions/{id}/receive", new
                {
                    session,
                    message = new
                    {
                        messageId,
                        timestamp = DateTimeOffset.UtcNow,
                        direction = "inbound",
                        value = new { texto = "fake private text", remetente = new { nome = "Helena", stableId = actor }, canal = new { nome = "DM", nativeId = native }, reply = (object?)null }
                    }
                }, token)).EnsureSuccessStatusCode();
            (await browser.PostAsJsonAsync($"/api/flow/typed/sessions/{id}/drain", new { session }, token)).EnsureSuccessStatusCode();
            var snapshot = await browser.GetFromJsonAsync<JsonElement>($"/api/flow/typed/sessions/{id}", token);
            Assert.True(snapshot.GetProperty("executions")[0].GetProperty("success").GetBoolean());
            var trace = Assert.Single(snapshot.GetProperty("trace").EnumerateArray(), t => t.GetProperty("eventName").GetString() == "MemoryQueryCompleted");
            Assert.Equal("2", trace.GetProperty("memoryMetadata").GetProperty("returnedItemCount").GetString());
            Assert.DoesNotContain("não brincar", snapshot.GetRawText());
            Assert.DoesNotContain("fake private text", snapshot.GetRawText());
            Assert.DoesNotContain("PLEX73", snapshot.GetRawText());
        }
        finally { directory.Delete(true); }
    }

    private sealed class NeverExternal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test must not call external services.");
    }
}
