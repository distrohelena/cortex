using System.Net;
using Xunit;
using static Filomena.Llm.Tests.ParticipationSemanticsLiveTests;

namespace Filomena.Llm.Tests;

public sealed class ParticipationUsageTests
{
    [Theory]
    [InlineData("{\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":7,\"completion_tokens_details\":{\"reasoning_tokens\":3}}}", 12L, 7L, 3L)]
    [InlineData("{\"usage\":null}", null, null, null)]
    [InlineData("{}", null, null, null)]
    public async Task TelemetryPreservesBodyAndDoesNotInventMissingUsage(string body, long? prompt, long? completion, long? reasoning)
    {
        using var observer = new UsageObserver(new Response(body));
        using var http = new HttpClient(observer) { BaseAddress = new Uri("http://fixture.invalid/") };
        using var result = await http.PostAsync("v1/chat/completions", new StringContent("{}"), TestContext.Current.CancellationToken);
        Assert.Equal(body, await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var usage = Assert.Single(observer.Calls);
        Assert.Equal(prompt, usage.PromptTokens);
        Assert.Equal(completion, usage.CompletionTokens);
        Assert.Equal(reasoning, usage.ReasoningTokens);
    }

    private sealed class Response(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
