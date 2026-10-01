using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class TypedFlowSafetyTests
{
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static JsonObject Graph() => JsonNode.Parse(TypedSliceTests.Fixture().GetRawText())!.AsObject();
    private static JsonNode Node(JsonObject graph, string id) => graph["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == id)!;
    private static JsonElement Message => JsonSerializer.SerializeToElement(new { texto = "oi", remetente = (object?)null, canal = new { nome = "teste", nativeId = "private-id" }, reply = (object?)null });
    private static readonly string Valid = """{"acao":"calar","proximo_estado":"atenta","motivo":"um\tdois\ntrês"}""";

    [Fact]
    public void StablePortsSurviveRenamesButNotIncompatibleTypesOrRemovedPorts()
    {
        var graph = Graph(); var input = Node(graph, "ai")["inputs"]![0]!;
        input["name"] = "renomeada"; Node(graph, "ai")["responseFields"]![2]!["name"] = "razao";
        Assert.Equal("i1", TypedFlowCompiler.Compile(Json(graph), "in").Nodes["ai"].Inputs[0].Id);
        input["type"] = "number";
        Assert.Contains(Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in")).Issues, i => i.NodeId == "ai" && i.Port == "i1");
        Assert.Equal(4, graph["edges"]!.AsArray().Count);
        input["type"] = "text";
        TypedFlowCompiler.Compile(Json(graph), "in");
        Node(graph, "ai")["responseFields"]!.AsArray().RemoveAt(2);
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in"));
    }

    [Fact]
    public void RejectsUnavailableBranchOutputsCyclesFanoutAndConfiguredWireConflict()
    {
        var graph = Graph();
        graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"done","sourcePort":"r","target":"ai","targetPort":"i1"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in"));
        graph = Graph(); graph["controlEdges"]![2]!["source"] = "in"; graph["controlEdges"]![2]!["sourcePort"] = "then";
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in"));
        graph = Graph(); Node(graph, "ai")["inputs"]![0]!["value"] = "local";
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in"));
        graph = Graph();
        graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"done","sourcePort":"then","target":"ai","targetPort":"exec"}"""));
        Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in"));
        // done is bypassed on c1. Its output must not be available to the later consumer.
        graph = Graph();
        var consumer = Node(graph, "done").DeepClone(); consumer["id"] = "consumer";
        graph["nodes"]!.AsArray().Add(consumer);
        graph["edges"]!.AsArray().Add(JsonNode.Parse("""{"source":"done","sourcePort":"r","target":"consumer","targetPort":"why"}"""));
        graph["controlEdges"]!.AsArray().Add(JsonNode.Parse("""{"source":"switch","sourcePort":"c1","target":"consumer","targetPort":"exec"}"""));
        Assert.Contains(Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Json(graph), "in")).Issues, i => i.NodeId == "consumer" && i.Reason.Contains("Produtor"));
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("1e999")]
    [InlineData("\"0.5\"")]
    public async Task NumericBoundsAndFiniteValuesAreValidatedAtomically(string value)
    {
        var graph = Graph();
        Node(graph, "ai")["responseFields"]!.AsArray().Add(JsonNode.Parse("""{"id":"weight","name":"peso","type":"number","min":0,"max":1}"""));
        var flow = TypedFlowCompiler.Compile(Json(graph), "in");
        var schema = TypedFlowRunner.ResponseSchema(flow.Nodes["ai"])!.Value;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(4, schema.GetProperty("required").GetArrayLength());
        var fake = new Completion(Valid[..^1] + ",\"peso\":" + value + "}");
        var result = await TypedFlowRunner.RunAsync(flow, Message, fake, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Empty(result.Outputs); Assert.Equal(2, fake.Count);
    }

    [Theory]
    [InlineData("\"false\"", false)]
    [InlineData("false", true)]
    public async Task BooleanIsNotCoerced(string value, bool valid)
    {
        var graph = Graph();
        Node(graph, "ai")["responseFields"]!.AsArray().Add(JsonNode.Parse("""{"id":"care","name":"cuidado","type":"boolean"}"""));
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Json(graph), "in"), Message, new Completion(Valid[..^1] + ",\"cuidado\":" + value + "}"), TestContext.Current.CancellationToken);
        Assert.Equal(valid, result.Success);
    }

    [Fact]
    public async Task DuplicateFieldsFailAndRetryCanBeDisabled()
    {
        var graph = Graph(); Node(graph, "ai")["correctionRetry"] = false;
        var fake = new Completion(Valid[..^1] + ",\"acao\":\"responder\"}");
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Json(graph), "in"), Message, fake, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Empty(result.Outputs); Assert.Equal(1, fake.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextSwitchHasExactComparisonAndExplicitOther(bool other)
    {
        var graph = Graph();
        graph["edges"]![2]!["sourcePort"] = "o3";
        Node(graph, "switch")["settings"]!["other"] = other;
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(Json(graph), "in"), Message, new Completion(Valid), TestContext.Current.CancellationToken);
        Assert.Equal(other, result.Success);
        Assert.Equal(other ? "other" : null, result.Steps.Last().Exit);
    }

    [Fact]
    public async Task AsyncCompletionIsAwaitedCancellationPublishesNothingAndRunsAreIndependent()
    {
        var flow = TypedFlowCompiler.Compile(Json(Graph()), "in");
        var blocked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = TypedFlowRunner.RunAsync(flow, Message, new WaitingCompletion(blocked.Task), cancel.Token);
        Assert.False(running.IsCompleted);
        var other = await TypedFlowRunner.RunAsync(flow, Message, new Completion(Valid), TestContext.Current.CancellationToken);
        Assert.True(other.Success);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var next = await TypedFlowRunner.RunAsync(flow, Message, new Completion("{}"), TestContext.Current.CancellationToken);
        Assert.Empty(next.Outputs);
    }

    private sealed class Completion(string output) : IFlowCompletion
    {
        public int Count { get; private set; }
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) { Count++; return Task.FromResult(output); }
    }
    private sealed class WaitingCompletion(Task<string> output) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => output.WaitAsync(token);
    }
}
