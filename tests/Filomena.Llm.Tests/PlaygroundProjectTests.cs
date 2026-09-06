using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class PlaygroundProjectTests
{
    private const string Project = """
    {"format":"filomena-local-project","project":{"version":1,"stateScope":"session","initialStateId":"a","states":[{"id":"a","name":"Inicial","graph":{"version":2,"nodes":[{"id":"in","kind":"dm","x":0,"y":0},{"id":"p","kind":"prompt","prompt":"Árbitro","responseFields":[{"name":"motivo","description":"Até 10 palavras","type":"text"}],"x":100,"y":0}],"edges":[],"controlEdges":[{"source":"in","sourcePort":"then","target":"p","targetPort":"exec"}]}}]}}
    """;

    [Fact]
    public async Task SavesEntireDraftAcrossServerRestartAndRejectsUnsafeRequests()
    {
        var directory = Directory.CreateTempSubdirectory("filomena-project-");
        try
        {
            using var http = new HttpClient();
            using var document = JsonDocument.Parse(Project);
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/flow/project", TestContext.Current.CancellationToken)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/flow/project", document.RootElement, TestContext.Current.CancellationToken)).StatusCode);
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                (await browser.PostAsJsonAsync("/api/flow/project", document.RootElement, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/flow/project", new { message = "private chat" }, TestContext.Current.CancellationToken)).StatusCode);
            }
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(document.RootElement, loaded));
            }
            Assert.Single(Directory.GetFiles(directory.FullName));
        }
        finally { directory.Delete(true); }
    }
}
