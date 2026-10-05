using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class IncrementalKernelTests
{
    internal static JsonElement Message() => JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = (object?)null, canal = new { nome = "DM", nativeId = "fixture" }, reply = (object?)null });
    internal static TypedFlow Flow() => TypedFlowCompiler.Compile(ExecutionSnapshotCodecTests.Json("""
        {"version":3,"nodes":[{"id":"in","kind":"dm"},
        {"id":"first","kind":"prompt","prompt":"Write","outputMode":"text","correctionRetry":false,"inputs":[],"responseFields":[{"id":"text","name":"text","type":"text"}]},
        {"id":"second","kind":"prompt","prompt":"Continue","outputMode":"text","correctionRetry":false,"inputs":[{"id":"previous","name":"previous","type":"text","required":true}],"responseFields":[{"id":"text","name":"text","type":"text"}]}],
        "edges":[{"source":"first","sourcePort":"text","target":"second","targetPort":"previous"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"first","targetPort":"exec"},{"source":"first","sourcePort":"then","target":"second","targetPort":"exec"}]}
        """), "in");
    internal static ExecutionStart Identity()
    {
        var snapshot = ExecutionSnapshotCodecTests.Snapshot();
        return new(snapshot.Scope, snapshot.BatchId, snapshot.ProjectRef, snapshot.BatchRef, snapshot.StateId, snapshot.StateVersion, snapshot.EntryKind);
    }
    internal sealed class Model : IFlowCompletion
    {
        internal List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) { Requests.Add(request); return Task.FromResult(request.NodeId == "first" ? "previous answer" : "final answer"); }
    }

    [Fact]
    public async Task PromptThenBranchResumePreservesOutputs()
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Flows.TypedBranchTests.Graph()), "in");
        var model = new ScriptedModel("{\"signal\":true}");
        var state = FlowExecutionKernel.Start(flow, Message(), Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        state.Snapshot.ConfirmedSend = new("send", "provider", "speech", new("Filomena", "self"));
        state.Snapshot.References["stableId:actor"] = "ref1";
        var restored = new FlowExecutionState(ExecutionSnapshotCodec.Decode(ExecutionSnapshotCodec.Encode(state.Snapshot), state.Snapshot.Scope), state.RuntimeOutputs);
        var last = await FlowExecutionKernel.AdvanceAsync(flow, restored, new(new ScriptedModel()), TestContext.Current.CancellationToken);
        Assert.Equal("true", last.Result.Steps.Single(x => x.NodeId == "branch").Exit);
        Assert.Single(model.Requests);
        Assert.Equal("provider", last.Result.ConfirmedSend?.ProviderMessageId);
        Assert.Equal("ref1", restored.Snapshot.References["stableId:actor"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalInputsAndTextStructuredCorrectionAreUnchanged(bool structured)
    {
        var graph = System.Text.Json.Nodes.JsonNode.Parse("""
            {"version":3,"nodes":[{"id":"in","kind":"dm"},
            {"id":"ai","kind":"prompt","prompt":"Write","inputs":[{"id":"optional","name":"optional","type":"text","required":false}],
            "responseFields":[{"id":"text","name":"text","type":"text"}]}],"edges":[],
            "controlEdges":[{"source":"in","sourcePort":"then","target":"ai","targetPort":"exec"}]}
            """
        )!;
        graph["nodes"]![1]!["outputMode"] = structured ? "structured" : "text";
        var model = structured ? new ScriptedModel("not-json", "{\"text\":\"corrected\"}") : new ScriptedModel("", "corrected");
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"), Message(), model, TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("corrected", result.Outputs["ai"]["text"].Value.GetString());
        Assert.Equal(2, model.Requests.Count);
        Assert.NotNull(model.Requests[1].Correction);
        Assert.All(model.Requests, request => Assert.False(request.Inputs.TryGetProperty("optional", out _)));
    }

    internal sealed class ScriptedModel(params string[] responses) : IFlowCompletion
    {
        internal List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            if (Requests.Count > responses.Length) throw new InvalidOperationException("Unexpected model call.");
            return Task.FromResult(responses[Requests.Count - 1]);
        }
    }

    [Fact]
    public async Task PureAdvanceStopsBeforePaidEffect()
    {
        var flow = Flow(); var model = new Model();
        var state = FlowExecutionKernel.Start(flow, Message(), Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        var step = await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        Assert.Empty(model.Requests);
        Assert.Equal("first", step.NextNodeId);
        Assert.Equal(StepEffect.Inference, step.NextEffect);
        Assert.Equal(ExecutionStatus.Ready, step.Status);
    }

    [Fact]
    public async Task OneAdvanceExecutesAtMostOneOperationalNode()
    {
        var flow = Flow(); var model = new Model();
        var state = FlowExecutionKernel.Start(flow, Message(), Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        var first = await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        Assert.Single(model.Requests);
        Assert.Equal("second", first.NextNodeId);
        Assert.Equal("previous answer", state.Snapshot.Values[new("first", "text")].Value.GetString());
        var last = await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, last.Status);
        Assert.Equal("final answer", last.Result.Outputs["second"]["text"].Value.GetString());
    }

    [Fact]
    public async Task RestartKeepsDomainReferencesAndConfirmedSend()
    {
        var flow = Flow(); var model = new Model();
        var state = FlowExecutionKernel.Start(flow, Message(), Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        await FlowExecutionKernel.AdvanceAsync(flow, state, new(model), TestContext.Current.CancellationToken);
        var encoded = ExecutionSnapshotCodec.Encode(state.Snapshot);
        var restored = new FlowExecutionState(ExecutionSnapshotCodec.Decode(encoded, state.Snapshot.Scope), state.RuntimeOutputs);
        var final = await FlowExecutionKernel.AdvanceAsync(flow, restored, new(model), TestContext.Current.CancellationToken);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal("previous answer", model.Requests[1].Inputs.GetProperty("previous").GetString());
        Assert.True(final.Result.Success);
        Assert.Same(FlowDomain.Message, restored.Snapshot.Values[new("in", "message")].Type);
    }
}
