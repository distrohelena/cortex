using System.Collections.Concurrent;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorValidation(bool Success, string? EntryId, IReadOnlyList<TypedFlowIssue> Issues);
public sealed record EditorRunSnapshot(string? Id, string State, JsonElement? Result, string? Error, IReadOnlyList<string> Diagnostics, string? NextNodeId = null);

public sealed class EditorExecutionService : IDisposable
{
    private readonly IReadOnlyList<NodeDefinition> _definitions;
    private readonly Func<IReadOnlyList<FlowNodeHandler>> _bindHandlers;
    private readonly ConcurrentQueue<string> _diagnostic;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;
    private EditorRunSnapshot _snapshot = new(null, "idle", null, null, []);
    private bool _busy;
    public EditorExecutionService(IReadOnlyList<NodeDefinition> definitions, Func<IReadOnlyList<FlowNodeHandler>> bindHandlers, ConcurrentQueue<string>? diagnostic = null)
    { _definitions = definitions; _bindHandlers = bindHandlers; _diagnostic = diagnostic ?? new(); }
    public EditorExecutionService(IReadOnlyList<FlowNodeHandler> handlers, ConcurrentQueue<string>? diagnostic = null)
        : this(handlers.Select(handler => handler.Definition).ToArray(), () => handlers, diagnostic) { }
    public bool Busy { get { lock (_sync) return _busy; } }
    public EditorRunSnapshot Snapshot() { lock (_sync) return _snapshot with { Diagnostics = _diagnostic.ToArray() }; }

    public string? SelectEntry(JsonElement graph, string? entryId)
    {
        if (!string.IsNullOrWhiteSpace(entryId)) return entryId;
        var entries = _definitions.Where(definition => definition.ExecutionMode == FlowNodeExecutionMode.Entry).Select(definition => definition.Kind).ToHashSet(StringComparer.Ordinal);
        return graph.GetProperty("nodes").EnumerateArray().FirstOrDefault(node =>
            node.TryGetProperty("kind", out var kind) && (kind.GetString() is "dm" or "group" || entries.Contains(kind.GetString()!)))
            is var entry && entry.ValueKind == JsonValueKind.Object ? entry.GetProperty("id").GetString() : null;
    }
    public EditorValidation Validate(JsonElement graph, string? entryId)
    {
        var entry = SelectEntry(graph, entryId);
        if (entry is null) return new(false, null, [new("graph", "entry", "Select an available entry node. Check the plugins in Settings.")]);
        try { _ = TypedFlowCompiler.Validate(graph, entry, _definitions); return new(true, entry, []); }
        catch (TypedFlowException error) { return new(false, entry, error.Issues); }
    }

    public async Task MutateAsync(Func<Task> mutation, CancellationToken token)
    {
        await _mutations.WaitAsync(token);
        try
        {
            if (Busy) throw new InvalidOperationException("Wait for the run to finish or cancel it before changing the graph or settings.");
            await mutation();
        }
        finally { _mutations.Release(); }
    }
    public async Task StartAsync(JsonElement graph, string? entryId, CancellationToken token)
    {
        await _mutations.WaitAsync(token);
        try
        {
            if (Busy) throw new InvalidOperationException("A run is already in progress.");
            var validation = Validate(graph, entryId);
            if (!validation.Success) throw new TypedFlowException(validation.Issues);
            var handlers = _bindHandlers();
            EnsureResolvedContractParity(graph, _definitions, handlers);
            var flow = TypedFlowCompiler.Compile(graph.Clone(), validation.EntryId!, handlers);
            lock (_sync)
            {
                _diagnostic.Clear();
                _cancellation?.Dispose();
                _cancellation = new();
                _snapshot = new(Guid.NewGuid().ToString("N"), "running", null, null, []);
                _busy = true;
                var runToken = _cancellation.Token;
                _task = Task.Run(() => RunAsync(flow, runToken), CancellationToken.None);
            }
        }
        finally { _mutations.Release(); }
    }
    internal static void EnsureResolvedContractParity(JsonElement graph, IReadOnlyList<NodeDefinition> definitions, IReadOnlyList<FlowNodeHandler> handlers)
    {
        var metadata = TypedFlowCompiler.Describe(graph, definitions);
        var runtime = TypedFlowCompiler.Describe(graph, handlers);
        bool SamePorts(IReadOnlyList<FlowNodePortDescription> left, IReadOnlyList<FlowNodePortDescription> right) =>
            left.Count == right.Count && left.Zip(right).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
                pair.First.Required == pair.Second.Required && NullableTypeEquals(pair.First.Type, pair.Second.Type) &&
                NullableJsonEquals(pair.First.ConstantValue, pair.Second.ConstantValue));
        foreach (var (id, expected) in metadata)
        {
            if (!runtime.TryGetValue(id, out var actual) || expected.ExecutionMode != actual.ExecutionMode || expected.Effect != actual.Effect ||
                expected.DisplayName != actual.DisplayName || expected.NavigableGraphReference != actual.NavigableGraphReference ||
                !SamePorts(expected.Inputs, actual.Inputs) || !SamePorts(expected.Outputs, actual.Outputs) ||
                !expected.ControlInputs.SequenceEqual(actual.ControlInputs) || !expected.ControlOutputs.SequenceEqual(actual.ControlOutputs))
                throw new InvalidDataException("Runtime handler contract differs from graph metadata for node " + id + ".");
        }
    }
    private static bool NullableJsonEquals(JsonElement? left, JsonElement? right) => left is null && right is null ||
        left is { } l && right is { } r && JsonElement.DeepEquals(l, r);
    private static bool NullableTypeEquals(FlowType? left, FlowType? right) => left is null && right is null ||
        left is not null && right is not null && JsonElement.DeepEquals(left.ToJsonSchema(), right.ToJsonSchema());
    private async Task RunAsync(TypedFlow flow, CancellationToken token)
    {
        try
        {
            var result = await TypedFlowRunner.RunAsync(flow, token);
            var output = SerializeResult(result);
            lock (_sync) _snapshot = _snapshot with { State = result.Success ? "completed" : "failed", Result = output };
        }
        catch (OperationCanceledException) { lock (_sync) _snapshot = _snapshot with { State = "cancelled", Error = "Run cancelled." }; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (_sync) _snapshot = _snapshot with { State = "failed", Error = error.Message }; }
        finally { lock (_sync) _busy = false; }
    }
    internal static JsonElement SerializeResult(TypedRunResult result) => JsonSerializer.SerializeToElement(new
            {
                result.Success, result.Steps,
                outputs = result.Outputs.ToDictionary(node => node.Key, node => node.Value.ToDictionary(port => port.Key,
                    port => new { schema = port.Value.Type.ToJsonSchema(), value = port.Value.Value }))
            }, CortexSettingsStore.JsonOptions);

    public void Cancel() { lock (_sync) _cancellation?.Cancel(); }
    public Task WaitAsync(CancellationToken token) { lock (_sync) return _task.WaitAsync(token); }
    public void Dispose() { Cancel(); _cancellation?.Dispose(); }
}
