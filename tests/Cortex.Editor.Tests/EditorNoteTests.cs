using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Editor.Tests;

public sealed class EditorNoteTests
{
    static JsonElement Graph(string? wireKind = null)
    {
        var graph = JsonNode.Parse("""{"version":4,"nodes":[{"id":"input","kind":"fixture","x":10,"y":20,"settings":{}},{"id":"note","kind":"note","x":90,"y":120,"settings":{"text":"Primeira linha\n<img src=x onerror=alert(1)>"}}],"edges":[],"controlEdges":[]}""")!;
        if (wireKind is not null) graph[wireKind]!.AsArray().Add(JsonNode.Parse("""{"source":"input","sourcePort":"out","target":"note","targetPort":"in"}"""));
        return JsonSerializer.SerializeToElement(graph);
    }
    static FlowNodeHandler Entry(Action called) => new(new NodeDefinition("fixture", 1, FlowEffect.Pure), (_, _, _) =>
    {
        called();return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>());
    }, isEntry: true);

    [Fact]
    public async Task NoteIsSavedWithItsTextAndPositionButNeverExecuted()
    {
        var calls = 0;
        var graph = Graph();
        var flow = TypedFlowCompiler.Compile(graph, "input", [Entry(() => calls++)]);
        Assert.DoesNotContain("note", flow.Nodes.Keys);
        using var execution = new EditorExecutionService([Entry(() => calls++)]);
        await execution.StartAsync(graph, "input", TestContext.Current.CancellationToken);
        await execution.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("completed", execution.Snapshot().State);
        Assert.Equal(1, calls);
        var path = Path.Combine(Path.GetTempPath(), "cortex-note-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, graph.GetRawText(), TestContext.Current.CancellationToken);
            var store = new EditorGraphStore();
            await store.OpenFileAsync(path, "input", TestContext.Current.CancellationToken);
            await store.SaveAsync(TestContext.Current.CancellationToken);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.True(JsonElement.DeepEquals(graph, saved.RootElement));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("edges")]
    [InlineData("controlEdges")]
    public void NoteRejectsConnections(string wireKind)
    {
        var error = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(Graph(wireKind), "input", [Entry(() => { })]));
        Assert.Contains(error.Issues, issue => issue.NodeId == "note" && issue.Port == "ports");
    }

    [Fact]
    public void NoteIsAvailableWithoutPluginsAndHasAMultilineTextField()
    {
        var catalog = JsonSerializer.SerializeToElement(EditorCatalog.Available([]));
        var note = Assert.Single(catalog.EnumerateArray(), item => item.GetProperty("kind").GetString() == "note");
        Assert.Equal("Nota", note.GetProperty("title").GetString());
        Assert.Empty(note.GetProperty("ports").EnumerateArray());
        Assert.Empty(note.GetProperty("executionPorts").EnumerateArray());
        Assert.True(note.GetProperty("fields")[0].GetProperty("multiline").GetBoolean());
        var descriptions = JsonSerializer.SerializeToElement(EditorCatalog.Descriptions(Graph(), []));
        Assert.Equal("Nota", descriptions.GetProperty("note").GetProperty("title").GetString());
    }
}
