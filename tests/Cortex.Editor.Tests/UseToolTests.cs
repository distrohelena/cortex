using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Editor.Tests;

public sealed class UseToolTests
{
    [Fact]
    public async Task ToolArtifactsAreOnlyServedFromTheLockedGeneratedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-tool-artifacts-" + Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "generated"); Directory.CreateDirectory(allowed);
        try
        {
            var path = Path.Combine(allowed, "preview.mp4"); await File.WriteAllTextAsync(path, "generated", TestContext.Current.CancellationToken);
            var privatePath = Path.Combine(root, "private.mp4"); await File.WriteAllTextAsync(privatePath, "private", TestContext.Current.CancellationToken);
            var returnedPath = path;
            var handler = new FlowNodeHandler(new NodeDefinition("artifact", 1, FlowEffect.WriteState,
                dataOutputs: [new("video", new ObjectType(new Dictionary<string, FlowField> { ["path"] = new(new StringType(), true) }))],
                controlOutputs: [new("then")], configFields: [new("outputDirectory", new StringType(), Required: true)]), (_, _, _) =>
                Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["video"] = JsonSerializer.SerializeToElement(new { path = returnedPath }) }), true);
            var graph = JsonSerializer.SerializeToElement(new { version = 3,
                usage = new { tools = new { preview = new { entryId = "p", afterNode = "p", parameters = Array.Empty<string>(), artifactPort = "video", artifactDirectorySetting = "outputDirectory" } } },
                nodes = new[] { new { id = "p", kind = "artifact", settings = new { outputDirectory = allowed } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            using var tools = new EditorUseTools(graph, [handler]);
            Assert.Null(tools.Artifact("preview"));
            await tools.StartAsync("preview", "p", [], TestContext.Current.CancellationToken); await tools.WaitAsync("preview", TestContext.Current.CancellationToken);
            Assert.Equal(path, tools.Artifact("preview")!.Path);
            returnedPath = privatePath;
            await tools.StartAsync("preview", "p", [], TestContext.Current.CancellationToken); await tools.WaitAsync("preview", TestContext.Current.CancellationToken);
            Assert.Null(tools.Artifact("preview"));
            tools.Reset(); Assert.Null(tools.Artifact("preview"));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task RepeatedToolRunsKeepTheMainReviewPausedAndCannotChangeLockedSettings()
    {
        var calls = new List<string>();
        var main = new FlowNodeHandler(new NodeDefinition("input", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new StringType())], controlOutputs: [new("then")],
            configFields: [new("text", new StringType(), Required: true)]), (config, _, _) =>
            { calls.Add("main"); return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["value"] = config.GetProperty("text").Clone() }); }, true);
        var tool = new FlowNodeHandler(new NodeDefinition("tool", 1, FlowEffect.WriteState,
            dataOutputs: [new("value", new StringType())], controlOutputs: [new("then")],
            configFields: [new("text", new StringType(), Required: true), new("private", new StringType())]), (config, _, _) =>
            { calls.Add("tool"); return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["value"] = config.GetProperty("text").Clone() }); }, true);
        var tail = new FlowNodeHandler(new NodeDefinition("tail", 1, FlowEffect.Pure,
            dataInputs: [new("value", new StringType(), Required: true)], dataOutputs: [new("value", new StringType())], controlInputs: [new("exec")], controlOutputs: [new("then")]),
            (_, inputs, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement> { ["value"] = inputs["value"].Value }));
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            usage = new { reviewOutputs = new Dictionary<string, string[]> { ["a"] = ["value"] }, tools = new { preview = new { entryId = "p", afterNode = "a", parameters = new[] { "text" } } } },
            nodes = new object[] { new { id = "a", kind = "input", settings = new { text = "original" } }, new { id = "b", kind = "tail" }, new { id = "p", kind = "tool", settings = new { text = "original", @private = "locked" } } },
            edges = new[] { new { source = "a", sourcePort = "value", target = "b", targetPort = "value" } },
            controlEdges = new[] { new { source = "a", sourcePort = "then", target = "b", targetPort = "exec" } }
        });
        using var service = new EditorUseService(graph, "a", [main, tail, tool]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunToolAsync("preview", new() { ["text"] = JsonSerializer.SerializeToElement("one") }, TestContext.Current.CancellationToken));
        await service.AdvanceAsync([], TestContext.Current.CancellationToken); await service.WaitAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
        {
            await service.RunToolAsync("preview", new() { ["text"] = JsonSerializer.SerializeToElement("preview " + i) }, TestContext.Current.CancellationToken);
            await service.WaitToolAsync("preview", TestContext.Current.CancellationToken);
            Assert.Equal("completed", service.ToolSnapshot("preview").State);
            Assert.Equal("paused", service.Snapshot().State);
            Assert.Equal("b", service.Snapshot().NextNodeId);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunToolAsync("preview", new() { ["private"] = JsonSerializer.SerializeToElement("changed") }, TestContext.Current.CancellationToken));
        await service.ReviewAsync(new() { ["value"] = JsonSerializer.SerializeToElement("human") }, TestContext.Current.CancellationToken);
        await service.AdvanceAsync([], TestContext.Current.CancellationToken); await service.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("human", service.Snapshot().Result!.Value.GetProperty("outputs").GetProperty("b").GetProperty("value").GetProperty("value").GetString());
        Assert.Equal(new[] { "main", "tool", "tool" }, calls);
        Assert.Equal("locked", graph.GetProperty("nodes")[2].GetProperty("settings").GetProperty("private").GetString());
    }
}
