using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class TypedFunctionCallTests
{
    private static readonly ObjectType PayloadType = new(new Dictionary<string, FlowField>
    {
        ["name"] = new(new StringType(), true),
        ["items"] = new(new ArrayType(new IntegerType()), true),
        ["note"] = new(new NullableType(new StringType()), false),
    });
    private static readonly ObjectType ResultType = new(new Dictionary<string, FlowField>
    {
        ["accepted"] = new(new ArrayType(new IntegerType()), true),
        ["details"] = new(new ObjectType(new Dictionary<string, FlowField> { ["ok"] = new(new BooleanType(), true) }), true),
    });

    [Fact]
    public void DescriptionUsesDefinitionsWithoutInvokingRuntimeDelegate()
    {
        var definition = Function("route", FlowNodeExecutionMode.Action, FlowEffect.Model);
        var invoked = false;
        var registry = new FlowFunctionRegistry();
        registry.RegisterDefinition(definition);
        var described = Assert.Single(registry.Describe());
        registry.RegisterInvoker("route", (_, _) =>
        {
            invoked = true;
            throw new InvalidOperationException("Runtime invocation must not be used for description.");
        });

        Assert.Same(definition, described);
        Assert.Equal("route", described.Id);
        Assert.Equal("Roteamento", described.DisplayName);
        Assert.False(invoked);

        var metadataOnly = new FlowFunctionRegistry();
        metadataOnly.RegisterDefinition(definition);
        Assert.Single(metadataOnly.Describe());
        Assert.Throws<InvalidOperationException>(() => metadataOnly.CreateCallHandler("route", "call-route"));
    }

    [Fact]
    public async Task RegisteredCallCarriesNestedJsonValuesAndExecutesOnceOnChosenPath()
    {
        var invoked = 0;
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("route", FlowNodeExecutionMode.Action, FlowEffect.Model, ResultType),
            (inputs, _) =>
            {
                invoked++;
                Assert.Equal("""{"name":"task","items":[1,2],"note":null}""", inputs["payload"].Value.GetRawText());
                IReadOnlyDictionary<string, JsonElement> result = new Dictionary<string, JsonElement>
                {
                    ["result"] = Json("""{"accepted":[3,4],"details":{"ok":true}}""")
                };
                return Task.FromResult(result);
            });
        var handler = registry.CreateCallHandler("route", "call-route");
        var graph = Graph(
            [Node("start", "start"), Node("call", "call-route")],
            [Data("start", "payload", "call", "payload")],
            [Control("start", "then", "call", "exec")]);

        var flow = Compile(graph, "start", [Start(PayloadType), handler]);
        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success, string.Join(" | ", result.Steps.Select(step => step.Error).Where(error => error is not null)));
        Assert.Equal(1, invoked);
        Assert.Equal("""{"accepted":[3,4],"details":{"ok":true}}""", result.Outputs["call"]["result"].Value.GetRawText());
        Assert.Equal(new[] { "start", "call" }, result.Steps.Select(step => step.NodeId));
    }

    [Fact]
    public async Task RegisteredCallWiresNativeDictionaryValuesWithoutFlatteningKeys()
    {
        var type = new DictionaryType(new IntegerType());
        var payload = Json("""{"Model A":8192,"gpt-6.1-sol":4096}""");
        var entry = new FlowNodeHandler(new NodeDefinition("dictionaryEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("budget", type)], controlOutputs: [new("then")])
        { ExecutionMode = FlowNodeExecutionMode.Entry }, (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
            new Dictionary<string, JsonElement> { ["budget"] = payload }));
        var registry = new FlowFunctionRegistry();
        registry.Register(new FlowFunctionDefinition("budget", "Budget", [new("budget", type, Required: true)],
            [new("result", type)], FlowNodeExecutionMode.Action, FlowEffect.Pure),
            (inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["result"] = inputs["budget"].Value.Clone() }));
        var graph = Graph([Node("start", "dictionaryEntry"), Node("call", "call-budget")],
            [Data("start", "budget", "call", "budget")], [Control("start", "then", "call", "exec")]);

        var flow = Compile(graph, "start", [entry, registry.CreateCallHandler("budget", "call-budget")]);
        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success, string.Join(" | ", result.Steps.Select(step => step.Error).Where(error => error is not null)));
        Assert.Equal(payload.GetRawText(), result.Outputs["call"]["result"].Value.GetRawText());
    }

    [Fact]
    public async Task DictionaryConstantUsesTheSameRecursiveTypeContractAsFunctionInputs()
    {
        var type = new DictionaryType(new IntegerType());
        var payload = Json("""{"Model A":8192,"gpt-6.1-sol":4096}""");
        var entry = new FlowNodeHandler(new NodeDefinition("start", 1, FlowEffect.Pure,
            controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry },
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        var registry = new FlowFunctionRegistry();
        registry.Register(new FlowFunctionDefinition("budget", "Budget", [new("budget", type, Required: true)],
            [new("result", type)], FlowNodeExecutionMode.Action, FlowEffect.Pure),
            (inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["result"] = inputs["budget"].Value.Clone() }));
        var call = registry.CreateCallHandler("budget", "call-budget");
        var graph = Graph([Node("start", "start"), Node("call", "call-budget",
                new { inputValues = new Dictionary<string, JsonElement> { ["budget"] = payload } })],
            [], [Control("start", "then", "call", "exec")]);

        var result = await TypedFlowRunner.RunAsync(Compile(graph, "start", [entry, call]), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(payload.GetRawText(), result.Outputs["call"]["result"].Value.GetRawText());

        var invalidGraph = Graph([Node("start", "start"), Node("call", "call-budget",
                new { inputValues = new { budget = "not a dictionary" } })], [],
            [Control("start", "then", "call", "exec")]);
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(invalidGraph, "start", [entry, call]));
    }

    [Fact]
    public async Task JsonFunctionWiringAndConfiguredConstantsPreserveNativeValues()
    {
        var type = new JsonType();
        var payload = Json("""{"mensagem":7,"unknown":[null,{"StableId":"person-1"}]}""");
        var entry = new FlowNodeHandler(new NodeDefinition("jsonEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("payload", type)], controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry },
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["payload"] = payload }));
        var calls = 0;
        var registry = new FlowFunctionRegistry();
        registry.Register(new FlowFunctionDefinition("raw-json", "Raw JSON", [new("payload", type, Required: true)],
            [new("result", type, Required: true)], FlowNodeExecutionMode.Action, FlowEffect.Pure), (inputs, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["result"] = inputs["payload"].Value.Clone() });
        });
        var call = registry.CreateCallHandler("raw-json", "call-json");
        var wired = Graph([Node("start", "jsonEntry"), Node("call", "call-json")],
            [Data("start", "payload", "call", "payload")], [Control("start", "then", "call", "exec")]);
        var wiredResult = await TypedFlowRunner.RunAsync(Compile(wired, "start", [entry, call]), CancellationToken.None);
        Assert.True(wiredResult.Success);
        Assert.True(JsonElement.DeepEquals(payload, wiredResult.Outputs["call"]["result"].Value));

        var configured = Graph([Node("start", "start"), Node("call", "call-json",
                new { inputValues = new Dictionary<string, JsonElement> { ["payload"] = payload } })], [],
            [Control("start", "then", "call", "exec")]);
        var start = Start(PayloadType);
        var configuredResult = await TypedFlowRunner.RunAsync(Compile(configured, "start", [start, call]), CancellationToken.None);
        Assert.True(configuredResult.Success);
        Assert.True(JsonElement.DeepEquals(payload, configuredResult.Outputs["call"]["result"].Value));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PureJsonFunctionCarriesOpaqueValuesToItsConsumer()
    {
        var type = new JsonType();
        var payload = Json("""{"mensagem":7,"StableId":"ref-looking","nested":[null,true]}""");
        var entry = new FlowNodeHandler(new NodeDefinition("jsonEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("payload", type)], controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry },
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["payload"] = payload }));
        var registry = new FlowFunctionRegistry();
        var invocations = 0;
        registry.Register(new FlowFunctionDefinition("pure-json", "Copy JSON", [new("payload", type, Required: true)],
            [new("result", type, Required: true)], FlowNodeExecutionMode.Pure, FlowEffect.Pure), (inputs, _) =>
        {
            invocations++;
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["result"] = inputs["payload"].Value.Clone() });
        });
        var consumer = new FlowNodeHandler(new NodeDefinition("jsonSink", 1, FlowEffect.Pure,
            dataInputs: [new("result", type, Required: true)], controlInputs: [new("exec")]), (_, inputs, _) =>
        {
            Assert.True(JsonElement.DeepEquals(payload, inputs["result"].Value));
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>());
        });
        var graph = Graph([Node("start", "jsonEntry"), Node("call", "call-json"), Node("sink", "jsonSink")],
            [Data("start", "payload", "call", "payload"), Data("call", "result", "sink", "result")],
            [Control("start", "then", "sink", "exec")]);

        var result = await TypedFlowRunner.RunAsync(Compile(graph, "start",
            [entry, registry.CreateCallHandler("pure-json", "call-json"), consumer]), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task RegisteredCallRunsOnceOnlyOnTheSelectedControlBranch()
    {
        var calls = 0;
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("route", FlowNodeExecutionMode.Action, FlowEffect.Model, ResultType), (_, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
            {
                ["result"] = Json("""{"accepted":[],"details":{"ok":true}}""")
            });
        });
        var graph = Graph(
            [Node("start", "start"), Node("switch", "switch", new { cases = new[] { new { id = "chosen", value = "go" }, new { id = "skipped", value = "stop" } }, other = false }),
             Node("chosen-call", "call-chosen"), Node("skipped-call", "call-skipped")],
            [Data("start", "choice", "switch", "value"), Data("start", "payload", "chosen-call", "payload"), Data("start", "payload", "skipped-call", "payload")],
            [Control("start", "then", "switch", "exec"), Control("switch", "chosen", "chosen-call", "exec"), Control("switch", "skipped", "skipped-call", "exec")]);
        var flow = Compile(graph, "start", [Start(PayloadType, choice: "go"),
            registry.CreateCallHandler("route", "call-chosen"), registry.CreateCallHandler("route", "call-skipped")]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, calls);
        Assert.Contains(result.Steps, step => step.NodeId == "chosen-call");
        Assert.DoesNotContain(result.Steps, step => step.NodeId == "skipped-call");
    }

    [Fact]
    public void UnknownCallKindFailsCompilationBeforeAnyFunctionCanRun()
    {
        var calls = 0;
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("route", FlowNodeExecutionMode.Action, FlowEffect.WriteState),
            (_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>());
            });
        var graph = Graph([Node("start", "start"), Node("call", "unregistered-call")], [],
            [Control("start", "then", "call", "exec")]);

        Assert.Throws<TypedFlowException>(() =>
            TypedFlowCompiler.Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("route", "call-route")]));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CallHandlerKeepsRegisteredModeAndEffect()
    {
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("pure-transform", FlowNodeExecutionMode.Pure, FlowEffect.Pure), (_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        registry.Register(Function("action-pure-effect", FlowNodeExecutionMode.Action, FlowEffect.Pure), (_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        registry.Register(Function("write", FlowNodeExecutionMode.Action, FlowEffect.WriteState), (_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));

        Assert.True(registry.CreateCallHandler("pure-transform", "call-pure").IsPure);
        var action = registry.CreateCallHandler("action-pure-effect", "call-action");
        Assert.False(action.IsPure);
        Assert.Equal(new[] { "exec" }, action.Definition.ControlInputs.Select(port => port.Name));
        Assert.Equal(new[] { "then" }, action.Definition.ControlOutputs.Select(port => port.Name));
        Assert.Equal(FlowEffect.WriteState, registry.CreateCallHandler("write", "call-write").Definition.Effect);
    }

    [Fact]
    public void PureCallMetadataCanBeDescribedWithoutBindingAndNavigationIsPreserved()
    {
        var registry = new FlowFunctionRegistry();
        registry.RegisterDefinition(new FlowFunctionDefinition("pure", "Somar",
            [new FlowDataPortDefinition("value", new NumberType(), Required: true)],
            [new FlowDataPortDefinition("result", new NumberType(), Required: true)],
            FlowNodeExecutionMode.Pure, FlowEffect.Pure, "graph://sum"));

        var description = registry.DescribeCall("pure", "call-pure");

        Assert.Equal("Chamar Somar", description.DisplayName);
        Assert.Equal(FlowNodeExecutionMode.Pure, description.ExecutionMode);
        Assert.Equal("graph://sum", description.NavigableGraphReference);
        Assert.Single(description.DataInputs);
        Assert.Single(description.DataOutputs);
        Assert.Empty(description.ControlInputs);
        Assert.Empty(description.ControlOutputs);
    }

    [Fact]
    public async Task PureRegisteredCallEvaluatesOncePerConsumingOperationAndCachesBothOutputs()
    {
        var invocations = 0;
        var definition = new FlowFunctionDefinition("pair", "Pair",
            [new FlowDataPortDefinition("payload", PayloadType, Required: true)],
            [new FlowDataPortDefinition("left", new NumberType(), Required: true), new FlowDataPortDefinition("right", new NumberType(), Required: true)],
            FlowNodeExecutionMode.Pure, FlowEffect.Pure);
        var registry = new FlowFunctionRegistry();
        registry.Register(definition, (inputs, _) =>
        {
            invocations++;
            var source = inputs["payload"].Value.GetProperty("items").GetArrayLength();
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
            {
                ["left"] = JsonSerializer.SerializeToElement(source), ["right"] = JsonSerializer.SerializeToElement(source + 1)
            });
        });
        var graph = Graph(
            [Node("start", "start"), Node("call", "call-pair"), Node("first", "sink"), Node("second", "sink")],
            [Data("start", "payload", "call", "payload"), Data("call", "left", "first", "left"), Data("call", "right", "first", "right"),
                Data("call", "left", "second", "left"), Data("call", "right", "second", "right")],
            [Control("start", "then", "first", "exec"), Control("first", "then", "second", "exec")]);
        var flow = Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("pair", "call-pair"), PairSink()]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, invocations);
        Assert.Equal(5, result.Outputs["first"]["sum"].Value.GetInt32());
        Assert.Equal(5, result.Outputs["second"]["sum"].Value.GetInt32());
    }

    [Fact]
    public void PureFunctionCannotDeclareAnOperationalEffect()
    {
        var registry = new FlowFunctionRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(
            Function("bad", FlowNodeExecutionMode.Pure, FlowEffect.Model),
            (_, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>())));
    }

    [Fact]
    public async Task ModelAndWriteCallsRetainTheirKernelStepClassification()
    {
        foreach (var (id, effect, expected) in new[]
        {
            ("model", FlowEffect.Model, StepEffect.Inference),
            ("write", FlowEffect.WriteState, StepEffect.Artifact),
        })
        {
            var registry = new FlowFunctionRegistry();
            registry.Register(Function(id, FlowNodeExecutionMode.Action, effect), (_, _) =>
                Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["result"] = Json("""{"ok":true}""") }));
            var graph = Graph([Node("start", "start"), Node("call", "call-" + id)], [Data("start", "payload", "call", "payload")],
                [Control("start", "then", "call", "exec")]);
            var flow = Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler(id, "call-" + id)]);
            var state = FlowExecutionKernel.Start(flow, Json("{}"), Durable.IncrementalKernelTests.Identity(), null);
            var step = await FlowExecutionKernel.AdvanceAsync(flow, state, new FlowExecutionServices(new NoModelCompletion()), TestContext.Current.CancellationToken);

            Assert.Equal(expected, step.NextEffect);
            Assert.Equal(ExecutionStatus.Ready, step.Status);
            Assert.Equal("call", step.NextNodeId);
        }
    }

    [Fact]
    public async Task RequiredDeclaredInputAcceptsValidConfiguredConstantWithoutWire()
    {
        var registry = RegistryWithCall();
        var graph = Graph([Node("start", "start"), Node("call", "call-route", new { inputValues = new { payload = new { name = "n", items = new[] { 1, 2 }, note = (string?)null } } })],
            [], [Control("start", "then", "call", "exec")]);

        var flow = Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("route", "call-route")]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("""{"name":"n","items":[1,2],"note":null}""", result.Outputs["call"]["result"].Value.GetRawText());
        Assert.Equal(new[] { "payload" }, flow.Nodes["call"].Inputs.Select(port => port.Id));
        Assert.Equal(PayloadType, flow.Nodes["call"].Inputs[0].Type);
    }

    [Theory]
    [InlineData("""{"inputValues":{"payload":"wrong"}}""")]
    [InlineData("""{"inputValues":{"payload":null}}""")]
    [InlineData("""{"inputValues":{"undeclared":1}}""")]
    [InlineData("""{"inputValues":[]}""")]
    public void InvalidConstantShapeOrTypeFailsCompilation(string settingsJson)
    {
        var registry = RegistryWithCall();
        var graph = Graph([Node("start", "start"), Node("call", "call-route", Json(settingsJson))], [],
            [Control("start", "then", "call", "exec")]);

        Assert.Throws<TypedFlowException>(() =>
            TypedFlowCompiler.Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("route", "call-route")]));
    }

    [Fact]
    public void WireAndConfiguredValueForSameFunctionInputAreRejected()
    {
        var registry = RegistryWithCall();
        var graph = Graph([Node("start", "start"), Node("call", "call-route", new { inputValues = new { payload = new { name = "n", items = new[] { 1 }, note = (string?)null } } })],
            [Data("start", "payload", "call", "payload")], [Control("start", "then", "call", "exec")]);

        var error = Assert.Throws<TypedFlowException>(() =>
            TypedFlowCompiler.Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("route", "call-route")]));

        Assert.Contains(error.Issues, issue => issue.NodeId == "call" && issue.Port == "payload");
    }

    [Fact]
    public async Task MissingOptionalCallInputRemainsAbsentFromInvocation()
    {
        var optionalDefinition = new FlowFunctionDefinition("optional", "Optional",
            [new FlowDataPortDefinition("query", new StringType())],
            [new FlowDataPortDefinition("result", new BooleanType(), Required: true)],
            FlowNodeExecutionMode.Action, FlowEffect.Pure);
        IReadOnlyDictionary<string, TypedValue>? seen = null;
        var registry = new FlowFunctionRegistry();
        registry.Register(optionalDefinition, (inputs, _) =>
        {
            seen = inputs;
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(inputs.Count == 0) });
        });
        var graph = Graph([Node("start", "start"), Node("call", "call-optional")], [], [Control("start", "then", "call", "exec")]);
        var flow = Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("optional", "call-optional")]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(seen!);
        Assert.Equal(JsonValueKind.True, result.Outputs["call"]["result"].Value.ValueKind);
    }

    [Fact]
    public async Task OptionalConfiguredCallInputIsPassedWithoutWire()
    {
        var definition = new FlowFunctionDefinition("optional", "Optional",
            [new FlowDataPortDefinition("query", new StringType())],
            [new FlowDataPortDefinition("result", new StringType(), Required: true)],
            FlowNodeExecutionMode.Action, FlowEffect.Pure);
        var registry = new FlowFunctionRegistry();
        registry.Register(definition, (inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
            new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(inputs["query"].Value.GetString()) }));
        var graph = Graph([Node("start", "start"), Node("call", "call-optional", new { inputValues = new { query = "configured" } })],
            [], [Control("start", "then", "call", "exec")]);
        var flow = Compile(graph, "start", [Start(PayloadType), registry.CreateCallHandler("optional", "call-optional")]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("configured", result.Outputs["call"]["result"].Value.GetString());
    }

    [Fact]
    public async Task CallPropagatesFailureAndCancellation()
    {
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("failure", FlowNodeExecutionMode.Action, FlowEffect.Model), (_, _) => throw new IOException("host unavailable"));
        var flow = Compile(Graph([Node("start", "start"), Node("call", "call-failure")],
            [Data("start", "payload", "call", "payload")], [Control("start", "then", "call", "exec")]),
            "start", [Start(PayloadType), registry.CreateCallHandler("failure", "call-failure")]);
        await Assert.ThrowsAsync<IOException>(() => TypedFlowRunner.RunAsync(flow, CancellationToken.None));

        var cancelledRegistry = new FlowFunctionRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancelledRegistry = new FlowFunctionRegistry();
        cancelledRegistry.Register(Function("cancel", FlowNodeExecutionMode.Action, FlowEffect.Model), async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new Dictionary<string, JsonElement>();
        });
        var cancelledFlow = Compile(Graph([Node("start", "start"), Node("call", "call-cancel")],
            [Data("start", "payload", "call", "payload")], [Control("start", "then", "call", "exec")]),
            "start", [Start(PayloadType), cancelledRegistry.CreateCallHandler("cancel", "call-cancel")]);
        using var cancellation = new CancellationTokenSource();
        var running = TypedFlowRunner.RunAsync(cancelledFlow, cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task MakeStructOmitsMissingOptionalFieldsAndAcceptsConfiguredInputs()
    {
        var schema = new ObjectType(new Dictionary<string, FlowField>
        {
            ["name"] = new(new StringType(), true),
            ["note"] = new(new NullableType(new StringType()), false),
            ["numbers"] = new(new ArrayType(new IntegerType()), true),
        });
        var make = FlowStructFactory.CreateMakeHandler("make-payload", schema);
        var sink = Sink(schema);
        var graph = Graph(
            [Node("start", "start"), Node("make", "make-payload", new { inputValues = new { name = "Ada", numbers = new[] { 2, 3 } } }), Node("sink", "sink")],
            [Data("make", "value", "sink", "payload")],
            [Control("start", "then", "sink", "exec")]);
        var flow = Compile(graph, "start", [Start(PayloadType), make, sink]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("""{"name":"Ada","numbers":[2,3]}""", result.Outputs["sink"]["result"].Value.GetRawText());
    }

    [Fact]
    public async Task BreakStructExtractsSelectedArrayAndNullableFieldsIncludingPresentNull()
    {
        var schema = new ObjectType(new Dictionary<string, FlowField>
        {
            ["numbers"] = new(new ArrayType(new IntegerType()), true),
            ["note"] = new(new NullableType(new StringType()), false),
            ["unused"] = new(new StringType(), false),
        });
        var source = Start(schema, """{"numbers":[5,8],"note":null}""");
        var split = FlowStructFactory.CreateBreakHandler("break-payload", schema, ["numbers", "note"]);
        var sink = Sink(new ArrayType(new IntegerType()), "numbers");
        var graph = Graph([Node("start", "start"), Node("break", "break-payload"), Node("sink", "sink")],
            [Data("start", "payload", "break", "value"), Data("break", "numbers", "sink", "numbers")],
            [Control("start", "then", "sink", "exec")]);
        var flow = Compile(graph, "start", [source, split, sink]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[5,8]", result.Outputs["sink"]["result"].Value.GetRawText());
    }

    [Fact]
    public async Task BreakStructFailsAtRuntimeForSelectedAbsentOptionalField()
    {
        var schema = new ObjectType(new Dictionary<string, FlowField>
        {
            ["required"] = new(new StringType(), true),
            ["optional"] = new(new StringType(), false),
        });
        var source = Start(schema, """{"required":"present"}""");
        var split = FlowStructFactory.CreateBreakHandler("break-payload", schema, ["optional"]);
        var sink = Sink(new StringType(), "optional");
        var graph = Graph([Node("start", "start"), Node("break", "break-payload"), Node("sink", "sink")],
            [Data("start", "payload", "break", "value"), Data("break", "optional", "sink", "optional")],
            [Control("start", "then", "sink", "exec")]);
        var flow = Compile(graph, "start", [source, split, sink]);

        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("optional", result.Steps.Last().Error ?? string.Empty);
    }

    [Fact]
    public void BreakStructRejectsUnknownFieldAndCompilerRejectsIncompatibleMakeToBreakWire()
    {
        var schema = new ObjectType(new Dictionary<string, FlowField> { ["number"] = new(new NumberType(), true) });
        Assert.Throws<ArgumentException>(() => FlowStructFactory.CreateBreakHandler("break", schema, ["missing"]));

        var makeSchema = new ObjectType(new Dictionary<string, FlowField> { ["number"] = new(new StringType(), true) });
        var make = FlowStructFactory.CreateMakeHandler("make", makeSchema);
        var split = FlowStructFactory.CreateBreakHandler("break", schema, ["number"]);
        var sink = Sink(new NumberType(), "number");
        var graph = Graph(
            [Node("start", "start"), Node("make", "make", new { inputValues = new { number = "not-a-number" } }), Node("break", "break"), Node("sink", "sink")],
            [Data("make", "value", "break", "value"), Data("break", "number", "sink", "number")],
            [Control("start", "then", "sink", "exec")]);
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "start", [Start(PayloadType), make, split, sink]));
    }

    private static FlowFunctionDefinition Function(string id, FlowNodeExecutionMode mode, FlowEffect effect, FlowType? returnType = null) =>
        new(id, id == "route" ? "Roteamento" : id,
            [new FlowDataPortDefinition("payload", PayloadType, Required: true)],
            [new FlowDataPortDefinition("result", returnType ?? PayloadType, Required: true)],
            mode, effect, "graph://" + id);

    private static TypedFlow Compile(JsonElement graph, string entryId, IEnumerable<FlowNodeHandler> handlers)
    {
        try { return TypedFlowCompiler.Compile(graph, entryId, handlers); }
        catch (TypedFlowException exception)
        {
            throw new InvalidOperationException(string.Join(" | ", exception.Issues.Select(issue => $"{issue.NodeId}.{issue.Port}: {issue.Reason}")), exception);
        }
    }

    private static FlowFunctionRegistry RegistryWithCall()
    {
        var registry = new FlowFunctionRegistry();
        registry.Register(Function("route", FlowNodeExecutionMode.Action, FlowEffect.Pure),
            (inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["result"] = inputs["payload"].Value.Clone() }));
        return registry;
    }

    private static FlowNodeHandler Start(ObjectType type, string? value = null, string? choice = null)
    {
        var payload = value is null
            ? Json("""{"name":"task","items":[1,2],"note":null}""")
            : Json(value);
        var outputs = new List<FlowDataPortDefinition> { new("payload", type) };
        if (choice is not null) outputs.Add(new("choice", new StringType()));
        var definition = new NodeDefinition("start", 1, FlowEffect.Pure,
            dataOutputs: outputs,
            controlOutputs: [new FlowControlPortDefinition("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry };
        return new(definition, (_, _, _) =>
        {
            var values = new Dictionary<string, JsonElement> { ["payload"] = payload };
            if (choice is not null) values["choice"] = JsonSerializer.SerializeToElement(choice);
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(values);
        });
    }

    private static FlowNodeHandler Sink(FlowType type, string input = "payload") =>
        new(new NodeDefinition("sink", 1, FlowEffect.Pure,
            dataInputs: [new FlowDataPortDefinition(input, type, Required: true)],
            dataOutputs: [new FlowDataPortDefinition("result", type)],
            controlInputs: [new FlowControlPortDefinition("exec")]),
            (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["result"] = inputs[input].Value.Clone() }));

    private static FlowNodeHandler PairSink() => new(new NodeDefinition("sink", 1, FlowEffect.Pure,
        dataInputs: [new("left", new NumberType(), Required: true), new("right", new NumberType(), Required: true)],
        dataOutputs: [new("sum", new NumberType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
        (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
        { ["sum"] = JsonSerializer.SerializeToElement(inputs["left"].Value.GetInt32() + inputs["right"].Value.GetInt32()) }));

    private static JsonElement Graph(IEnumerable<object> nodes, IEnumerable<object> edges, IEnumerable<object> controlEdges) =>
        JsonSerializer.SerializeToElement(new { version = 4, nodes, edges, controlEdges });

    private static object Node(string id, string kind, object? settings = null) =>
        settings is null ? new { id, kind } : new { id, kind, settings };

    private static object Data(string source, string sourcePort, string target, string targetPort) =>
        new { source, sourcePort, target, targetPort };

    private static object Control(string source, string sourcePort, string target, string targetPort) =>
        new { source, sourcePort, target, targetPort };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class NoModelCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            Task.FromException<string>(new InvalidOperationException("No model should be invoked."));
    }
}
