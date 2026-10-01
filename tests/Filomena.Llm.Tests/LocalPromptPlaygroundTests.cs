using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Filomena.App;
using Cortex.Inference.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class LocalPromptPlaygroundTests
{
    [Fact]
    public async Task ServesPageAndCallsOnlyConfiguredModelWithExplicitInputs()
    {
        var handler = new ModelHandler();
        await using var app = LocalPromptPlayground.Build(new LmStudioOptions(), new HttpClient(handler), 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var page = await browser.GetStringAsync("/", TestContext.Current.CancellationToken);
        Assert.Contains("Testar prompt", page);
        Assert.Equal(0, handler.Calls);
        browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
        using var response = await browser.PostAsJsonAsync("/api/test", new { prompt = "Be brief.", message = "Hello" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Hello back!", result.RootElement.GetProperty("text").GetString());
        Assert.Equal(1, handler.Calls);
        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal("google/gemma-4-e4b", sent.RootElement.GetProperty("model").GetString());
        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("Be brief.", messages[0].GetProperty("content").GetString());
        Assert.Equal("Hello", messages[1].GetProperty("content").GetString());
        Assert.False(sent.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task RejectsBrowserCrossOriginAndInvalidInputWithoutModelCalls()
    {
        var handler = new ModelHandler();
        await using var app = LocalPromptPlayground.Build(new LmStudioOptions(), new HttpClient(handler), 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var denied = await browser.PostAsJsonAsync("/api/test", new { prompt = "p", message = "m" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
        browser.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var crossOrigin = await browser.PostAsJsonAsync("/api/test", new { prompt = "p", message = "m" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        browser.DefaultRequestHeaders.Remove("Origin");
        using var invalid = await browser.PostAsJsonAsync("/api/test", new { prompt = "p", message = " " }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        browser.DefaultRequestHeaders.Host = "untrusted.example";
        using var invalidHost = await browser.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, invalidHost.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ReportsUnavailableModelWithoutLeakingProviderBody()
    {
        var handler = new ModelHandler { Fail = true };
        await using var app = LocalPromptPlayground.Build(new LmStudioOptions(), new HttpClient(handler), 0);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
        using var response = await browser.PostAsJsonAsync("/api/test", new { prompt = "p", message = "m" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("private-provider-details", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private sealed class ModelHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public bool Fail { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(Fail ? "private-provider-details" : """{"choices":[{"message":{"content":"Hello back!"}}]}""")
            };
        }
    }
}
