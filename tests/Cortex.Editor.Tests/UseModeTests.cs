using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;
namespace Cortex.Editor.Tests;
public sealed class UseModeTests
{
    [Fact]
    public async Task InputsAreOverridesAndEachStageExecutesOnce()
    {
        var graph=Graph(); var calls=new List<string>();
        var handlers=new[]{Handler("input",true,calls),Handler("transform",false,calls),Handler("save",false,calls)};
        using var service=new EditorUseService(graph,"a",handlers);
        var parameters=new Dictionary<string,Dictionary<string,JsonElement>> { ["a"]=new(){["text"]=JsonSerializer.SerializeToElement("custom")} };
        await service.AdvanceAsync(parameters,TestContext.Current.CancellationToken); await service.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("paused",service.Snapshot().State); Assert.Equal("b",service.Snapshot().NextNodeId); Assert.Equal(new[]{"input"},calls);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AdvanceAsync(parameters,TestContext.Current.CancellationToken));
        await service.AdvanceAsync([],TestContext.Current.CancellationToken); await service.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"input","transform"},calls);
        await service.AdvanceAsync([],TestContext.Current.CancellationToken); await service.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("completed",service.Snapshot().State); Assert.Equal(new[]{"input","transform","save"},calls);
        Assert.Equal("original",graph.GetProperty("nodes")[0].GetProperty("settings").GetProperty("text").GetString());
        Assert.Equal("custom",service.Snapshot().Result!.Value.GetProperty("outputs").GetProperty("c").GetProperty("value").GetProperty("value").GetString());
        await service.ResetAsync(TestContext.Current.CancellationToken); Assert.Equal("idle",service.Snapshot().State);
    }
    [Fact]
    public async Task UnexposedParametersAndUnknownNodesAreRejectedBeforeExecution()
    {
        var calls=new List<string>(); using var service=new EditorUseService(Graph(),"a",[Handler("input",true,calls),Handler("transform",false,calls),Handler("save",false,calls)]);
        foreach(var key in new[]{"private","__proto__"})
            await Assert.ThrowsAsync<InvalidDataException>(()=>service.AdvanceAsync(new(){["a"]=new(){[key]=JsonSerializer.SerializeToElement("x")}},TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.AdvanceAsync(new(){["unknown"]=new(){["text"]=JsonSerializer.SerializeToElement("x")}},TestContext.Current.CancellationToken));
        Assert.Empty(calls); Assert.Equal("idle",service.Snapshot().State);
    }
    private static FlowNodeHandler Handler(string kind,bool entry,List<string> calls)=>new(new NodeDefinition(kind,1,kind=="transform"?FlowEffect.Model:kind=="save"?FlowEffect.WriteState:FlowEffect.Pure,dataInputs:entry?[]:[new("value",new StringType(),Required:true)],dataOutputs:[new("value",new StringType())],controlInputs:entry?[]:[new("exec")],controlOutputs:[new("then")],configFields:entry?[new("text",new StringType(),Required:true),new("private",new StringType())]:[]), (config,inputs,_)=>{calls.Add(kind);return Task.FromResult<IReadOnlyDictionary<string,JsonElement>>(new Dictionary<string,JsonElement>{["value"]=entry?config.GetProperty("text").Clone():inputs["value"].Value.Clone()});},entry);
    private static JsonElement Graph()=>JsonSerializer.SerializeToElement(new {version=3,usage=new{fields=new Dictionary<string,string[]>{["a"]=["text"]}},nodes=new object[]{new{id="a",kind="input",settings=new{text="original",@private="locked"}},new{id="b",kind="transform"},new{id="c",kind="save"}},edges=new[]{new{source="a",sourcePort="value",target="b",targetPort="value"},new{source="b",sourcePort="value",target="c",targetPort="value"}},controlEdges=new[]{new{source="a",sourcePort="then",target="b",targetPort="exec"},new{source="b",sourcePort="then",target="c",targetPort="exec"}}});
}
