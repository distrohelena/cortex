using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Tests.Durable;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class OptionalOutputTests
{
    private static readonly FlowNodeHandler Entry = new(new NodeDefinition("entry", 1, FlowEffect.Pure,
        dataOutputs: [new("value", new NumberType())], controlOutputs: [new("then")]),
        (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(3) }), isEntry: true);

    private static FlowNodeHandler Producer(string kind, IReadOnlyDictionary<string, JsonElement> result,
        params FlowDataPortDefinition[] outputs) => new(new NodeDefinition(kind, 1, FlowEffect.Pure,
            dataOutputs: outputs, controlInputs: [new("exec")], controlOutputs: [new("then")]),
        (_, _, _) => Task.FromResult(result));

    private static FlowNodeHandler Consumer(FlowDataPortDefinition input, Func<IReadOnlyDictionary<string, TypedValue>, JsonElement> result) =>
        new(new NodeDefinition("consumer", 1, FlowEffect.Pure,
            dataInputs: [input], dataOutputs: [new("observed", new BooleanType(), Required: true)],
            controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["observed"] = result(inputs) }));

    private static FlowNodeHandler PureMissingOutput() => new(new NodeDefinition("optionalPure", 1, FlowEffect.Pure,
        dataInputs: [new("value", new NumberType(), Required: true)],
        dataOutputs: [new("maybe", new StringType(), Required: false)]) { ExecutionMode = FlowNodeExecutionMode.Pure },
        (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));

    private static JsonElement PureGraph() => JsonSerializer.SerializeToElement(new
    {
        version = 4,
        nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "pure", kind = "optionalPure" }, new { id = "consumer", kind = "consumer" } },
        edges = new[] {
            new { source = "entry", sourcePort = "value", target = "pure", targetPort = "value" },
            new { source = "pure", sourcePort = "maybe", target = "consumer", targetPort = "maybe" }
        },
        controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
    });

    private static JsonElement Graph(string producerKind, string output, string consumerInput) => JsonSerializer.SerializeToElement(new
    {
        version = 4,
        nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "producer", kind = producerKind }, new { id = "consumer", kind = "consumer" } },
        edges = new[] { new { source = "producer", sourcePort = output, target = "consumer", targetPort = consumerInput } },
        controlEdges = new[] {
            new { source = "entry", sourcePort = "then", target = "producer", targetPort = "exec" },
            new { source = "producer", sourcePort = "then", target = "consumer", targetPort = "exec" }
        }
    });

    [Fact]
    public async Task MissingOptionalOutputFlowsAsAbsenceAndSurvivesSnapshotRoundTrip()
    {
        var producer = Producer("producer", new Dictionary<string, JsonElement>(), new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: false), inputs =>
            JsonSerializer.SerializeToElement(!inputs.ContainsKey("maybe")));
        var flow = TypedFlowCompiler.Compile(Graph("producer", "maybe", "maybe"), "entry", [Entry, producer, consumer]);
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), IncrementalKernelTests.Identity(), null);

        var services = new FlowExecutionServices(new IncrementalKernelTests.ScriptedModel());
        var entry = await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken, oneNode: true);
        Assert.Equal(ExecutionStatus.Ready, entry.Status);
        var producerStep = await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken, oneNode: true);
        Assert.Equal(ExecutionStatus.Ready, producerStep.Status);
        Assert.Empty(state.Snapshot.Outputs["producer"]);
        Assert.False(state.Snapshot.Values.ContainsKey(new PortRef("producer", "maybe")));
        var restored = ExecutionSnapshotCodec.Decode(ExecutionSnapshotCodec.Encode(state.Snapshot), state.Snapshot.Scope);
        Assert.Empty(restored.Outputs["producer"]);
        Assert.False(restored.Values.ContainsKey(new PortRef("producer", "maybe")));
        var resumed = new FlowExecutionState(restored, state.RuntimeOutputs);

        var result = await FlowExecutionKernel.AdvanceAsync(flow, resumed, services, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.True(result.Result.Outputs["consumer"]["observed"].Value.GetBoolean());
    }

    [Fact]
    public async Task MissingOptionalOutputFailsAtRequiredReceiverWithSourceContext()
    {
        var producer = Producer("producer", new Dictionary<string, JsonElement>(), new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: true), _ => JsonSerializer.SerializeToElement(true));
        var flow = TypedFlowCompiler.Compile(Graph("producer", "maybe", "maybe"), "entry", [Entry, producer, consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Required input 'maybe'", result.Steps[^1].Error);
        Assert.Contains("producer", result.Steps[^1].Error);
    }

    [Fact]
    public async Task MissingPureOutputAtRequiredReceiverAttributesFailureToConsumer()
    {
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: true), _ => JsonSerializer.SerializeToElement(true));
        var flow = TypedFlowCompiler.Compile(PureGraph(), "entry", [Entry, PureMissingOutput(), consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("consumer", result.Steps[^1].NodeId);
        Assert.Contains("Required input 'maybe'", result.Steps[^1].Error);
    }

    [Fact]
    public async Task OptionalPureAbsenceThenConsumerExceptionAttributesFailureToConsumer()
    {
        var consumer = new FlowNodeHandler(new NodeDefinition("consumer", 1, FlowEffect.Pure,
            dataInputs: [new("maybe", new StringType(), Required: false)],
            dataOutputs: [new("observed", new BooleanType(), Required: true)],
            controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, _, _) => throw new InvalidDataException("consumer rejected absent data"));
        var flow = TypedFlowCompiler.Compile(PureGraph(), "entry", [Entry, PureMissingOutput(), consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("consumer", result.Steps[^1].NodeId);
        Assert.Contains("consumer rejected absent data", result.Steps[^1].Error);
    }

    [Fact]
    public async Task ExplicitNullOutputIsPresentAndAcceptedOnlyByNullableReceiver()
    {
        var nullableNumber = new NullableType(new NumberType());
        var producer = Producer("producer", new Dictionary<string, JsonElement>
        {
            ["maybe"] = JsonSerializer.SerializeToElement<object?>(null)
        }, new FlowDataPortDefinition("maybe", nullableNumber, Required: false));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", nullableNumber, Required: false), inputs =>
            JsonSerializer.SerializeToElement(inputs.ContainsKey("maybe") && inputs["maybe"].Value.ValueKind == JsonValueKind.Null));
        var flow = TypedFlowCompiler.Compile(Graph("producer", "maybe", "maybe"), "entry", [Entry, producer, consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Outputs["consumer"]["observed"].Value.GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.Outputs["producer"]["maybe"].Value.ValueKind);
    }

    [Fact]
    public async Task MissingRequiredProducerOutputFailsClearly()
    {
        var producer = Producer("producer", new Dictionary<string, JsonElement>(), new FlowDataPortDefinition("required", new StringType(), Required: true));
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "producer", kind = "producer" } },
            edges = Array.Empty<object>(),
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "producer", targetPort = "exec" } }
        }), "entry", [Entry, producer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Required host output is missing: required", result.Steps[^1].Error);
    }

    [Fact]
    public async Task UndeclaredOutputRemainsInvalidAndUnusedOptionalOutputMayBeAbsent()
    {
        var undeclared = Producer("producer", new Dictionary<string, JsonElement>
        {
            ["stray"] = JsonSerializer.SerializeToElement("x")
        }, new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "producer", kind = "producer" } },
            edges = Array.Empty<object>(),
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "producer", targetPort = "exec" } }
        });
        var failed = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "entry", [Entry, undeclared]), TestContext.Current.CancellationToken);
        Assert.False(failed.Success);
        Assert.Contains("not declared: stray", failed.Steps[^1].Error);

        var absent = Producer("producer", new Dictionary<string, JsonElement>(), new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var succeeded = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "entry", [Entry, absent]), TestContext.Current.CancellationToken);
        Assert.True(succeeded.Success);
        Assert.Empty(succeeded.Outputs["producer"]);
    }

    [Fact]
    public async Task PureNodeWithEmptyOutputSetIsCachedForConsumerActivation()
    {
        var evaluations = 0;
        var pure = new FlowNodeHandler(new NodeDefinition("optionalPure", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType(), Required: true)],
            dataOutputs: [new("maybe", new StringType(), Required: false)]) { ExecutionMode = FlowNodeExecutionMode.Pure },
            (_, _, _) => { evaluations++; return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()); });
        var consumer = new FlowNodeHandler(new NodeDefinition("consumer", 1, FlowEffect.Pure,
            dataInputs: [new("a", new StringType(), Required: false), new("b", new StringType(), Required: false)],
            dataOutputs: [new("observed", new BooleanType(), Required: true)],
            controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
            {
                ["observed"] = JsonSerializer.SerializeToElement(!inputs.ContainsKey("a") && !inputs.ContainsKey("b"))
            }));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "entry" }, new { id = "pure", kind = "optionalPure" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] {
                new { source = "entry", sourcePort = "value", target = "pure", targetPort = "value" },
                new { source = "pure", sourcePort = "maybe", target = "consumer", targetPort = "a" },
                new { source = "pure", sourcePort = "maybe", target = "consumer", targetPort = "b" }
            },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var flow = TypedFlowCompiler.Compile(graph, "entry", [Entry, pure, consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, evaluations);
        Assert.True(result.Outputs["consumer"]["observed"].Value.GetBoolean());
    }

    [Fact]
    public async Task RegisteredFunctionCallUsesTheSameOptionalOutputContract()
    {
        var registry = new FlowFunctionRegistry();
        registry.Register(new FlowFunctionDefinition("optional", "Optional",
            [], [new FlowDataPortDefinition("maybe", new StringType(), Required: false)],
            FlowNodeExecutionMode.Action, FlowEffect.Pure), (_, _) =>
                Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: false), inputs =>
            JsonSerializer.SerializeToElement(!inputs.ContainsKey("maybe")));
        var flow = TypedFlowCompiler.Compile(Graph("call-optional", "maybe", "maybe"), "entry",
            [Entry, registry.CreateCallHandler("optional", "call-optional"), consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Outputs["consumer"]["observed"].Value.GetBoolean());
    }

    [Fact]
    public async Task EntryMayOmitItsOptionalNamedOutput()
    {
        var optionalEntry = new FlowNodeHandler(new NodeDefinition("optionalEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("maybe", new StringType(), Required: false)], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()), isEntry: true);
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: false), inputs =>
            JsonSerializer.SerializeToElement(!inputs.ContainsKey("maybe")));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "optionalEntry" }, new { id = "consumer", kind = "consumer" } },
            edges = new[] { new { source = "entry", sourcePort = "maybe", target = "consumer", targetPort = "maybe" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var flow = TypedFlowCompiler.Compile(graph, "entry", [optionalEntry, consumer]);

        var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Outputs["consumer"]["observed"].Value.GetBoolean());
    }

    [Fact]
    public void MissingOptionalOutputDoesNotHideAProducerThatMayNotHaveRun()
    {
        var choiceType = new EnumType(["go", "stop"]);
        var entry = new FlowNodeHandler(new NodeDefinition("choiceEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("choice", choiceType)], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["choice"] = JsonSerializer.SerializeToElement("go") }), isEntry: true);
        var producer = Producer("producer", new Dictionary<string, JsonElement>(), new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: false), _ => JsonSerializer.SerializeToElement(true));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[] {
                new { id = "entry", kind = "choiceEntry" },
                new { id = "switch", kind = "switch", settings = new { cases = new[] { new { id = "chosen", value = "go" }, new { id = "skipped", value = "stop" } }, other = false } },
                new { id = "producer", kind = "producer" }, new { id = "consumer", kind = "consumer" }
            },
            edges = new[] {
                new { source = "entry", sourcePort = "choice", target = "switch", targetPort = "value" },
                new { source = "producer", sourcePort = "maybe", target = "consumer", targetPort = "maybe" }
            },
            controlEdges = new[] {
                new { source = "entry", sourcePort = "then", target = "switch", targetPort = "exec" },
                new { source = "switch", sourcePort = "chosen", target = "producer", targetPort = "exec" },
                new { source = "producer", sourcePort = "then", target = "consumer", targetPort = "exec" },
                new { source = "switch", sourcePort = "skipped", target = "consumer", targetPort = "exec" }
            }
        });

        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [entry, producer, consumer]));

        Assert.Contains(error.Issues, issue => issue.NodeId == "consumer" && issue.Reason.Contains("Produtor não executa em todos os caminhos anteriores: producer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StepOutputEditCanRemoveOptionalValueWithoutLeavingStaleState()
    {
        var producer = Producer("producer", new Dictionary<string, JsonElement>
        {
            ["maybe"] = JsonSerializer.SerializeToElement("draft")
        }, new FlowDataPortDefinition("maybe", new StringType(), Required: false));
        var consumer = Consumer(new FlowDataPortDefinition("maybe", new StringType(), Required: false), inputs =>
            JsonSerializer.SerializeToElement(!inputs.ContainsKey("maybe")));
        var flow = TypedFlowCompiler.Compile(Graph("producer", "maybe", "maybe"), "entry", [Entry, producer, consumer]);
        var execution = new TypedFlowExecution(flow);
        await execution.AdvanceAsync(TestContext.Current.CancellationToken);
        await execution.AdvanceAsync(TestContext.Current.CancellationToken);

        execution.AmendLastOutputs(new Dictionary<string, JsonElement>());
        var result = await execution.AdvanceAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.Outputs["consumer"]["observed"].Value.GetBoolean());
    }
}
