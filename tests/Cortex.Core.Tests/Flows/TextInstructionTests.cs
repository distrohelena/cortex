using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class TextInstructionTests
{
    private static JsonNode Graph(string type = "text") => JsonNode.Parse($$"""
        {"version":4,"nodes":[{"id":"in","kind":"dm"},
        {"id":"ai","kind":"prompt","prompt":"Write","outputMode":"text","inputs":[],"correctionRetry":false,
        "responseFields":[{"id":"stable-text","name":"texto","type":"{{type}}"}]}],"edges":[],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"ai","targetPort":"exec"}]}
        """)!;

    [Fact]
    public async Task NamedTextOutputDoesNotRequestJson()
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph()), "in");
        Assert.Null(TypedFlowRunner.ResponseSchema(flow.Nodes["ai"]));
        var result = await TypedFlowRunner.RunAsync(flow, Message(), new Model("Uma fala."), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("Uma fala.", result.Outputs["ai"]["stable-text"].Value.GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t")]
    public async Task EmptyTextNeverPublishesOutputs(string text)
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph()), "in");
        var result = await TypedFlowRunner.RunAsync(flow, Message(), new Model(text), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.False(result.Outputs.ContainsKey("ai"));
    }

    [Fact]
    public void TextModeCannotDeclareNumberOutput() => Assert.Throws<TypedFlowException>(() =>
        TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph("number")), "in"));

    [Fact]
    public void TextModeCannotPublishMultipleFields()
    {
        var graph = Graph();
        graph["nodes"]![1]!["responseFields"]!.AsArray().Add(JsonNode.Parse("""{"id":"extra","name":"extra","type":"text"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }

    [Fact]
    public async Task TextStillEnforcesExplicitWordLimit()
    {
        var graph = Graph();
        graph["nodes"]![1]!["responseFields"]![0]!["maxWords"] = 2;
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in");
        var result = await TypedFlowRunner.RunAsync(flow, Message(), new Model("um dois três"), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Empty(result.Outputs);
    }

    private static JsonElement Message() => JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = (object?)null, canal = new { nome = "DM", nativeId = "fixture" }, reply = (object?)null });
    private sealed class Model(string text) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => Task.FromResult(text);
    }
}
