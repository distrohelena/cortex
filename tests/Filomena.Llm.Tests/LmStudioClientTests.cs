using System.Net;
using System.Text;
using System.Text.Json;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class LmStudioClientTests
{
    [Fact]
    public async Task GeneratesFreeTextWithSameModelWithoutArbitrationSchema()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"Oi, Helena!"}}]}""");
        using var http = new HttpClient(handler);
        var client = new LmStudioClient(http, new LmStudioOptions());
        var text = await client.CompleteResponseAsync("system", "oi", TestContext.Current.CancellationToken);
        Assert.Equal("Oi, Helena!", text);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("google/gemma-4-e4b", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
        Assert.Equal(512, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task UsesConfiguredLoadedModelAndRequestsStructuredJson()
    {
        var handler = new CapturingHandler("""
            { "choices": [{ "message": { "content": "{\"decision\":\"ignore\",\"reason\":\"no_useful_contribution\"}" } }] }
            """);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1/"),
        };
        var client = new LmStudioClient(http, new LmStudioOptions
        {
            Model = "google/gemma-4-e4b",
            ArbitrationTemperature = 0.1,
            ArbitrationMaxTokens = 160,
        });

        var result = await client.CompleteArbitrationJsonAsync(
            "system",
            "recent conversation",
            TestContext.Current.CancellationToken);

        Assert.Contains("ignore", result, StringComparison.Ordinal);
        Assert.NotNull(handler.RequestBody);
        using var body = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("google/gemma-4-e4b", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.1, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(160, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(
            "json_schema",
            body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("/v1/chat/completions", handler.RequestUri?.AbsolutePath);
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
