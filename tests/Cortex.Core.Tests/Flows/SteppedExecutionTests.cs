using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Execution;
using Xunit;
namespace Cortex.Core.Tests.Flows;
public sealed class SteppedExecutionTests
{
    [Fact]
    public async Task SteppedOutputEditPreservesNativeJsonValues()
    {
        var type = new JsonType();
        var calls = new List<string>();
        var entry = new FlowNodeHandler(new NodeDefinition("jsonEntry", 1, FlowEffect.Pure,
            controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry }, (_, _, _) =>
            Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()));
        var producer = new FlowNodeHandler(new NodeDefinition("jsonProducer", 1, FlowEffect.Pure,
            dataOutputs: [new("payload", type)], controlInputs: [new("exec")], controlOutputs: [new("then")]), (_, _, _) =>
        {
            calls.Add("producer");
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement> { ["payload"] = JsonDocument.Parse("\"original\"").RootElement.Clone() });
        });
        var edited = JsonDocument.Parse("null").RootElement.Clone();
        var consumer = new FlowNodeHandler(new NodeDefinition("jsonConsumer", 1, FlowEffect.Pure,
            dataInputs: [new("payload", type, Required: true)], controlInputs: [new("exec")]), (_, inputs, _) =>
        {
            calls.Add("consumer");
            Assert.True(JsonElement.DeepEquals(edited, inputs["payload"].Value));
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>());
        });
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new[] { new { id = "entry", kind = "jsonEntry" }, new { id = "producer", kind = "jsonProducer" }, new { id = "consumer", kind = "jsonConsumer" } },
            edges = new[] { new { source = "producer", sourcePort = "payload", target = "consumer", targetPort = "payload" } },
            controlEdges = new[] { new { source = "entry", sourcePort = "then", target = "producer", targetPort = "exec" }, new { source = "producer", sourcePort = "then", target = "consumer", targetPort = "exec" } }
        });
        var session = new TypedFlowExecution(TypedFlowCompiler.Compile(graph, "entry", [entry, producer, consumer]));

        await session.AdvanceAsync(TestContext.Current.CancellationToken);
        await session.AdvanceAsync(TestContext.Current.CancellationToken);
        session.AmendLastOutputs(new Dictionary<string, JsonElement> { ["payload"] = edited });
        var final = await session.AdvanceAsync(TestContext.Current.CancellationToken);

        Assert.True(session.Completed);
        Assert.Equal(new[] { "producer", "consumer" }, calls);
        Assert.True(JsonElement.DeepEquals(edited, final.Outputs["producer"]["payload"].Value));
    }

    [Fact]
    public async Task EditingUpstreamOperationOutputRecomputesPureValuesWithoutRepeatingThatOperation()
    {
        var calls = new List<string>();
        var entry = new FlowNodeHandler(new NodeDefinition("entry", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new NumberType())], controlOutputs: [new("then")]),
            (_, _, _) =>
            {
                calls.Add("entry");
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                    new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(3) });
            }, isEntry: true);
        var producer = new FlowNodeHandler(new NodeDefinition("producer", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new NumberType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, _, _) =>
            {
                calls.Add("producer");
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                    new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(10) });
            });
        var pure = new FlowNodeHandler(new NodeDefinition("pure", 1, FlowEffect.Pure,
            dataInputs: [new("value", new NumberType(), Required: true)],
            dataOutputs: [new("left", new NumberType()), new("right", new NumberType())])
            { ExecutionMode = FlowNodeExecutionMode.Pure },
            (_, inputs, _) =>
            {
                calls.Add("pure");
                var value = inputs["value"].Value.GetInt32();
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                {
                    ["left"] = JsonSerializer.SerializeToElement(value + 1),
                    ["right"] = JsonSerializer.SerializeToElement(value + 2)
                });
            });
        var consumer = new FlowNodeHandler(new NodeDefinition("consumer", 1, FlowEffect.Pure,
            dataInputs: [new("left", new NumberType(), Required: true), new("right", new NumberType(), Required: true)],
            dataOutputs: [new("sum", new NumberType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, inputs, _) =>
            {
                calls.Add("consumer");
                return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
                {
                    ["sum"] = JsonSerializer.SerializeToElement(inputs["left"].Value.GetInt32() + inputs["right"].Value.GetInt32())
                });
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
        var session = new TypedFlowExecution(TypedFlowCompiler.Compile(graph, "entry", [entry, producer, pure, consumer]));

        await session.AdvanceAsync(TestContext.Current.CancellationToken);
        await session.AdvanceAsync(TestContext.Current.CancellationToken);
        session.AmendLastOutputs(new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(20) });
        var final = await session.AdvanceAsync(TestContext.Current.CancellationToken);

        Assert.True(session.Completed);
        Assert.Equal(new[] { "entry", "producer", "pure", "consumer" }, calls);
        Assert.Equal(43, final.Outputs["consumer"]["sum"].Value.GetInt32());
    }

    [Fact]
    public async Task EachAdvanceRunsOneNodeAndKeepsPreviousOutputsWithoutReplaying()
    {
        var calls = new List<string>();
        FlowNodeHandler Handler(string kind, bool entry) => new(new NodeDefinition(kind, 1, FlowEffect.Pure, dataInputs: entry ? [] : [new("value", new StringType(), Required:true)], dataOutputs:[new("value", new StringType())], controlInputs:entry ? [] : [new("exec")], controlOutputs:[new("then")]), (_, inputs, _) => { calls.Add(kind); return Task.FromResult<IReadOnlyDictionary<string,JsonElement>>(new Dictionary<string,JsonElement> { ["value"] = JsonSerializer.SerializeToElement(entry ? "start" : inputs["value"].Value.GetString()+"-"+kind) }); }, entry);
        var graph = JsonSerializer.SerializeToElement(new { version=4, nodes=new[]{new{id="a",kind="first"},new{id="b",kind="second"},new{id="c",kind="third"}}, edges=new[]{new{source="a",sourcePort="value",target="b",targetPort="value"},new{source="b",sourcePort="value",target="c",targetPort="value"}},controlEdges=new[]{new{source="a",sourcePort="then",target="b",targetPort="exec"},new{source="b",sourcePort="then",target="c",targetPort="exec"}}});
        var session = new TypedFlowExecution(TypedFlowCompiler.Compile(graph,"a",[Handler("first",true),Handler("second",false),Handler("third",false)]));
        var first = await session.AdvanceAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b", session.NextNodeId); Assert.Single(first.Steps); Assert.Equal(new[]{"first"},calls);
        session.AmendLastOutputs(new Dictionary<string,JsonElement>{["value"]=JsonSerializer.SerializeToElement("human edit")});
        await session.AdvanceAsync(TestContext.Current.CancellationToken);
        var final = await session.AdvanceAsync(TestContext.Current.CancellationToken);
        Assert.True(session.Completed); Assert.Null(session.NextNodeId);
        Assert.Equal(new[]{"first","second","third"},calls); Assert.Single(first.Steps);
        Assert.Equal("human edit-second-third",final.Outputs["c"]["value"].Value.GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.AdvanceAsync(TestContext.Current.CancellationToken));
    }
}
