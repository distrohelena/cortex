using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class BlueprintDescriptorTests
{
    [Fact]
    public void DescribesConfiguredHostPortsWithoutBindingAHandlerAndPreservesNestedSchema()
    {
        var schema = new ObjectType(new Dictionary<string, FlowField>
        {
            ["items"] = new(new ArrayType(new NumberType()), Required: true),
            ["note"] = new(new NullableType(new StringType()), Required: false)
        });
        var entry = new NodeDefinition("fixtureEntry", 1, FlowEffect.Pure, controlOutputs: [new("then")])
        {
            DisplayName = "Start",
            ExecutionMode = FlowNodeExecutionMode.Entry
        };
        var configured = new NodeDefinition("configured", 1, FlowEffect.Pure,
            configFields: [new("shape", schema, Required: true)],
            resolver: _ => new NodeDefinitionResolution(
                [new("input", schema, Required: true)], [new("output", schema)], [], [], []))
        {
            DisplayName = "Configured operation",
            ExecutionMode = FlowNodeExecutionMode.Pure
        };
        var graph = Graph(
            new { id = "entry", kind = "fixtureEntry", settings = new { } },
            new { id = "configured", kind = "configured", settings = new { shape = new { kind = "object" } } });

        var descriptions = TypedFlowCompiler.Describe(graph, [entry, configured]);
        var description = descriptions["configured"];
        Assert.Equal("Configured operation", description.DisplayName);
        Assert.Equal(FlowNodeExecutionMode.Pure, description.ExecutionMode);
        var input = Assert.Single(description.Inputs);
        Assert.Equal("input", input.Name);
        Assert.True(input.Required);
        Assert.Equal(schema, input.Type);
        Assert.Equal(new[] { "items", "note" }, Assert.IsType<ObjectType>(input.Type).Fields.Keys);
        Assert.Empty(description.Diagnostics);
    }

    [Fact]
    public void DescribesTypedDictionaryPortsWithTheirValueSchema()
    {
        var dictionary = new DictionaryType(new NullableType(new IntegerType()));
        var entry = new NodeDefinition("budgetEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("budget", dictionary)], controlOutputs: [new("then")])
        { DisplayName = "Budget", ExecutionMode = FlowNodeExecutionMode.Entry };
        var graph = Graph(new { id = "entry", kind = "budgetEntry", settings = new { } },
            new { id = "count", kind = "count", settings = new { } });

        var description = TypedFlowCompiler.Describe(graph, [entry])["entry"];
        var output = Assert.Single(description.Outputs);

        Assert.Equal("budget", output.Name);
        Assert.Equal(dictionary.ToJsonSchema().GetRawText(), output.Type!.ToJsonSchema().GetRawText());
    }

    [Fact]
    public void DescribesDisconnectedTypeDependentBuiltinPortsAsUnresolvedWithDiagnostic()
    {
        var graph = Graph(new { id = "entry", kind = "dm", settings = new { } }, new { id = "count", kind = "count", settings = new { } });

        var description = TypedFlowCompiler.Describe(graph, Array.Empty<NodeDefinition>());
        var count = description["count"];
        Assert.Contains(count.Inputs, port => port.Name == "list" && port.Type is null);
        Assert.Contains(count.Outputs, port => port.Name == "count" && port.Type is NumberType);
        Assert.Contains(count.Diagnostics, issue => issue.Port == "list");
    }

    [Fact]
    public void InvalidBuiltinConfigurationStillExposesAlreadyKnownPorts()
    {
        var graph = Graph(
            new { id = "entry", kind = "dm", settings = new { } },
            new { id = "property", kind = "property", settings = new { } });

        var description = TypedFlowCompiler.Describe(graph, Array.Empty<NodeDefinition>())["property"];
        Assert.Contains(description.Inputs, port => port.Name == "object" && port.Type is null);
        Assert.Contains(description.Outputs, port => port.Name == "value" && port.Type is null);
        Assert.Contains(description.Diagnostics, issue => issue.Port == "config");
    }

    [Fact]
    public void BuiltinFailuresPreserveFixedAndFieldResolvablePorts()
    {
        var countGraph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[] { new { id = "entry", kind = "dm", settings = new { } }, new { id = "count", kind = "count", settings = new { } } },
            edges = new[] { new { source = "entry", sourcePort = "message", target = "count", targetPort = "list" } },
            controlEdges = Array.Empty<object>()
        });
        var count = TypedFlowCompiler.Describe(countGraph, Array.Empty<NodeDefinition>())["count"];
        Assert.Contains(count.Inputs, port => port.Name == "list" && port.Type == FlowDomain.Message);
        Assert.Contains(count.Outputs, port => port.Name == "count" && port.Type is NumberType);
        Assert.NotEmpty(count.Diagnostics);

        var boundaryGraph = Graph(
            new { id = "entry", kind = "dm", settings = new { } },
            new { id = "boundaries", kind = "interpretBoundaries", settings = new { } });
        var boundaries = TypedFlowCompiler.Describe(boundaryGraph, Array.Empty<NodeDefinition>())["boundaries"];
        Assert.Contains(boundaries.Inputs, port => port.Name == "memories" && port.Type == new ArrayType(FlowDomain.Memory));
        Assert.Contains(boundaries.Outputs, port => port.Name == "boundaries" && port.Type == new ArrayType(FlowDomain.Boundary));
        Assert.Contains(boundaries.ControlInputs, port => port.Name == "exec");
        Assert.NotEmpty(boundaries.Diagnostics);

        var promptGraph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "dm", settings = new { } },
                new { id = "prompt", kind = "prompt", prompt = " ", inputs = new[] { new { id = "who", name = "who", type = "text" } }, responseFields = new[] { new { id = "score", name = "score", type = "number", min = 0, max = 10 } } }
            },
            edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
        });
        var prompt = TypedFlowCompiler.Describe(promptGraph, Array.Empty<NodeDefinition>())["prompt"];
        Assert.Contains(prompt.Inputs, port => port.Id == "who" && port.Type is StringType);
        Assert.Contains(prompt.Outputs, port => port.Id == "score" && port.Type is NumberType);
        Assert.NotEmpty(prompt.Diagnostics);
    }

    [Fact]
    public void InvalidConfiguredDefaultsAndPromptFieldsDoNotAbortOtherDescriptions()
    {
        var entry = new NodeDefinition("fixtureEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("seed", new NumberType())], controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry };
        var host = new NodeDefinition("configured", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType())]) { ExecutionMode = FlowNodeExecutionMode.Pure };
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "fixtureEntry", settings = new { } },
                new { id = "configured", kind = "configured", settings = new { inputValues = new { value = "wrong" } } },
                new { id = "prompt", kind = "prompt", prompt = "do it", responseFields = new[] { new { id = "score", name = "score", type = "number", min = 5, max = 1 } } },
                new { id = "count", kind = "count", settings = new { } }
            }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
        });

        var descriptions = TypedFlowCompiler.Describe(graph, [entry, host]);
        Assert.Equal(4, descriptions.Count);
        Assert.Contains(descriptions["configured"].Diagnostics, issue => issue.NodeId == "configured" && issue.Port == "value");
        Assert.Contains(descriptions["configured"].Inputs, port => port.Name == "value" && port.Type is NumberType);
        Assert.Contains(descriptions["prompt"].Diagnostics, issue => issue.NodeId == "prompt" && issue.Port == "score");
        Assert.Contains(descriptions["prompt"].Outputs, port => port.Id == "score" && port.Type is NumberType);
        Assert.Contains(descriptions["count"].Outputs, port => port.Name == "count");
    }

    [Fact]
    public void MixedHostDefaultsKeepValidConstantsAndRejectCompilation()
    {
        var entry = new NodeDefinition("fixtureEntry", 1, FlowEffect.Pure,
            controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry };
        var host = new NodeDefinition("configured", 1, FlowEffect.Pure,
            dataInputs: [new("a", new NumberType()), new("b", new NumberType())],
            controlInputs: [new("exec")], controlOutputs: [new("then")]);
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "fixtureEntry", settings = new { } },
                new { id = "configured", kind = "configured", settings = new { inputValues = new { a = 1, b = "wrong" } } },
                new { id = "other", kind = "count", settings = new { } }
            }, edges = Array.Empty<object>(),
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "configured", targetPort = "exec" } }
        });

        var descriptions = TypedFlowCompiler.Describe(graph, [entry, host]);
        var configured = descriptions["configured"];
        Assert.Equal(1, Assert.Single(configured.Inputs, port => port.Name == "a").ConstantValue!.Value.GetInt32());
        Assert.Null(Assert.Single(configured.Inputs, port => port.Name == "b").ConstantValue);
        Assert.Contains(configured.Diagnostics, issue => issue.Port == "b");
        Assert.Contains(descriptions.Keys, id => id == "other");

        var handler = new FlowNodeHandler(host,
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry", [new FlowNodeHandler(entry,
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()), isEntry: true), handler]));
    }

    [Fact]
    public void MemoryDescriptionsKeepFixedPortsForIncompatiblePeopleAndInvalidSettings()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "dm", settings = new { } },
                new { id = "incompatible", kind = "memory", settings = new { mode = "hybrid", scope = "person", categories = new[] { "fato" }, maxItems = 5, maxCharacters = 1000 } },
                new { id = "invalid", kind = "memory", settings = new { } }
            },
            edges = new[] { new { source = "entry", sourcePort = "message", target = "incompatible", targetPort = "persons" } },
            controlEdges = Array.Empty<object>()
        });

        var descriptions = TypedFlowCompiler.Describe(graph, Array.Empty<NodeDefinition>());
        var incompatible = descriptions["incompatible"];
        Assert.Contains(incompatible.Outputs, port => port.Name == "memories" && port.Type is ArrayType);
        Assert.Contains(incompatible.Inputs, port => port.Name == "query" && port.Type is StringType);
        Assert.Contains(incompatible.ControlInputs, port => port.Name == "exec");
        Assert.Contains(incompatible.ControlOutputs, port => port.Name == "then");
        Assert.Contains(incompatible.Diagnostics, issue => issue.Port == "persons");

        var invalid = descriptions["invalid"];
        Assert.Contains(invalid.Outputs, port => port.Name == "memories" && port.Type is ArrayType);
        Assert.Contains(invalid.Inputs, port => port.Name == "query" && port.Type is StringType);
        Assert.Contains(invalid.ControlInputs, port => port.Name == "exec");
        Assert.Contains(invalid.ControlOutputs, port => port.Name == "then");
        Assert.NotEmpty(invalid.Diagnostics);
    }

    [Fact]
    public void MetadataValidationSharesHostDefinitionConstraintsWithHandlerConstruction()
    {
        var invalidDefinitions = new[]
        {
            new NodeDefinition("badVersion", 2, FlowEffect.Send) { ExecutionMode = FlowNodeExecutionMode.Entry },
            new NodeDefinition("badPureEffect", 1, FlowEffect.Model,
                dataOutputs: [new("result", new NumberType())]) { ExecutionMode = FlowNodeExecutionMode.Pure }
        };
        foreach (var definition in invalidDefinitions)
        {
            var graph = definition.Kind == "badPureEffect"
                ? JsonSerializer.SerializeToElement(new
                {
                    version = 4,
                    nodes = new object[]
                    {
                        new { id = "entry", kind = "dm", settings = new { } },
                        new { id = "badPureEffect", kind = "badPureEffect", settings = new { } },
                        new { id = "consumer", kind = "prompt", prompt = "consume", inputs = new[] { new { id = "n", name = "n", type = "number" } } }
                    },
                    edges = new[] { new { source = "badPureEffect", sourcePort = "result", target = "consumer", targetPort = "n" } },
                    controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "consumer", targetPort = "exec" } }
                })
                : JsonSerializer.SerializeToElement(new
                {
                    version = 4, nodes = new[] { new { id = "entry", kind = definition.Kind, settings = new { } } },
                    edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
                });
            Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Validate(graph, "entry", [definition]));
            Assert.Throws<ArgumentException>(() => new FlowNodeHandler(definition,
                (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>())));
        }
    }

    [Fact]
    public void MemoryDoesNotInventPersonTypeFromAnUnresolvedUpstreamContract()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "dm", settings = new { } },
                new { id = "property", kind = "property", settings = new { path = new[] { "nome" } } },
                new { id = "memory", kind = "memory", settings = new { mode = "hybrid", scope = "person", categories = new[] { "fato" }, maxItems = 5, maxCharacters = 1000 } }
            },
            edges = new[] { new { source = "property", sourcePort = "value", target = "memory", targetPort = "persons" } },
            controlEdges = Array.Empty<object>()
        });

        var memory = TypedFlowCompiler.Describe(graph, Array.Empty<NodeDefinition>())["memory"];
        Assert.Null(Assert.Single(memory.Inputs, port => port.Name == "persons").Type);
        Assert.Contains(memory.Diagnostics, issue => issue.Port == "persons");
    }

    [Fact]
    public void HumanFeedbackTimeoutRequiresItsTimeoutExecutionEdgeInCompilerAndDescription()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "dm", settings = new { } },
                new { id = "feedback", kind = "humanFeedback", settings = new { question = "review?", timeoutSeconds = 30 } }
            },
            edges = Array.Empty<object>(),
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "feedback", targetPort = "exec" } }
        });

        var description = TypedFlowCompiler.Describe(graph, Array.Empty<NodeDefinition>())["feedback"];
        Assert.Contains(description.Diagnostics, issue => issue.Port == "timeout");
        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "entry"));
        Assert.Contains(error.Issues, issue => issue.NodeId == "feedback" && issue.Port == "config");
    }

    [Fact]
    public void ConnectedBuiltinUsesSameResolvedContractWithMetadataOnlyAndRuntimeCompiler()
    {
        var schema = new ArrayType(new StringType());
        var entryDefinition = new NodeDefinition("fixtureEntry", 1, FlowEffect.Pure,
            dataOutputs: [new("items", schema)], controlOutputs: [new("then")])
        { ExecutionMode = FlowNodeExecutionMode.Entry };
        var sinkDefinition = new NodeDefinition("sink", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType(), Required: true)], controlInputs: [new("exec")], controlOutputs: [new("then")]);
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "entry", kind = "fixtureEntry", settings = new { } },
                new { id = "count", kind = "count", settings = new { } },
                new { id = "sink", kind = "sink", settings = new { } }
            },
            edges = new[]
            {
                new { source = "entry", sourcePort = "items", target = "count", targetPort = "list" },
                new { source = "count", sourcePort = "count", target = "sink", targetPort = "value" }
            },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "sink", targetPort = "exec" } }
        });

        var description = TypedFlowCompiler.Describe(graph, [entryDefinition, sinkDefinition])["count"];
        Assert.Equal(schema, Assert.Single(description.Inputs).Type);
        Assert.Equal(new NumberType(), Assert.Single(description.Outputs).Type);
        var handler = new FlowNodeHandler(entryDefinition, (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        TypedFlow compiledFlow;
        try { compiledFlow = TypedFlowCompiler.Compile(graph, "entry", [handler, new FlowNodeHandler(sinkDefinition, (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()))]); }
        catch (TypedFlowException error) { Assert.Fail(string.Join(" | ", error.Issues.Select(issue => $"{issue.NodeId}.{issue.Port}: {issue.Reason}"))); return; }
        var compiled = compiledFlow.Nodes["count"];
        Assert.Equal(compiled.Inputs.Select(port => (port.Id, port.Type)), description.Inputs.Select(port => (port.Id, port.Type!)));
        Assert.Equal(compiled.Outputs.Select(port => (port.Id, port.Type)), description.Outputs.Select(port => (port.Id, port.Type!)));
    }

    private static JsonElement Graph(object entry, object node) => JsonSerializer.SerializeToElement(new
    {
        version = 4, nodes = new[] { entry, node }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
    });
}
