using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class PlaygroundReloadTests
{
    [Fact]
    public async Task DevelopmentReadsEditedAssetsAndRevisionChangesWithoutRestart()
    {
        var folder = Directory.CreateTempSubdirectory("filomena-reload-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "flow.html"), "<html><body>before</body></html>", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "live-reload.js"), "/* local reload client */", TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            await using var app = LocalPromptPlayground.Build(new(), http, 0, folder.FullName);
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var before = await browser.GetStringAsync("/flow", TestContext.Current.CancellationToken);
            Assert.Contains("before", before);
            Assert.Contains("/__playground/live-reload.js?revision=", before);
            Assert.Equal("/* local reload client */", await browser.GetStringAsync("/__playground/live-reload.js", TestContext.Current.CancellationToken));
            var first = await browser.GetFromJsonAsync<JsonObject>("/__playground/revision", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "flow.html"), "<html><body>after</body></html>", TestContext.Current.CancellationToken);
            var second = await browser.GetFromJsonAsync<JsonObject>("/__playground/revision", TestContext.Current.CancellationToken);
            Assert.NotEqual(first!["revision"]!.GetValue<string>(), second!["revision"]!.GetValue<string>());
            Assert.Contains("after", await browser.GetStringAsync("/flow", TestContext.Current.CancellationToken));
            // Only registered frontend files are served, not arbitrary files in the directory.
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "secret.txt"), "do-not-serve", TestContext.Current.CancellationToken);
            using var secret = await browser.GetAsync("/secret.txt", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task NormalModeKeepsEmbeddedAssetsAndDoesNotExposeReloadEndpoint()
    {
        using var http = new HttpClient();
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.DoesNotContain("/__playground/live-reload.js", await browser.GetStringAsync("/flow", TestContext.Current.CancellationToken));
        using var revision = await browser.GetAsync("/__playground/revision", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, revision.StatusCode);
    }
}
