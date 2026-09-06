using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class FlowGraphAssetTests
{
    [Fact]
    public async Task ReusableEditorAssetsAreEmbeddedAndServedWithoutDevelopmentFiles()
    {
        using var http = new HttpClient();
        await using var app = LocalPromptPlayground.Build(new(), http, 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (var (route, marker, mime) in new[] {
            ("/flow-graph/index.js", "createFlowGraphEditor", "text/javascript"),
            ("/flow-graph/camera.js", "attachCamera", "text/javascript"),
            ("/flow-graph/editor.css", ".fg-viewport", "text/css"),
            ("/flow.js", "from './flow-graph/index.js'", "text/javascript") })
        {
            using var response = await browser.GetAsync(route, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            Assert.Equal(mime, response.Content.Headers.ContentType!.MediaType);
            Assert.Contains(marker, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }
}
