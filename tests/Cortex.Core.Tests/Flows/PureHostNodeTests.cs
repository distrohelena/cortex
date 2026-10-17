using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Tests.Durable;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class PureHostNodeTests
{
    private static readonly FlowNodeHandler Entry = new(new NodeDefinition("entry", 1, FlowEffect.Pure,
        dataOutputs: [new("value", new NumberType())], controlOutputs: [new("then")]),
        (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(3) }), isEntry: true);

    private static FlowNodeHandler Pure(string kind, Action? evaluated = null, FlowEffect effect = FlowEffect.Pure,
        bool withControl = false, bool invalidOutput = false, bool block = false, Exception? failure = null,
        TaskCompletionSource? started = null) =>
        new(new NodeDefinition(kind, 1, effect,
            dataInputs: [new("value", new NumberType(), Required: true)],
            dataOutputs: [new("left", new NumberType()), new("right", new NumberType())],
            controlInputs: withControl ? [new("exec")] : [],
            controlOutputs: withControl ? [new("then")] : []) { ExecutionMode = FlowNodeExecutionMode.Pure },
            async (_, inputs, token) =>
            {
                evaluated?.Invoke();
                started?.TrySetResult();
                if (failure is not null) throw failure;
                if (block) await Task.Delay(Timeout.Infinite, token);
                var number = inputs["value"].Value.GetInt32();
                return new Dictionary<string, JsonElement>
                {
                    ["left"] = invalidOutput ? JsonSerializer.SerializeToElement("wrong") : JsonSerializer.SerializeToElement(number + 1),
                    ["right"] = JsonSerializer.SerializeToElement(number + 2)
                };
            });

    private static FlowNodeHandler Consumer(string kind, FlowEffect effect = FlowEffect.Pure) =>
        new(new NodeDefinition(kind, 1, effect,
            dataInputs: [new("left", new NumberType(), Required: true), new("right", new NumberType(), Required: true)],
            dataOutputs: [new("sum", new NumberType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["sum"] = JsonSerializer.SerializeToElement(
                    inputs["left"].Value.GetInt32() + inputs["right"].Value.GetInt32()) }));

    private static FlowNodeHandler Producer() =>
        new(new NodeDefinition("maker", 1, FlowEffect.Pure, dataOutputs: [new("value", new NumberType())],
            controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(7) }));

    private static FlowNodeHandler Sink() =>
        new(new NodeDefinition("sink", 1, FlowEffect.Pure, controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));

    private static JsonElement Graph(params (string Source, string SourcePort, string Target, string TargetPort)[] edges) =>
        JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "pure", kind = "pure" }, new { id = "consumer", kind = "consumer" } },
            edges = edges.Select(edge => new { source = edge.Source, sourcePort = edge.SourcePort, target = edge.Target, targetPort = edge.TargetPort }).ToArray(),
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });

    [Fact]
    public async Task PureHostWithoutControlPortsRunsOnDemandAndCachesFanOutPerConsumer()
    {
        var evaluations = 0;
        var graph = Graph(("entry", "value", "pure", "value"), ("pure", "left", "consumer", "left"), ("pure", "right", "consumer", "right"));
        var flow = TypedFlowCompiler.Compile(graph, "entry", [Entry, Pure("pure", () => evaluations++), Consumer("consumer")]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, evaluations);
        Assert.Equal(9, result.Outputs["consumer"]["sum"].Value.GetInt32());
    }

    [Fact]
    public async Task BuiltInCountUsesPureModeAndDoesNotPersistDerivedOutput()
    {
        Assert.True(NodeCatalog.V1.TryGet("dm-input", 1, out var dmInputDefinition));
        Assert.Equal(FlowNodeExecutionMode.Entry, dmInputDefinition.ExecutionMode);

        var entry = new FlowNodeHandler(new NodeDefinition("numbers", 1, FlowEffect.Pure,
            dataOutputs: [new("list", new ArrayType(new NumberType()))], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
            {
                ["list"] = JsonSerializer.SerializeToElement(new[] { 3, 4, 5 })
            }), isEntry: true);
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "numbers" }, new { id = "count", kind = "count" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "list", target = "count", targetPort = "list" },
                new { source = "count", sourcePort = "count", target = "consumer", targetPort = "left" },
                new { source = "count", sourcePort = "count", target = "consumer", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var flow = TypedFlowCompiler.Compile(graph, "entry", [entry, Consumer("consumer")]);
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), IncrementalKernelTests.Identity(), null);

        var completed = await FlowExecutionKernel.AdvanceAsync(flow, state, new(new IncrementalKernelTests.ScriptedModel()), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.Equal(6, completed.Result.Outputs["consumer"]["sum"].Value.GetInt32());
        Assert.False(state.Snapshot.Values.ContainsKey(new PortRef("count", "count")));
    }

    [Theory]
    [InlineData(FlowEffect.Model)]
    [InlineData(FlowEffect.WriteState)]
    public void PureHostRejectsEffectsAndControlPorts(FlowEffect effect)
    {
        Assert.Throws<ArgumentException>(() => Pure("bad", effect: effect));
        Assert.Throws<ArgumentException>(() => Pure("bad", withControl: true));
    }

    [Fact]
    public void EntryHandlerNormalizesModeAndRejectsPureConflict()
    {
        Assert.Equal(FlowNodeExecutionMode.Entry, Entry.Definition.ExecutionMode);
        var conflicting = new NodeDefinition("bad-entry", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new NumberType())], controlOutputs: [new("then")])
        { ExecutionMode = FlowNodeExecutionMode.Pure };

        Assert.Throws<ArgumentException>(() => new FlowNodeHandler(conflicting,
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()), isEntry: true));
        var explicitEntry = new NodeDefinition("explicit-entry", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new NumberType())], controlOutputs: [new("then")])
        { ExecutionMode = FlowNodeExecutionMode.Entry };
        Assert.True(new FlowNodeHandler(explicitEntry,
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>())).IsEntry);
    }

    [Fact]
    public async Task PureHostIsEvaluatedOnceForEachConsumerActivation()
    {
        var evaluations = 0;
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "pure", kind = "pure" },
                new { id = "first", kind = "consumer" }, new { id = "second", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "left", target = "first", targetPort = "left" },
                new { source = "pure", sourcePort = "right", target = "first", targetPort = "right" },
                new { source = "pure", sourcePort = "left", target = "second", targetPort = "left" },
                new { source = "pure", sourcePort = "right", target = "second", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "first", targetPort = "exec" },
                new { source = "first", sourcePort = "then", target = "second", targetPort = "exec" } }
        });
        TypedFlow flow;
        try { flow = TypedFlowCompiler.Compile(graph, "entry", [Entry, Pure("pure", () => evaluations++), Consumer("consumer")]); }
        catch (TypedFlowException exception)
        {
            Assert.Fail(string.Join(Environment.NewLine, exception.Issues.Select(issue => $"{issue.NodeId}.{issue.Port}: {issue.Reason}")));
            return;
        }

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void PureDataCycleFailsCompilation()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "pure", kind = "pure" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "left", target = "consumer", targetPort = "left" },
                new { source = "consumer", sourcePort = "sum", target = "pure", targetPort = "value" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });

        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [Entry, Pure("pure"), Consumer("consumer")]));
    }

    [Fact]
    public void PureProducerAvailableOnlyOnOneBranchFailsCompilation()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[] { new { id = "entry", kind = "entry" }, new { id = "choose", kind = "branch", settings = new { @operator = "eq", b = 3 } },
                new { id = "maker", kind = "maker" }, new { id = "pure", kind = "pure" }, new { id = "after", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "choose", targetPort = "a" },
                new { source = "maker", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "left", target = "after", targetPort = "left" },
                new { source = "pure", sourcePort = "right", target = "after", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "choose", targetPort = "exec" },
                new { source = "choose", sourcePort = "true", target = "maker", targetPort = "exec" },
                new { source = "choose", sourcePort = "false", target = "after", targetPort = "exec" },
                new { source = "maker", sourcePort = "then", target = "after", targetPort = "exec" } }
        });

        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [Entry, Producer(), Pure("pure"), Consumer("after")]));
    }

    [Fact]
    public async Task PureOutputValidationFailurePreventsConsumer()
    {
        var consumerCalls = 0;
        var consumer = new FlowNodeHandler(Consumer("consumer").Definition,
            (_, inputs, _) =>
            {
                consumerCalls++;
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                    { ["sum"] = JsonSerializer.SerializeToElement(inputs["left"].Value.GetInt32()) });
            });
        var flow = TypedFlowCompiler.Compile(Graph(("entry", "value", "pure", "value"), ("pure", "left", "consumer", "left"), ("pure", "right", "consumer", "right")),
            "entry", [Entry, Pure("pure", invalidOutput: true), consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, consumerCalls);
    }

    [Fact]
    public async Task PureFailurePreventsConsumerAndIsAttributedToPureNode()
    {
        var consumerCalls = 0;
        var consumer = new FlowNodeHandler(Consumer("consumer").Definition,
            (_, _, _) =>
            {
                consumerCalls++;
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                    { ["sum"] = JsonSerializer.SerializeToElement(0) });
            });
        var flow = TypedFlowCompiler.Compile(Graph(("entry", "value", "pure", "value"), ("pure", "left", "consumer", "left"), ("pure", "right", "consumer", "right")),
            "entry", [Entry, Pure("pure", failure: new InvalidDataException("pure failed")), consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, consumerCalls);
        Assert.Equal("pure", result.Steps[^1].NodeId);
        Assert.Equal("failed", result.Steps[^1].Status);
        Assert.Contains("pure", result.Steps[^1].Error, StringComparison.Ordinal);
        Assert.Contains("left", result.Steps[^1].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrdinaryPureHandlerFailureMakesExecutionTerminalAndKeepsOriginTrace()
    {
        var flow = TypedFlowCompiler.Compile(Graph(("entry", "value", "pure", "value"), ("pure", "left", "consumer", "left"), ("pure", "right", "consumer", "right")),
            "entry", [Entry, Pure("pure", failure: new NotSupportedException("unsupported pure operation")), Consumer("consumer")]);
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), IncrementalKernelTests.Identity(), null);
        var traces = new List<TypedNodeExecutionTrace>();

        var step = await FlowExecutionKernel.AdvanceAsync(flow, state,
            new(new IncrementalKernelTests.ScriptedModel()) { Observer = trace => traces.Add(trace) }, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Failed, step.Status);
        Assert.Null(state.Snapshot.Cursor);
        Assert.Equal("pure", step.Result.Steps[^1].NodeId);
        Assert.Equal("failed", step.Result.Steps[^1].Status);
        Assert.Contains("pure", step.Result.Steps[^1].Error, StringComparison.Ordinal);
        Assert.Contains("left", step.Result.Steps[^1].Error, StringComparison.Ordinal);
        var failureTrace = Assert.Single(traces, trace => trace.Status == "failed");
        Assert.Equal("pure", failureTrace.NodeId);
        Assert.Equal("left", failureTrace.Metadata!["port"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => FlowExecutionKernel.AdvanceAsync(flow, state,
            new(new IncrementalKernelTests.ScriptedModel()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PureHandlerIsNotEvaluatedForAnUnchosenBranch()
    {
        var evaluations = 0;
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[] { new { id = "entry", kind = "entry" },
                new { id = "choose", kind = "branch", settings = new { @operator = "eq", b = 3 } },
                new { id = "pure", kind = "pure" }, new { id = "consumer", kind = "consumer" }, new { id = "sink", kind = "sink" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "choose", targetPort = "a" },
                new { source = "entry", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "left", target = "consumer", targetPort = "left" },
                new { source = "pure", sourcePort = "right", target = "consumer", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "choose", targetPort = "exec" },
                new { source = "choose", sourcePort = "true", target = "sink", targetPort = "exec" },
                new { source = "choose", sourcePort = "false", target = "consumer", targetPort = "exec" } }
        });
        var flow = TypedFlowCompiler.Compile(graph, "entry", [Entry, Pure("pure", () => evaluations++), Consumer("consumer"), Sink()]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, evaluations);
        Assert.Contains(result.Steps, step => step.NodeId == "sink");
        Assert.DoesNotContain(result.Steps, step => step.NodeId == "consumer");
    }

    [Fact]
    public async Task CancellationDuringPureEvaluationStopsConsumerAndCancelsActivation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerCalls = 0;
        var consumer = new FlowNodeHandler(Consumer("consumer").Definition,
            (_, _, _) =>
            {
                consumerCalls++;
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                    { ["sum"] = JsonSerializer.SerializeToElement(0) });
            });
        var flow = TypedFlowCompiler.Compile(Graph(("entry", "value", "pure", "value"), ("pure", "left", "consumer", "left"), ("pure", "right", "consumer", "right")),
            "entry", [Entry, Pure("pure", block: true, started: started), consumer]);
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), IncrementalKernelTests.Identity(), null);
        using var cancellation = new CancellationTokenSource();
        var traces = new List<TypedNodeExecutionTrace>();
        var services = new FlowExecutionServices(new IncrementalKernelTests.ScriptedModel()) { Observer = trace => traces.Add(trace) };
        var advance = FlowExecutionKernel.AdvanceAsync(flow, state, services, cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => advance);

        Assert.Equal(0, consumerCalls);
        Assert.Equal(ExecutionStatus.Cancelled, state.Snapshot.Status);
        var cancelledTrace = Assert.Single(traces, trace => trace.Status == "cancelled");
        Assert.Equal("pure", cancelledTrace.NodeId);
        Assert.Equal("left", cancelledTrace.Metadata!["port"]);
    }

    [Fact]
    public async Task DurableRestartRecomputesPureOutputsWithoutRepeatingCompletedOperation()
    {
        var producerCalls = 0;
        var pureCalls = 0;
        var entry = Entry;
        var producer = new FlowNodeHandler(new NodeDefinition("producer", 1, FlowEffect.WriteState,
            dataOutputs: [new("value", new NumberType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, _, _) =>
            {
                producerCalls++;
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                    new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(10) });
            });
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "producer", kind = "producer" },
                new { id = "pure", kind = "pure" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "producer", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "left", target = "consumer", targetPort = "left" },
                new { source = "pure", sourcePort = "right", target = "consumer", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "producer", targetPort = "exec" },
                new { source = "producer", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var pure = Pure("pure", () => pureCalls++);
        var flow = TypedFlowCompiler.Compile(graph, "entry", [entry, producer, pure, Consumer("consumer")]);
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), IncrementalKernelTests.Identity(), null);
        var services = new FlowExecutionServices(new IncrementalKernelTests.ScriptedModel());

        await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken);
        await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken);

        Assert.Equal(1, producerCalls);
        Assert.Equal("consumer", state.Snapshot.Cursor);
        Assert.False(state.Snapshot.Values.ContainsKey(new PortRef("pure", "left")));
        Assert.False(state.Snapshot.Values.ContainsKey(new PortRef("pure", "right")));
        state.Snapshot.Values[new("pure", "left")] = new(new NumberType(), JsonSerializer.SerializeToElement(999));
        var restored = new FlowExecutionState(ExecutionSnapshotCodec.Decode(ExecutionSnapshotCodec.Encode(state.Snapshot), state.Snapshot.Scope), state.RuntimeOutputs);

        var completed = await FlowExecutionKernel.AdvanceAsync(flow, restored, services, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.Equal(1, producerCalls);
        Assert.Equal(1, pureCalls);
        Assert.Equal(23, completed.Result.Outputs["consumer"]["sum"].Value.GetInt32());
        Assert.False(restored.Snapshot.Values.ContainsKey(new PortRef("pure", "left")));
        Assert.False(restored.Snapshot.Values.ContainsKey(new PortRef("pure", "right")));
    }

    [Fact]
    public void ResolvedPureDefinitionWithControlPortsFailsCompilation()
    {
        var dynamic = new NodeDefinition("dynamic-pure", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType(), Required: true)], dataOutputs: [new("value", new NumberType())],
            resolver: _ => new([new("value", new NumberType(), Required: true)], [new("value", new NumberType())],
                [new("exec")], [], [])) { ExecutionMode = FlowNodeExecutionMode.Pure };
        var handler = new FlowNodeHandler(dynamic, (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4, nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "dynamic", kind = "dynamic-pure" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "dynamic", targetPort = "value" }, new { source = "dynamic", sourcePort = "value", target = "consumer", targetPort = "left" }, new { source = "dynamic", sourcePort = "value", target = "consumer", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });

        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [Entry, handler, Consumer("consumer")]));
    }

    [Fact]
    public void ResolvedHostPureWithUnconstrainedDataPortFailsCompilation()
    {
        var definition = new NodeDefinition("dynamic-pure", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType(), Required: true)], dataOutputs: [new("value", new NumberType())],
            resolver: _ => new([new("value", new NumberType(), Required: true, AcceptsAny: true)],
                [new("value", new NumberType())], [], [], [])) { ExecutionMode = FlowNodeExecutionMode.Pure };
        var handler = new FlowNodeHandler(definition,
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                { ["value"] = JsonSerializer.SerializeToElement(0) }));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4, nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "dynamic", kind = "dynamic-pure" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "value", target = "dynamic", targetPort = "value" },
                new { source = "dynamic", sourcePort = "value", target = "consumer", targetPort = "left" },
                new { source = "dynamic", sourcePort = "value", target = "consumer", targetPort = "right" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });

        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [Entry, handler, Consumer("consumer")]));
    }
}
