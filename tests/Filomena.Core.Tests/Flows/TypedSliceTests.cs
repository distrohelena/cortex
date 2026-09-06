using System.Text.Json;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class TypedSliceTests
{
    public static JsonElement Fixture() => JsonSerializer.SerializeToElement(new
    {
        version = 3,
        nodes = new object[] {
            new { id="in", kind="dm" },
            new { id="prop", kind="property", settings=new { path=new[]{"texto"} } },
            new { id="ai", kind="prompt", prompt="Decida", inputs=new[]{new{id="i1",name="mensagem",type="text",required=true}}, responseFields=new object[]{
                new{id="o1",name="acao",type="enum",values=new[]{"calar","esperar","responder"}},
                new{id="o2",name="proximo_estado",type="enum",values=new[]{"inativa","atenta","engajada"}},
                new{id="o3",name="motivo",type="text",maxWords=10}} },
            new { id="switch",kind="switch",settings=new{cases=new[]{new{id="c1",value="calar"},new{id="c2",value="esperar"},new{id="c3",value="responder"}},other=false} },
            new { id="done",kind="prompt",prompt="Use motivo",inputs=new[]{new{id="why",name="motivo",type="text",required=true}},responseFields=new[]{new{id="r",name="texto",type="text"}} }
        },
        edges = new[] {
            new{source="in",sourcePort="message",target="prop",targetPort="object"},
            new{source="prop",sourcePort="value",target="ai",targetPort="i1"},
            new{source="ai",sourcePort="o1",target="switch",targetPort="value"},
            new{source="ai",sourcePort="o3",target="done",targetPort="why"}
        },
        controlEdges = new[] { new { source = "in", sourcePort = "then", target = "ai", targetPort = "exec" }, new { source = "ai", sourcePort = "then", target = "switch", targetPort = "exec" }, new { source = "switch", sourcePort = "c3", target = "done", targetPort = "exec" } }
    });

    private static JsonElement Message => JsonSerializer.SerializeToElement(new { texto = "oi", remetente = new { nome = "Helena", stableId = "fixture:helena" }, canal = new { nome = "DM", nativeId = "dm1" }, reply = (object?)null });

    [Theory]
    [InlineData("responder", "c3", 2)]
    [InlineData("calar", "c1", 1)]
    [InlineData("esperar", "c2", 1)]
    public async Task ExecutesOnlySelectedCaseAndPublishesTypedOutputs(string action, string branch, int calls)
    {
        var fake = new FakeCompletion([JsonSerializer.Serialize(new { acao = action, proximo_estado = "engajada", motivo = "foi chamada" }), "{\"texto\":\"ok\"}"]);
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Fixture(), "in"), Message, fake, TestContext.Current.CancellationToken);
        Assert.True(result.Success); Assert.Equal(calls, fake.Requests.Count);
        Assert.Contains(result.Steps, s => s.NodeId == "switch" && s.Exit == branch);
        Assert.Equal(action, result.Outputs["ai"]["o1"].Value.GetString());
        Assert.IsType<EnumType>(result.Outputs["ai"]["o1"].Type);
        Assert.Equal("oi", fake.Requests[0].Inputs.GetProperty("mensagem").GetString());
        if (calls == 2) Assert.Equal("foi chamada", fake.Requests[1].Inputs.GetProperty("motivo").GetString());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"acao\":\"x\",\"proximo_estado\":\"engajada\",\"motivo\":\"oi\"}")]
    [InlineData("{\"acao\":false,\"proximo_estado\":\"engajada\",\"motivo\":\"oi\"}")]
    [InlineData("{\"acao\":\"calar\",\"proximo_estado\":\"engajada\",\"motivo\":\"oi\",\"extra\":1}")]
    [InlineData("{\"acao\":\"calar\",\"proximo_estado\":\"engajada\",\"motivo\":\"um dois tres quatro cinco seis sete oito nove dez onze\"}")]
    public async Task InvalidOutputsNeverPublishAndRetryIsBounded(string invalid)
    {
        var fake = new FakeCompletion([invalid, invalid]);
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Fixture(), "in"), Message, fake, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Empty(result.Outputs); Assert.Equal(2, fake.Requests.Count);
        Assert.DoesNotContain(result.Steps, s => s.NodeId == "switch");
        var recovered = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Fixture(), "in"), Message, new FakeCompletion([invalid, "{\"acao\":\"calar\",\"proximo_estado\":\"atenta\",\"motivo\":\"aguardar\"}"]), TestContext.Current.CancellationToken);
        Assert.True(recovered.Success);
    }

    private sealed class FakeCompletion(IEnumerable<string> outputs) : IFlowCompletion
    {
        private readonly Queue<string> queue = new(outputs);
        public List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) { token.ThrowIfCancellationRequested(); Requests.Add(request); return Task.FromResult(queue.Dequeue()); }
    }
}
