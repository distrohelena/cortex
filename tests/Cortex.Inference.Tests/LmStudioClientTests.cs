using System.Net;
using System.Text;
using System.Text.Json;
using Cortex.Inference.LmStudio;
using Xunit;

namespace Cortex.Inference.Tests;

public sealed class LmStudioClientTests
{
    [Fact]
    public async Task RejectsTruncatedEvenSyntacticallyValidStructuredOutput()
    {
        var handler = new CapturingHandler("""{"choices":[{"finish_reason":"length","message":{"content":"{\"mentions\":[]}"}}]}""");
        using var http = new HttpClient(handler);
        var client = new LmStudioClient(http, new LmStudioOptions());
        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteStructuredAsync("system", "segments", "gemma", JsonSerializer.SerializeToElement(new { type = "object" }), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task InfiniteRequestDeadlineStillHonorsExplicitCancellation()
    {
        using var http = new HttpClient(new CapturingHandler("{}")) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new LmStudioClient(http, new LmStudioOptions { RequestTimeoutSeconds = -1 });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteResponseAsync("system", "fixture", cancellation.Token));
    }

    [Fact]
    public async Task SendsEvaluationSpecificSchemaWithSeparateDecisionBranches()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"{}"}}]}""");
        using var http = new HttpClient(handler);
        var client = new LmStudioClient(http, new LmStudioOptions());
        var schema = LmStudioClient.CreateArbitrationSchema(["m1", "m2"], ["p1"]);
        await client.CompleteArbitrationJsonAsync("system", "context", schema, TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var branches = body.RootElement.GetProperty("response_format").GetProperty("json_schema")
            .GetProperty("schema").GetProperty("anyOf");
        var respond = branches[0].GetProperty("properties");
        Assert.Equal("respond", respond.GetProperty("decision").GetProperty("enum")[0].GetString());
        Assert.Equal(new string?[] { "m1", "m2", null }, respond.GetProperty("anchorRef").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new string?[] { "p1", null }, respond.GetProperty("targetRef").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("null", respond.GetProperty("recheckAfterSeconds").GetProperty("type").GetString());
        var wait = branches[1].GetProperty("properties");
        Assert.Equal("integer", wait.GetProperty("recheckAfterSeconds").GetProperty("type").GetString());
        Assert.Equal("null", wait.GetProperty("targetRef").GetProperty("type").GetString());
        Assert.Equal("null", branches[2].GetProperty("properties").GetProperty("recheckAfterSeconds").GetProperty("type").GetString());
    }

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
        Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task DefaultArbitrationBudgetAllowsReasoningAndUsesRequestOwnedTimeout()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"{}"}}]}""");
        using var http = new HttpClient(handler);
        var options = new LmStudioOptions();
        var client = new LmStudioClient(http, options);
        await client.CompleteArbitrationJsonAsync("system", "oi", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(180, options.RequestTimeoutSeconds);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
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
