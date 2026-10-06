using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;
namespace Cortex.Core.Tests.Flows;
public sealed class SteppedExecutionTests
{
    [Fact]
    public async Task EachAdvanceRunsOneNodeAndKeepsPreviousOutputsWithoutReplaying()
    {
        var calls = new List<string>();
        FlowNodeHandler Handler(string kind, bool entry) => new(new NodeDefinition(kind, 1, FlowEffect.Pure, dataInputs: entry ? [] : [new("value", new StringType(), Required:true)], dataOutputs:[new("value", new StringType())], controlInputs:entry ? [] : [new("exec")], controlOutputs:[new("then")]), (_, inputs, _) => { calls.Add(kind); return Task.FromResult<IReadOnlyDictionary<string,JsonElement>>(new Dictionary<string,JsonElement> { ["value"] = JsonSerializer.SerializeToElement(entry ? "start" : inputs["value"].Value.GetString()+"-"+kind) }); }, entry);
        var graph = JsonSerializer.SerializeToElement(new { version=3, nodes=new[]{new{id="a",kind="first"},new{id="b",kind="second"},new{id="c",kind="third"}}, edges=new[]{new{source="a",sourcePort="value",target="b",targetPort="value"},new{source="b",sourcePort="value",target="c",targetPort="value"}},controlEdges=new[]{new{source="a",sourcePort="then",target="b",targetPort="exec"},new{source="b",sourcePort="then",target="c",targetPort="exec"}}});
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
