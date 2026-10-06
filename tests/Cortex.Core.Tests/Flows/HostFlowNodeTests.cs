using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class HostFlowNodeTests
{
    private static readonly JsonElement Graph = JsonSerializer.SerializeToElement(new
    {
        version = 3,
        nodes = new[] { new { id = "file", kind = "file" }, new { id = "result", kind = "result" } },
        edges = new[] { new { source = "file", sourcePort = "path", target = "result", targetPort = "path" } },
        controlEdges = new[] { new { source = "file", sourcePort = "then", target = "result", targetPort = "exec" } }
    });

    [Fact]
    public async Task HostNodesRunThroughExistingKernelWithoutChatOrModelCompletion()
    {
        var calls = new List<string>();
        var flow = TypedFlowCompiler.Compile(Graph, "file", Handlers(calls));
        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(new[] { "file", "result" }, calls);
        Assert.Equal(new[] { "file", "result" }, result.Steps.Select(s => s.NodeId));
        Assert.Equal("video.mp4", result.Outputs["result"]["path"].Value.GetString());
    }

    [Fact]
    public async Task ArtifactWritingHostRunsInItsOwnStepWithoutCallingAModel()
    {
        var calls = new List<string>();
        var flow = TypedFlowCompiler.Compile(Graph, "file", Handlers(calls, effect: FlowEffect.WriteState));
        var state = FlowExecutionKernel.Start(flow, JsonSerializer.SerializeToElement(new { }), Durable.IncrementalKernelTests.Identity(), null);
        var services = new FlowExecutionServices(new Durable.IncrementalKernelTests.ScriptedModel());
        var first = await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "file" }, calls);
        Assert.Equal(StepEffect.Artifact, first.NextEffect);
        var last = await FlowExecutionKernel.AdvanceAsync(flow, state, services, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "file", "result" }, calls);
        Assert.True(last.Result.Success);
        Assert.Equal(ExecutionStatus.Completed, last.Status);
    }

    [Fact]
    public void CompilerRejectsIncompatibleHostPortBeforeExecutingAnyHandler()
    {
        var calls = new List<string>();
        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Graph, "file", Handlers(calls, wrongType: true)));
        Assert.Contains(error.Issues, issue => issue.NodeId == "result" && issue.Port == "path");
        Assert.Empty(calls);
    }

    [Fact]
    public async Task InvalidHostOutputFailsBeforeDownstreamNode()
    {
        var calls = new List<string>();
        var flow = TypedFlowCompiler.Compile(Graph, "file", Handlers(calls, invalidOutput: true));
        var result = await TypedFlowRunner.RunAsync(flow, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(new[] { "file" }, calls);
        Assert.Equal("failed", result.Steps.Single().Status);
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public void DataConnectionWithoutExecutionConnectionIsRejected()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3, nodes = Graph.GetProperty("nodes"), edges = Graph.GetProperty("edges"), controlEdges = Array.Empty<object>()
        });
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "file", Handlers([])));
    }

    private static FlowNodeHandler[] Handlers(List<string> calls, bool wrongType = false, bool invalidOutput = false, FlowEffect effect = FlowEffect.Pure) =>
    [
        new(new NodeDefinition("file", 1, FlowEffect.Pure,
            dataOutputs: [new("path", new StringType())], controlOutputs: [new("then")]),
            (_, _, _) =>
            {
                calls.Add("file");
                IReadOnlyDictionary<string, JsonElement> output = new Dictionary<string, JsonElement>
                { ["path"] = invalidOutput ? JsonSerializer.SerializeToElement(42) : JsonSerializer.SerializeToElement("video.mp4") };
                return Task.FromResult(output);
            }, isEntry: true),
        new(new NodeDefinition("result", 1, effect,
            dataInputs: [new("path", wrongType ? new NumberType() : new StringType(), Required: true)],
            dataOutputs: [new("path", new StringType())], controlInputs: [new("exec")]),
            (_, inputs, _) =>
            {
                calls.Add("result");
                IReadOnlyDictionary<string, JsonElement> output = new Dictionary<string, JsonElement> { ["path"] = inputs["path"].Value };
                return Task.FromResult(output);
            })
    ];
}
