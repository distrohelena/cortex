using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Editor.Tests;

public sealed class EditorRuntimeTests
{
    [Fact]
    public async Task UnavailableNodesAndEdgesSurviveOpenAndSave()
    {
        var path = Path.Combine(Path.GetTempPath(), "cortex-graph-" + Guid.NewGuid() + ".json");
        var graph = Graph("missingPlugin");
        await File.WriteAllTextAsync(path, graph.GetRawText(), TestContext.Current.CancellationToken);
        var store = new EditorGraphStore();
        await store.OpenFileAsync(path, "input", TestContext.Current.CancellationToken);
        using var execution = new EditorExecutionService([]);
        Assert.False(execution.Validate(store.Graph, store.EntryId).Success);
        await store.SaveAsync(TestContext.Current.CancellationToken);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(graph, saved.RootElement));
    }

    [Fact]
    public async Task CancelReleasesBusyStateAndPreventsMutationDuringRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Handler(async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return "never"; });
        using var execution = new EditorExecutionService([handler]);
        await execution.StartAsync(Graph("fixture"), "input", TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.MutateAsync(() => Task.CompletedTask, TestContext.Current.CancellationToken));
        execution.Cancel();
        await execution.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(execution.Busy);
        Assert.Equal("cancelled", execution.Snapshot().State);
        await execution.MutateAsync(() => Task.CompletedTask, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailureDoesNotRetryAndReleasesBusyState()
    {
        var calls = 0;
        using var execution = new EditorExecutionService([Handler(_ => { calls++; throw new InvalidDataException("fixture failure"); })]);
        await execution.StartAsync(Graph("fixture"), "input", TestContext.Current.CancellationToken);
        await execution.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        Assert.False(execution.Busy);
        Assert.Equal("failed", execution.Snapshot().State);
        Assert.False(execution.Snapshot().Result!.Value.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task SuccessfulResultContainsTypedSchemaAndValue()
    {
        using var execution = new EditorExecutionService([Handler(_ => Task.FromResult("real-value"))]);
        await execution.StartAsync(Graph("fixture"), "input", TestContext.Current.CancellationToken);
        await execution.WaitAsync(TestContext.Current.CancellationToken);
        var result = execution.Snapshot().Result!.Value;
        Assert.Equal("completed", execution.Snapshot().State);
        Assert.Equal("real-value", result.GetProperty("outputs").GetProperty("input").GetProperty("value").GetProperty("value").GetString());
    }

    internal static JsonElement Graph(string kind) => JsonSerializer.SerializeToElement(new
    {
        version = 3, nodes = new[] { new { id = "input", kind, x = 30, y = 40, settings = new { original = "<img src=x onerror=alert(1)>" } } },
        edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
    });
    private static FlowNodeHandler Handler(Func<CancellationToken, Task<string>> execute) => new(new NodeDefinition("fixture", 1, FlowEffect.Pure,
        dataOutputs: [new("value", new StringType())]), async (_, _, token) =>
        new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(await execute(token)) }, isEntry: true);
}
