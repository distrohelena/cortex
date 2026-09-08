using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class TypedBranchTests
{
    internal static JsonNode Graph() => JsonNode.Parse("""
        {"version":3,"nodes":[
          {"id":"in","kind":"dm"},
          {"id":"ai","kind":"prompt","prompt":"Observe","inputs":[],"responseFields":[{"id":"signal","name":"signal","type":"boolean"}]},
          {"id":"branch","kind":"branch","settings":{"operator":"eq","b":true}}
        ],"edges":[{"source":"ai","sourcePort":"signal","target":"branch","targetPort":"a"}],
        "controlEdges":[{"source":"in","sourcePort":"then","target":"ai","targetPort":"exec"},{"source":"ai","sourcePort":"then","target":"branch","targetPort":"exec"}]}
        """)!;

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task BooleanBranchExecutesExactlyOneTypedExit(bool signal, string selected)
    {
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(Graph()), "in");
        var message = JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = (object?)null, canal = new { nome = "DM", nativeId = "fixture" }, reply = (object?)null });
        var result = await TypedFlowRunner.RunAsync(flow, message, new Model(signal), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal(selected, Assert.Single(result.Steps, s => s.NodeId == "branch").Exit);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("actor?")]
    [InlineData("message[]")]
    public void RejectsNonScalarOrOptionalComparisons(string type)
    {
        var graph = Graph();
        // Test Branch itself, not the flat-output restriction of Instruction.
        graph["nodes"]![1]!["responseFields"] = new JsonArray();
        graph["edges"]![0]!["source"] = "in";
        graph["edges"]![0]!["sourcePort"] = "message";
        if (type == "actor?")
        {
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{"id":"property","kind":"property","settings":{"path":["remetente"]}}"""));
            graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"in","sourcePort":"message","target":"property","targetPort":"object"}"""));
            graph["edges"]![0]!["source"] = "property"; graph["edges"]![0]!["sourcePort"] = "value";
        }
        if (type == "message[]")
        {
            graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{"id":"history","kind":"history"}"""));
            graph["edges"]![0]!["source"] = "history"; graph["edges"]![0]!["sourcePort"] = "messages";
            graph["controlEdges"]![1]!["target"] = "history";
            graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"history","sourcePort":"then","target":"branch","targetPort":"exec"}"""));
        }
        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
        Assert.Contains(error.Issues, i => i.NodeId == "branch");
    }

    [Fact]
    public void NoStringToBooleanCoercion()
    {
        var graph = Graph(); graph["nodes"]![2]!["settings"]!["b"] = "true";
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }

    [Fact]
    public void WiredAndConfiguredBIsInvalid()
    {
        var graph = Graph();
        graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"ai","sourcePort":"signal","target":"branch","targetPort":"b"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in"));
    }

    [Theory]
    [InlineData("eq", "false")]
    [InlineData("ne", "true")]
    [InlineData("gt", "true")]
    [InlineData("gte", "true")]
    [InlineData("lt", "false")]
    [InlineData("lte", "false")]
    public async Task NumericOperatorsKeepTypedComparison(string operation, string expected)
    {
        var graph = Graph(); graph["nodes"]![1]!["responseFields"]![0]!["type"] = "number";
        graph["nodes"]![2]!["settings"]!["operator"] = operation; graph["nodes"]![2]!["settings"]!["b"] = 1;
        var flow = TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph), "in");
        var message = JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = (object?)null, canal = new { nome = "DM", nativeId = "fixture" }, reply = (object?)null });
        var result = await TypedFlowRunner.RunAsync(flow, message, new Model(2), TestContext.Current.CancellationToken);
        Assert.True(result.Success); Assert.Equal(expected, Assert.Single(result.Steps, s => s.NodeId == "branch").Exit);
    }

    private sealed class Model(object signal) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => Task.FromResult(JsonSerializer.Serialize(new { signal }));
    }
}
