using System.Net;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm.OpenAi;
using Filomena.Llm;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class OpenAiFlowCompletionTests
{
    [Fact]
    public async Task InstructionOptionsOverrideProviderDefaults()
    {
        var handler = new Handler("""{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}""");
        using var http = new HttpClient(handler);
        await new OpenAiFlowCompletion(http, () => "test-only").CompleteAsync(Request() with { ReasoningEffort = "low", MaxOutputTokens = 512 }, TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("low", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(512, body.RootElement.GetProperty("max_output_tokens").GetInt32());
    }
    [Fact]
    public async Task UsesOfficialResponsesWithStrictSchemaAndExternalCredential()
    {
        var handler = new Handler("""
            {"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"{\"resultado\":\"permitir\",\"explicacao\":\"ok\"}"}]}],"usage":{"input_tokens":10,"output_tokens":20,"output_tokens_details":{"reasoning_tokens":12}}}
            """);
        using var http = new HttpClient(handler);
        var client = new OpenAiFlowCompletion(http, () => "test-only-secret");
        var result = await client.CompleteAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Contains("permitir", result);
        Assert.Equal("https://api.openai.com/v1/responses", handler.Uri);
        Assert.Equal("Bearer test-only-secret", handler.Authorization);
        Assert.DoesNotContain("test-only-secret", handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("medium", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal("gpt-5.6-luna", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("failed")]
    public async Task IncompleteResponsesNeverBecomeApproval(string status)
    {
        using var http = new HttpClient(new Handler($$"""{"status":"{{status}}","output":[]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenAiFlowCompletion(http, () => "secret")
            .CompleteAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HttpErrorDoesNotExposeResponseBodyOrKey()
    {
        using var http = new HttpClient(new Handler("sensitive body", HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new OpenAiFlowCompletion(http, () => "secret")
            .CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("sensitive", error.ToString());
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialMakesNoRequest()
    {
        var handler = new Handler("{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiFlowCompletion(http, () => null)
            .CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Null(handler.Uri);
    }

    [Theory]
    [InlineData("lmstudio")]
    [InlineData("openai")]
    public async Task RouterSelectsOnlyConfiguredProvider(string provider)
    {
        var selected = new RecordingCompletion();
        var other = new RecordingCompletion();
        var router = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            [provider] = selected,
            [provider == "openai" ? "lmstudio" : "openai"] = other
        });
        Assert.Equal("selected", await router.CompleteAsync(Request() with { Provider = provider }, TestContext.Current.CancellationToken));
        Assert.Equal(1, selected.Calls);
        Assert.Equal(0, other.Calls);
        await Assert.ThrowsAsync<InvalidDataException>(() => router.CompleteAsync(Request() with { Provider = "unknown" }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"no"}]}]}""")]
    [InlineData("""{"status":"completed","output":[]}""")]
    public async Task RefusalOrEmptyCannotBecomeApproval(string payload)
    {
        using var http = new HttpClient(new Handler(payload));
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenAiFlowCompletion(http, () => "secret")
            .CompleteAsync(Request(), TestContext.Current.CancellationToken));
    }

    private sealed class RecordingCompletion : IFlowCompletion
    {
        public int Calls { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult("selected");
        }
    }

    internal static FlowCompletionRequest Request() => new("validator", BoundaryValidatorProbeTests.Prompt,
        JsonSerializer.SerializeToElement(new { resposta = "synthetic" }), BoundaryValidatorProbeTests.Contract.ToJsonSchema(),
        "gpt-5.6-luna", "openai", null);

    private sealed class Handler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(status) { Content = new StringContent(response) };
        }
    }
}
