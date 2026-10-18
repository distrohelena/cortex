using System.Net;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Inference;
using Cortex.Inference;
using Cortex.Inference.OpenAi;
using Cortex.Inference.LmStudio;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class NativeResponseObservabilityTests
{
    private const string Credential = "provider-credential-without-prefix";
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false,
        properties = new { choice = new { type = "string", @enum = new[] { "yes", "no" } } }, required = new[] { "choice" } });
    private static FlowCompletionRequest Request(string provider) => new("analysis", "test", JsonSerializer.SerializeToElement(new { value = "test" }),
        Schema, provider == "openai" ? "test-model" : "jev-1.13.0", provider, null);

    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }); }
    }

    private static IFlowCompletion Bind(IFlowCompletion completion, FlowCompletionRequest request, List<InferenceAttemptObservation> events,
        bool capture = false)
    {
        var authority = new HostInferenceAuthority(observe: events.Add);
        var profile = new HostInferenceProfile("p", "1", completion, request, _ => { })
            { DiagnosticOptions = new() { CaptureRawResponses = capture, RedactResponse = value => value } };
        var scope = new InferenceScope("test");
        return authority.Bind(authority.Grant(scope, "test", [profile]), scope, "p", new("run", "analysis", "1"));
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("incomplete", false)]
    public async Task OpenAiReportsUsageBeforeResponseStatusValidation(string status, bool success)
    {
        var payload = $$"""{"model":"observed-model","status":"{{status}}","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":23,"output_tokens":7},"echo":"{{Credential}}"}""";
        using var http = new HttpClient(new Handler(payload));
        var request = Request("openai");
        var events = new List<InferenceAttemptObservation>();
        var bound = Bind(new OpenAiFlowCompletion(http, () => Credential), request, events, capture: true);
        if (success) Assert.Equal("ok", await bound.CompleteAsync(request, CancellationToken.None));
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => bound.CompleteAsync(request, CancellationToken.None));
            Assert.Equal(FlowFailureCategory.Provider, FlowDiagnostics.GetFailure(error)!.Category);
        }
        var observation = Assert.Single(events);
        Assert.Equal(23, observation.InputTokens);
        Assert.Equal(7, observation.OutputTokens);
        Assert.Equal("observed-model", observation.ObservedModel);
        Assert.Null(observation.Cost);
        Assert.DoesNotContain(Credential, observation.CapturedResponse!);
        Assert.Contains("[REDACTED]", observation.CapturedResponse!);
        Assert.DoesNotContain("echo", JsonSerializer.Serialize(observation));
    }

    [Fact]
    public async Task SystemOneReportsUsageBeforeInvalidAnswersAndRetainsValidationCause()
    {
        const string payload = """{"model":"jev-1.13.0","usage":{"input_tokens":31,"output_tokens":9},"answers":{"choice":{"type":"choice","choice":"yes","confidence":0.9,"probabilities":{"yes":0.7,"no":0.7}}}}""";
        using var http = new HttpClient(new Handler(payload));
        var client = new SystemOneClient(http, new Uri("https://example.invalid/jev"), () => Credential, true);
        var request = Request("jev");
        var events = new List<InferenceAttemptObservation>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(new SystemOneFlowCompletion(client, "jev", 0.5), request, events)
            .CompleteAsync(request, CancellationToken.None));
        var diagnostic = FlowDiagnostics.GetFailure(error)!;
        Assert.Equal(FlowFailureCategory.Validation, diagnostic.Category);
        Assert.Equal(FlowFailureOrigin.Adapter, diagnostic.Origin);
        Assert.Contains("probability_mass_mismatch", diagnostic.Exception!.Message);
        Assert.Contains("ValidateAnswer", diagnostic.Exception.StackTrace!);
        Assert.Equal(31, Assert.Single(events).InputTokens);
        Assert.Equal(9, events[0].OutputTokens);
        Assert.Null(events[0].Cost);
    }

    [Fact]
    public async Task RoutedHostBindingRecordsOnlyOneAttemptWithTheSameCorrelationId()
    {
        using var http = new HttpClient(new Handler("""{"model":"observed-model","status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"{\"value\":\"ok\"}"}]}],"usage":{"input_tokens":10,"output_tokens":4}}"""));
        var graph = FailureObservabilityTests.PromptGraph();
        var expected = new FlowCompletionRequest("ai", "", JsonSerializer.SerializeToElement(new { }), TypedFlowRunner.ResponseSchema(graph.Nodes["ai"]), null, null, null);
        var events = new List<InferenceAttemptObservation>();
        var authority = new HostInferenceAuthority(observe: events.Add);
        var scope = new InferenceScope("test");
        var completion = new RewriteProvider(new OpenAiFlowCompletion(http, () => Credential));
        var profile = HostInferenceProfile.ForRuntime("p", "1", completion, expected);
        var bound = authority.Bind(authority.Grant(scope, "test", [profile]), scope, "p", new("run", "ai", "1"));
        var router = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion> { ["lmstudio"] = bound });
        var result = await TypedFlowRunner.RunAsync(graph, FailureObservabilityTests.Message(), router, CancellationToken.None);
        Assert.True(result.Success);
        var attempt = Assert.Single(result.InferenceAttempts);
        Assert.Equal(Assert.Single(events).AttemptId, attempt.AttemptId);
        Assert.Equal(10, attempt.Usage.InputTokens);
        Assert.Equal("completed", attempt.Outcome);
        Assert.Equal(InferenceAttemptStage.AdapterReturnedPendingValidation, events[0].Stage);
    }

    private sealed class RewriteProvider(IFlowCompletion inner) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            inner.CompleteAsync(request with { Provider = "openai", Model = "test-model" }, token);
    }

    [Fact]
    public async Task LmStudioRetainsUsageWhenContentIsMissing()
    {
        using var http = new HttpClient(new Handler("""{"model":"observed-local","usage":{"prompt_tokens":15,"completion_tokens":6},"choices":[]}"""));
        var completion = new FlowModelCompletion(new LmStudioClient(http, new LmStudioOptions()), "default");
        var request = Request("lmstudio");
        var events = new List<InferenceAttemptObservation>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(completion, request, events).CompleteAsync(request, CancellationToken.None));
        Assert.Equal(15, Assert.Single(events).InputTokens);
        Assert.Equal(6, events[0].OutputTokens);
        Assert.Equal("observed-local", events[0].ObservedModel);
        Assert.Null(events[0].Cost);
    }

    [Fact]
    public async Task OptInProviderErrorCaptureScrubsCredentialAndKeepsStatus()
    {
        var handler = new Handler("provider rejected " + Credential, HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var request = Request("openai");
        var events = new List<InferenceAttemptObservation>();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Bind(new OpenAiFlowCompletion(http, () => Credential), request, events, capture: true)
            .CompleteAsync(request, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal("provider rejected [REDACTED]", Assert.Single(events).CapturedResponse);
        Assert.Null(events[0].InputTokens);
        Assert.DoesNotContain(Credential, error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MalformedProviderJsonRetainsParsingExceptionAndDoesNotInventUsage()
    {
        using var http = new HttpClient(new Handler("{private-invalid-json"));
        var request = Request("openai");
        var events = new List<InferenceAttemptObservation>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(new OpenAiFlowCompletion(http, () => Credential), request, events)
            .CompleteAsync(request, CancellationToken.None));
        var diagnostic = FlowDiagnostics.GetFailure(error)!;
        Assert.IsAssignableFrom<JsonException>(diagnostic.Exception);
        Assert.Equal(FlowFailureCategory.Parsing, diagnostic.Category);
        Assert.Null(Assert.Single(events).InputTokens);
        Assert.DoesNotContain("private-invalid-json", JsonSerializer.Serialize(events));
    }
}
