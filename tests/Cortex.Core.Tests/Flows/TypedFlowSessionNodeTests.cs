using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class TypedFlowSessionNodeTests
{
    [Fact]
    public void GlobalValidationRejectsInvalidEdgeAndCycleOutsideSelectedEntryPath()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new { id = "selected-exit", kind = "exit", settings = new { mode = "switch", targetStateId = "inactive" } },
                new { id = "group", kind = "group" },
                new
                {
                    id = "inactive-a",
                    kind = "prompt",
                    prompt = "A",
                    inputs = new[] { new { id = "value", name = "value", type = "text", required = false } },
                },
                new { id = "inactive-b", kind = "prompt", prompt = "B" },
            },
            edges = new[]
            {
                new { source = "missing", sourcePort = "message", target = "inactive-a", targetPort = "value" },
            },
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "selected-exit", targetPort = "exec" },
                new { source = "group", sourcePort = "then", target = "inactive-a", targetPort = "exec" },
                new { source = "inactive-a", sourcePort = "then", target = "inactive-b", targetPort = "exec" },
                new { source = "inactive-b", sourcePort = "then", target = "inactive-a", targetPort = "exec" },
            },
        });

        var exception = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "dm"));

        Assert.Contains(exception.Issues, issue => issue.Reason.Contains("Origem inexistente", StringComparison.Ordinal));
        Assert.Contains(exception.Issues, issue => issue.Reason.Contains("Ciclo não permitido", StringComparison.Ordinal));
    }

    [Fact]
    public void DynamicExitRequiresAnExactEnumToStateMapping()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new
                {
                    id = "decide",
                    kind = "prompt",
                    prompt = "Choose",
                    responseFields = new[]
                    {
                        new { id = "state", name = "state", type = "enum", values = new[] { "wake", "sleep" } },
                    },
                },
                new
                {
                    id = "exit",
                    kind = "exit",
                    settings = new
                    {
                        mode = "switch",
                        stateMapping = new Dictionary<string, string>
                        {
                            ["wake"] = "engaged",
                            ["sleep"] = "inactive",
                        },
                    },
                },
            },
            edges = new[]
            {
                new { source = "decide", sourcePort = "state", target = "exit", targetPort = "state" },
            },
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "decide", targetPort = "exec" },
                new { source = "decide", sourcePort = "then", target = "exit", targetPort = "exec" },
            },
        });

        var flow = TypedFlowCompiler.Compile(graph, "dm");

        var input = Assert.Single(flow.Nodes["exit"].Inputs);
        Assert.Equal("state", input.Id);
        Assert.Equal(["wake", "sleep"], Assert.IsType<EnumType>(input.Type).Values);
    }

    [Fact]
    public void ConfiguredExitIsTerminalAndAcceptsOnlySwitchMode()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new { id = "exit", kind = "exit", settings = new { mode = "switch", targetStateId = "engaged" } },
            },
            edges = Array.Empty<object>(),
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "exit", targetPort = "exec" },
            },
        });

        var flow = TypedFlowCompiler.Compile(graph, "dm");

        Assert.Empty(flow.Nodes["exit"].Inputs);
        Assert.Empty(flow.Nodes["exit"].Outputs);
        Assert.Empty(flow.Nodes["exit"].Exits);
    }

    [Fact]
    public void HistoryHasNoDataInputAndPublishesMessagesOnThen()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new
                {
                    id = "history",
                    kind = "history",
                    settings = new
                    {
                        maxMessages = 20,
                        maxAgeMinutes = (double?)null,
                        maxCharacters = 4000,
                        includeCharacterMessages = false,
                        mode = "before-current-batch",
                    },
                },
            },
            edges = Array.Empty<object>(),
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "history", targetPort = "exec" },
            },
        });

        var flow = TypedFlowCompiler.Compile(graph, "dm");

        var history = flow.Nodes["history"];
        Assert.Empty(history.Inputs);
        var messages = Assert.IsType<ArrayType>(Assert.Single(history.Outputs).Type);
        Assert.Equal(FlowDomain.Message, messages.ElementType);
        Assert.Equal(["then"], history.Exits);
    }

    [Theory]
    [InlineData("dm", "group")]
    [InlineData("group", "dm")]
    public void SharedEntryPortKeepsOnlyTheSelectedEntryProducer(string selected, string inactive)
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new { id = "group", kind = "group" },
                new
                {
                    id = "batch",
                    kind = "accumulate",
                    settings = new { quietSeconds = (double?)null, maxMessages = 2, maxWaitSeconds = (double?)null },
                },
            },
            edges = new[]
            {
                new { source = "dm", sourcePort = "message", target = "batch", targetPort = "message" },
                new { source = "group", sourcePort = "message", target = "batch", targetPort = "message" },
            },
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "batch", targetPort = "exec" },
                new { source = "group", sourcePort = "then", target = "batch", targetPort = "exec" },
            },
        });

        var flow = TypedFlowCompiler.Compile(graph, selected);

        var edge = Assert.Single(flow.Graph.DataEdges);
        Assert.Equal(selected, edge.Source.NodeId);
        Assert.DoesNotContain(flow.Graph.Nodes, node => node.Id == inactive);
        Assert.DoesNotContain(flow.Graph.ControlEdges, item => item.Source.NodeId == inactive);
    }

    [Fact]
    public void AccumulateHasStableMessageBatchAndExecutionPorts()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new object[]
            {
                new { id = "dm", kind = "dm" },
                new
                {
                    id = "batch",
                    kind = "accumulate",
                    settings = new { quietSeconds = 2.0, maxMessages = (int?)null, maxWaitSeconds = 10.0 },
                },
            },
            edges = new[]
            {
                new { source = "dm", sourcePort = "message", target = "batch", targetPort = "message" },
            },
            controlEdges = new[]
            {
                new { source = "dm", sourcePort = "then", target = "batch", targetPort = "exec" },
            },
        });

        var flow = TypedFlowCompiler.Compile(graph, "dm");

        var accumulator = flow.Nodes["batch"];
        Assert.Equal("message", Assert.Single(accumulator.Inputs).Id);
        Assert.Equal(FlowDomain.Message, accumulator.Inputs[0].Type);
        var messages = Assert.IsType<ArrayType>(Assert.Single(accumulator.Outputs).Type);
        Assert.Equal(FlowDomain.Message, messages.ElementType);
        Assert.Equal(["then"], accumulator.Exits);
    }
}
