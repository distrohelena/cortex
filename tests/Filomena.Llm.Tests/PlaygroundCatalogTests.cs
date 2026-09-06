using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class PlaygroundCatalogTests
{
    [Fact]
    public async Task UserCatalogReplacesDefaultsAndCanChangeWithoutRestart()
    {
        var directory = Directory.CreateTempSubdirectory("filomena-catalog-");
        try
        {
            var path = Path.Combine(directory.FullName, "model-catalog.json");
            await File.WriteAllTextAsync(path, """{"version":1,"providers":[{"id":"mine","name":"My provider","models":[{"id":"offline-model","name":"Offline model"}]}]}""", TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            await using var app = LocalPromptPlayground.Build(new(), http, 0, directory.FullName);
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var catalog = await browser.GetFromJsonAsync<JsonObject>("/api/model-catalog", TestContext.Current.CancellationToken);
            Assert.Single(catalog!["providers"]!.AsArray());
            Assert.Equal("mine", catalog["providers"]![0]!["id"]!.GetValue<string>());
            Assert.Equal("offline-model", catalog["providers"]![0]!["models"]![0]!["id"]!.GetValue<string>());
            var revision = await browser.GetFromJsonAsync<JsonObject>("/__playground/revision", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path, """{"version":1,"providers":[]}""", TestContext.Current.CancellationToken);
            var empty = await browser.GetFromJsonAsync<JsonObject>("/api/model-catalog", TestContext.Current.CancellationToken);
            Assert.Empty(empty!["providers"]!.AsArray());
            var changed = await browser.GetFromJsonAsync<JsonObject>("/__playground/revision", TestContext.Current.CancellationToken);
            Assert.NotEqual(revision!["revision"]!.GetValue<string>(), changed!["revision"]!.GetValue<string>());
            await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
            using var invalid = await browser.GetAsync("/api/model-catalog", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task StarterCatalogContainsLocalModelExamplesWithoutCallingProvider()
    {
        using var http = new HttpClient();
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var catalog = await browser.GetFromJsonAsync<JsonObject>("/api/model-catalog", TestContext.Current.CancellationToken);
        Assert.Contains(catalog!["providers"]!.AsArray(), p => p!["id"]!.GetValue<string>() == "lmstudio");
    }
}
