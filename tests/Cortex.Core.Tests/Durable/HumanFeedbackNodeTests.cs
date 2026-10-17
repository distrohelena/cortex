using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class HumanFeedbackNodeTests
{
    internal static JsonNode Graph() => JsonNode.Parse("""
        {"version":4,"nodes":[{"id":"in","kind":"dm"},
        {"id":"human","kind":"humanFeedback","settings":{"question":"Como devo continuar?"}},
        {"id":"ai","kind":"prompt","prompt":"Continue","outputMode":"text","inputs":[{"id":"feedback","name":"feedback","type":"text","required":true}],"responseFields":[{"id":"text","name":"text","type":"text"}]}],
        "edges":[{"source":"human","sourcePort":"feedback","target":"ai","targetPort":"feedback"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"human","targetPort":"exec"},{"source":"human","sourcePort":"then","target":"ai","targetPort":"exec"}]}
        """
    )!;
    [Fact]
    public void FeedbackCompilesWithOptionalTextContext()
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph()), "in");
        var node = flow.Nodes["human"];
        Assert.False(Assert.Single(node.Inputs).Required);
        Assert.IsType<StringType>(Assert.Single(node.Outputs).Type);
        Assert.Equal(["then", "timeout"], node.Exits);
        var catalog = HumanFeedbackNode.Resolve(ExecutionSnapshotCodecTests.Json("{\"question\":\"Review\"}"));
        Assert.Empty(catalog.Issues);
        Assert.Equal("run", Assert.Single(catalog.ControlInputs).Name);
        Assert.False(catalog.ControlOutputs.Single(x => x.Name == "timeout").Required);
    }
    [Theory]
    [InlineData("{\"question\":\"\"}")]
    [InlineData("{\"question\":\"Review\",\"timeoutSeconds\":0}")]
    [InlineData("{\"question\":\"Review\",\"timeoutSeconds\":1.5}")]
    [InlineData("{\"question\":\"Review\",\"timeoutSeconds\":-1}")]
    public void QuestionAndTimeoutAreValidated(string settings)
    {
        var graph = Graph(); graph["nodes"]![1]!["settings"] = JsonNode.Parse(settings);
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }
    [Fact]
    public void ConfiguredTimeoutRequiresControlEdge()
    {
        var graph = Graph(); graph["nodes"]![1]!["settings"]!["timeoutSeconds"] = 30;
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }
    [Fact]
    public void TimeoutPathCannotConsumeUnavailableFeedback()
    {
        var graph = Graph(); graph["nodes"]![1]!["settings"]!["timeoutSeconds"] = 30;
        graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"human","sourcePort":"timeout","target":"ai","targetPort":"exec"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }
    [Fact]
    public async Task FeedbackPausesBeforeDownstreamPrompt()
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph()), "in");
        var model = new IncrementalKernelTests.ScriptedModel();
        var state = FlowExecutionKernel.Start(flow, IncrementalKernelTests.Message(), IncrementalKernelTests.Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        var waiting = await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.WaitingForHuman, waiting.Status);
        Assert.Equal("Como devo continuar?", state.Snapshot.PendingFeedback?.Question);
        Assert.Empty(model.Requests);
        var restored = ExecutionSnapshotCodec.Decode(ExecutionSnapshotCodec.Encode(state.Snapshot), state.Snapshot.Scope);
        Assert.Equal(state.Snapshot.PendingFeedback?.RequestId, restored.PendingFeedback?.RequestId);
        var local = await TypedFlowRunner.RunAsync(flow, IncrementalKernelTests.Message(), model, TestContext.Current.CancellationToken);
        Assert.False(local.Success);
        Assert.Contains(local.Steps, step => step.Error == "human-feedback-backend-required");
    }
    [Fact]
    public void NormalizedCatalogRejectsFeedbackOnTimeoutPath()
    {
        var graph = new GraphDocument("feedback", 1, GraphRole.Character, "character",
            [new("in", "manual-input", 1, ExecutionSnapshotCodecTests.Json("{}")),
                new("human", "humanFeedback", 1, ExecutionSnapshotCodecTests.Json("{\"question\":\"Review\",\"timeoutSeconds\":30}")),
                new("send", "send", 1, ExecutionSnapshotCodecTests.Json("{\"destinationId\":\"route\"}")),
                new("answered", "send", 1, ExecutionSnapshotCodecTests.Json("{\"destinationId\":\"route\"}"))],
            [new DataEdge(new("human", "feedback"), Array.Empty<string>(), new("send", "body")), new DataEdge(new("in", "text"), Array.Empty<string>(), new("answered", "body"))],
            [new(new("in", "success"), new("human", "run")), new(new("human", "then"), new("answered", "run")),
                new(new("human", "timeout"), new("send", "run"))], ["in"]);
        Assert.Contains(FlowValidator.Validate(graph, NodeCatalog.V1), issue => issue.Code == "data.value_unavailable");
    }
    [Fact]
    public void NumericContextIsRejected()
    {
        var graph = Graph();
        graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{"id":"number","kind":"prompt","prompt":"Number","inputs":[],"responseFields":[{"id":"number","name":"number","type":"number"}]}"""));
        graph["controlEdges"]![0]!["target"] = "number";
        graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"number","sourcePort":"then","target":"human","targetPort":"exec"}"""));
        graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"number","sourcePort":"number","target":"human","targetPort":"context"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }
    [Fact]
    public void FingerprintPinsQuestionContextAndRevision()
    {
        var key = new StepKey(DurableIdentityTests.Scope, "human", 1);
        var original = HumanFeedbackNode.Request(key, new("Review", null), "context");
        Assert.Equal(original, HumanFeedbackNode.Request(key, new("Review", null), "context"));
        Assert.NotEqual(original.RequestDigest, HumanFeedbackNode.Request(key, new("Other", null), "context").RequestDigest);
        Assert.NotEqual(original.RequestDigest, HumanFeedbackNode.Request(key, new("Review", null), "other").RequestDigest);
        Assert.NotEqual(original.RequestId, HumanFeedbackNode.Request(key with { Scope = key.Scope with { ProjectRevision = "other" } }, new("Review", null), "context").RequestId);
    }
}
