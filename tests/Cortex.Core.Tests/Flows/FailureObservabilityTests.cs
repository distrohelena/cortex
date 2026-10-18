using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Inference;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class FailureObservabilityTests
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static readonly FlowCompletionRequest Request = new("analysis", "private prompt", Empty, null, "test-model", "test", null);
    private static readonly InferenceScope Scope = new("test");

    private sealed class Completion(Func<FlowCompletionRequest, CancellationToken, Task<string>> complete) : IFlowCompletion
    {
        public int Calls { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        { Calls++; return complete(request, token); }
    }

    private static IFlowCompletion Bind(IFlowCompletion completion, Action<InferenceAttemptObservation>? observe = null,
        Action<string>? validate = null, FlowDiagnosticOptions? options = null)
    {
        var profile = new HostInferenceProfile("p", "1", completion, Request, validate ?? (_ => { }))
            { DiagnosticOptions = options ?? new() };
        var authority = new HostInferenceAuthority(observe: observe);
        return authority.Bind(authority.Grant(Scope, "principal", [profile]), Scope, "p", new("run", "analysis", "1"));
    }

    private static Task<string> ThrowAtAdapter(Exception error) => throw error;

    [Fact]
    public async Task ProviderExceptionChainAndStackSurviveWithoutLeakingIntoPublicException()
    {
        var original = new HttpRequestException("secret endpoint", new IOException("private inner"), HttpStatusCode.TooManyRequests);
        var events = new List<InferenceAttemptObservation>();
        var completion = new Completion((_, _) => ThrowAtAdapter(original));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Bind(completion, events.Add).CompleteAsync(Request, CancellationToken.None));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        var diagnostic = FlowDiagnostics.GetFailure(error)!;
        Assert.Same(original, diagnostic.Exception);
        Assert.Same(original.InnerException, diagnostic.Exception!.InnerException);
        Assert.Contains(nameof(ThrowAtAdapter), original.StackTrace!);
        Assert.Equal(FlowFailureCategory.Provider, diagnostic.Category);
        Assert.Equal(FlowFailureOrigin.Adapter, diagnostic.Origin);
        Assert.Equal(429, diagnostic.HttpStatusCode);
        Assert.Equal(2, diagnostic.ExceptionTypes.Count);
        Assert.Same(original, Assert.Throws<HttpRequestException>(() => diagnostic.ThrowOriginal()));
        Assert.DoesNotContain("private inner", JsonSerializer.Serialize(events));
        Assert.DoesNotContain("private inner", diagnostic.ToString());
        Assert.DoesNotContain("secret endpoint", diagnostic.ToString());
        Assert.Equal(1, completion.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostValidationRetainsParsingOrSchemaCauseAndUsage(bool parsing)
    {
        Exception original = parsing ? new JsonException("private JSON") : new InvalidDataException("private field");
        var events = new List<InferenceAttemptObservation>();
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(10647, 1331, 0.034604m, "observed-model", "test"));
            return Task.FromResult("private response");
        });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Bind(completion, events.Add, _ => throw original).CompleteAsync(Request, CancellationToken.None));
        var observation = Assert.Single(events);
        Assert.Same(original, observation.Failure!.Exception);
        Assert.Equal(parsing ? FlowFailureCategory.Parsing : FlowFailureCategory.Validation, observation.Failure.Category);
        Assert.Equal(FlowFailureOrigin.HostValidation, observation.Failure.Origin);
        Assert.Same(original, FlowDiagnostics.GetFailure(error)!.Exception);
        Assert.Equal(10647, observation.InputTokens);
        Assert.Equal(1331, observation.OutputTokens);
        Assert.Equal(0.034604m, observation.Cost);
        Assert.Equal("observed-model", observation.ObservedModel);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(observation));
        Assert.Null(observation.CapturedResponse);
    }

    [Fact]
    public async Task DeniedRequestsAreAttributedToRuntimeBeforeInvocation()
    {
        var events = new List<InferenceAttemptObservation>();
        var completion = new Completion((_, _) => throw new Exception("must not run"));
        var error = await Assert.ThrowsAsync<InferenceDeniedException>(() => Bind(completion, events.Add)
            .CompleteAsync(Request with { Model = "forbidden" }, CancellationToken.None));
        Assert.Equal(0, completion.Calls);
        Assert.Equal(FlowFailureOrigin.Runtime, FlowDiagnostics.GetFailure(error)!.Origin);
        Assert.Equal(InferenceAttemptStage.DeniedBeforeInvocation, Assert.Single(events).Stage);
        Assert.Equal(FlowFailureOrigin.Runtime, events[0].Failure!.Origin);
        Assert.Null(events[0].InputTokens);
        Assert.Null(events[0].Cost);
    }

    [Fact]
    public async Task ConsumerFailureIsNotMislabelledAsCortexOrProviderBugAndKeepsCompletedNodes()
    {
        var original = new InvalidOperationException("consumer evidence validation", new FormatException("consumer detail"));
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(10647, 1331, 0.034604m));
            return ThrowAtAdapter(original);
        });
        var bound = Bind(completion);
        var result = await TypedFlowRunner.RunAsync(HostGraph(async token => { await bound.CompleteAsync(Request, token); }), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("done", result.Outputs["first"]["value"].Value.GetString());
        Assert.Contains(result.Steps, s => s.NodeId == "first" && s.Status == "completed");
        Assert.Equal("second", result.Failure!.NodeId);
        Assert.Same(original, result.Failure.Exception);
        Assert.Equal(FlowFailureOrigin.Adapter, result.Failure.Origin);
        Assert.Equal(FlowFailureCategory.Unknown, result.Failure.Category);
        var attempt = Assert.Single(result.InferenceAttempts);
        Assert.Equal("analysis", attempt.NodeId);
        Assert.Equal(0.034604m, attempt.Usage.CostUsd);
        Assert.Equal(1, completion.Calls);
    }

    [Fact]
    public async Task HostNodeExceptionIsPreserved()
    {
        var original = new InvalidOperationException("host node detail", new IOException("inner"));
        var result = await TypedFlowRunner.RunAsync(HostGraph(_ => throw original), CancellationToken.None);
        Assert.Same(original, result.Failure!.Exception);
        Assert.Equal(FlowFailureOrigin.HostNode, result.Failure.Origin);
        Assert.Equal(FlowFailureCategory.Unknown, result.Failure.Category);
        Assert.Contains(nameof(HostNodeExceptionIsPreserved), original.StackTrace!);
    }

    [Fact]
    public async Task ExceptionsThatPreviouslyEscapedStillEscapeWithPartialResult()
    {
        var original = new ArgumentException("host argument");
        var error = await Assert.ThrowsAsync<ArgumentException>(() => TypedFlowRunner.RunAsync(HostGraph(_ => throw original), CancellationToken.None));
        Assert.Same(original, error);
        var result = FlowDiagnostics.GetRunResult(error)!;
        Assert.False(result.Success);
        Assert.Equal("second", result.Failure!.NodeId);
        Assert.Contains("first", result.Outputs.Keys);
    }

    [Fact]
    public async Task CancelledExecutionRetainsUsageAndCompletedNodesAndStillThrows()
    {
        using var source = new CancellationTokenSource();
        var original = new OperationCanceledException("private cancellation", source.Token);
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(30, 10, 0.002m));
            source.Cancel();
            throw original;
        });
        var bound = Bind(completion);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TypedFlowRunner.RunAsync(
            HostGraph(async token => { await bound.CompleteAsync(Request, token); }), source.Token));
        var result = FlowDiagnostics.GetRunResult(error)!;
        Assert.Equal(FlowFailureCategory.Cancellation, result.Failure!.Category);
        Assert.Same(original, result.Failure.Exception);
        Assert.Contains("first", result.Outputs.Keys);
        Assert.Equal(0.002m, Assert.Single(result.InferenceAttempts).Usage.CostUsd);
        Assert.DoesNotContain("private", error.ToString());
    }

    [Theory]
    [InlineData("not-json", FlowFailureCategory.Parsing)]
    [InlineData("{}", FlowFailureCategory.Validation)]
    public async Task RuntimeValidationKeepsEveryAttemptAndItsUsage(string response, FlowFailureCategory expected)
    {
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(10, 5, 0.001m));
            return Task.FromResult(response);
        });
        var result = await TypedFlowRunner.RunAsync(PromptGraph(), Message(), completion, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Failure!.Category);
        Assert.Equal("ai", result.Failure.NodeId);
        Assert.Equal(2, completion.Calls);
        Assert.Equal(2, result.InferenceAttempts.Count);
        Assert.All(result.InferenceAttempts, a => { Assert.Equal("invalid_output", a.Outcome); Assert.Equal(expected, a.Failure!.Category); });
        Assert.Equal(20, result.InferenceAttempts.Sum(a => a.Usage.InputTokens));
        Assert.Equal(0.002m, result.InferenceAttempts.Sum(a => a.Usage.CostUsd));
    }

    [Fact]
    public async Task SuccessfulCorrectionKeepsFailedAttemptWithoutChangingGraphOutcome()
    {
        var calls = 0;
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(10, 5));
            return Task.FromResult(++calls == 1 ? "not json" : "{\"value\":\"ok\"}");
        });
        var result = await TypedFlowRunner.RunAsync(PromptGraph(), Message(), completion, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Null(result.Failure);
        Assert.Equal("invalid_output", result.InferenceAttempts[0].Outcome);
        Assert.Equal("completed", result.InferenceAttempts[1].Outcome);
        Assert.Null(result.InferenceAttempts[1].Usage.CostUsd);
    }

    [Fact]
    public async Task UsageReportsMergeCumulativeSnapshotsWithoutCountingThemTwice()
    {
        var events = new List<InferenceAttemptObservation>();
        var completion = new Completion((request, _) =>
        {
            request.Diagnostics!.ReportUsage(new(12, 4));
            request.Diagnostics.ReportUsage(new(12, 4));
            request.Diagnostics.ReportUsage(new(CostUsd: 0.003m));
            return Task.FromResult("ok");
        });
        await Bind(completion, events.Add).CompleteAsync(Request, CancellationToken.None);
        Assert.Equal(12, Assert.Single(events).InputTokens);
        Assert.Equal(4, events[0].OutputTokens);
        Assert.Equal(0.003m, events[0].Cost);
    }

    [Fact]
    public async Task UnknownUsageStaysUnknown()
    {
        var completion = new Completion((_, _) => Task.FromResult("{\"value\":\"ok\"}"));
        var result = await TypedFlowRunner.RunAsync(PromptGraph(), Message(), completion, CancellationToken.None);
        var usage = Assert.Single(result.InferenceAttempts).Usage;
        Assert.Null(usage.InputTokens); Assert.Null(usage.OutputTokens); Assert.Null(usage.CostUsd); Assert.Null(usage.ObservedModel);
    }

    [Fact]
    public async Task CaptureIsDisabledByDefaultAndDoesNotCallRedactor()
    {
        var calls = 0;
        var options = new FlowDiagnosticOptions { RedactResponse = value => { calls++; return value; } };
        var completion = new Completion((_, _) => Task.FromResult("{\"value\":\"private\"}"));
        var result = await TypedFlowRunner.RunAsync(PromptGraph(), Message(), completion, options, CancellationToken.None);
        Assert.Equal(0, calls);
        Assert.Equal("disabled", Assert.Single(result.InferenceAttempts).CaptureStatus);
        Assert.Null(result.InferenceAttempts[0].CapturedResponse);
    }

    [Fact]
    public void CaptureRequiresHostRedactorBeforeAnyExecution()
    {
        var completion = new Completion((_, _) => throw new Exception("must not run"));
        Assert.Throws<ArgumentException>(() => { _ = TypedFlowRunner.RunAsync(PromptGraph(), Message(), completion,
            new FlowDiagnosticOptions { CaptureRawResponses = true }, CancellationToken.None); });
        Assert.Equal(0, completion.Calls);
    }

    [Fact]
    public async Task CapturedResponseIsRedactedBoundedAndExcludedFromSerialization()
    {
        const string response = "{\"api_key\":\"credential-secret\",\"authorization\":\"Bearer token-secret\",\"person\":\"private-person\",\"key\":\"sk-example-secret\"}";
        var events = new List<InferenceAttemptObservation>();
        var completion = new Completion((_, _) => Task.FromResult(response));
        var bound = Bind(completion, events.Add, options: new() { CaptureRawResponses = true,
            RedactResponse = value => value.Replace("private-person", "[PERSON]") });
        Assert.Equal(response, await bound.CompleteAsync(Request, CancellationToken.None));
        var observation = Assert.Single(events);
        Assert.Contains("[PERSON]", observation.CapturedResponse!);
        Assert.DoesNotContain("credential-secret", observation.CapturedResponse!);
        Assert.DoesNotContain("token-secret", observation.CapturedResponse!);
        Assert.DoesNotContain("sk-example-secret", observation.CapturedResponse!);
        Assert.DoesNotContain("[PERSON]", JsonSerializer.Serialize(observation));
        Assert.DoesNotContain("[PERSON]", observation.ToString());
        Assert.DoesNotContain("credential-secret", observation.ToString());
        Assert.DoesNotContain("private-person", JsonSerializer.Serialize(Request));
    }

    [Theory]
    [InlineData("throw", "redaction_failed")]
    [InlineData("discard", "discarded")]
    [InlineData("truncate", "truncated")]
    public async Task CaptureCannotFailExecution(string behavior, string status)
    {
        var events = new List<InferenceAttemptObservation>();
        var options = new FlowDiagnosticOptions { CaptureRawResponses = true, MaxResponseCharacters = 4,
            RedactResponse = value => behavior switch { "throw" => throw new InvalidOperationException("private redactor"), "discard" => null, _ => value } };
        Assert.Equal("long response", await Bind(new Completion((_, _) => Task.FromResult("long response")), events.Add,
            options: options).CompleteAsync(Request, CancellationToken.None));
        Assert.Equal(status, Assert.Single(events).CaptureStatus);
        Assert.True(events[0].CapturedResponse is null || events[0].CapturedResponse!.Length <= 4);
    }

    [Fact]
    public async Task ConcurrentRunsKeepAttemptsSeparate()
    {
        async Task<TypedRunResult> Run(int tokens)
        {
            var completion = new Completion(async (request, _) =>
            {
                await Task.Yield();
                request.Diagnostics!.ReportUsage(new(tokens, 1));
                return "ok";
            });
            var bound = Bind(completion);
            return await TypedFlowRunner.RunAsync(HostGraph(async token => { await bound.CompleteAsync(Request, token); }), CancellationToken.None);
        }
        var results = await Task.WhenAll(Run(12), Run(34));
        Assert.Equal(12, Assert.Single(results[0].InferenceAttempts).Usage.InputTokens);
        Assert.Equal(34, Assert.Single(results[1].InferenceAttempts).Usage.InputTokens);
        Assert.NotEqual(results[0].InferenceAttempts[0].AttemptId, results[1].InferenceAttempts[0].AttemptId);
    }

    [Fact]
    public async Task DurableSnapshotKeepsUsageAndSafeDiagnosticsWithoutRawPayloadsOrExceptions()
    {
        var completion = new Completion((request, _) => { request.Diagnostics!.ReportUsage(new(10, 4, 0.01m)); return Task.FromResult("not-json-private"); });
        var flow = PromptGraph();
        var state = FlowExecutionKernel.Start(flow, Message(), Durable.IncrementalKernelTests.Identity(), null);
        var services = new FlowExecutionServices(completion) { Diagnostics = new() { CaptureRawResponses = true, RedactResponse = _ => "redacted response" } };
        while (state.Snapshot.Status == ExecutionStatus.Ready)
            await FlowExecutionKernel.AdvanceAsync(flow, state, services, CancellationToken.None);
        var wire = ExecutionSnapshotCodec.Encode(state.Snapshot);
        Assert.DoesNotContain("not-json-private", wire.GetRawText());
        Assert.DoesNotContain("redacted response", wire.GetRawText());
        var restored = ExecutionSnapshotCodec.Decode(wire, state.Snapshot.Scope);
        Assert.Equal(2, restored.InferenceAttempts.Count);
        Assert.Equal(0.02m, restored.InferenceAttempts.Sum(a => a.Usage.CostUsd));
        Assert.Equal(FlowFailureCategory.Parsing, restored.Steps.Last().Diagnostic!.Category);
        Assert.Null(restored.Steps.Last().Diagnostic!.Exception);
        Assert.Null(restored.InferenceAttempts[0].CapturedResponse);
        var legacy = JsonNode.Parse(wire.GetRawText())!.AsObject();
        legacy.Remove("InferenceAttempts");
        foreach (var step in legacy["Steps"]!.AsArray()) step!.AsObject().Remove("Diagnostic");
        var oldSnapshot = ExecutionSnapshotCodec.Decode(JsonSerializer.SerializeToElement(legacy), state.Snapshot.Scope);
        Assert.Empty(oldSnapshot.InferenceAttempts);
        Assert.Equal(state.Snapshot.Steps.Count, oldSnapshot.Steps.Count);
    }

    internal static TypedFlow HostGraph(Func<CancellationToken, Task> second)
    {
        var graph = JsonSerializer.SerializeToElement(new { version = 4,
            nodes = new[] { new { id = "first", kind = "first" }, new { id = "second", kind = "second" } },
            edges = Array.Empty<object>(), controlEdges = new[] { new { source = "first", sourcePort = "then", target = "second", targetPort = "exec" } } });
        return TypedFlowCompiler.Compile(graph, "first", [
            new(new NodeDefinition("first", 1, FlowEffect.WriteState, dataOutputs: [new("value", new StringType())], controlOutputs: [new("then")]),
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("done") }), isEntry: true),
            new(new NodeDefinition("second", 1, FlowEffect.Model, controlInputs: [new("exec")]),
                async (_, _, token) => { await second(token); return new Dictionary<string, JsonElement>(); })]);
    }

    internal static TypedFlow PromptGraph() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(new { version = 4,
        nodes = new object[] { new { id = "in", kind = "dm" }, new { id = "ai", kind = "prompt", prompt = "Return a value",
            responseFields = new[] { new { id = "v", name = "value", type = "text" } } } }, edges = Array.Empty<object>(),
        controlEdges = new[] { new { source = "in", sourcePort = "then", target = "ai", targetPort = "exec" } } }), "in");

    internal static JsonElement Message() => JsonSerializer.SerializeToElement(new { texto = "test", remetente = new { nome = "Test", stableId = "test" },
        canal = new { nome = "DM", nativeId = "dm" }, reply = (object?)null });
}
