using System.Collections.Concurrent;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorValidation(bool Success, string? EntryId, IReadOnlyList<TypedFlowIssue> Issues);
public sealed record EditorRunSnapshot(string? Id, string State, JsonElement? Result, string? Error, IReadOnlyList<string> Diagnostics);

public sealed class EditorExecutionService(IReadOnlyList<FlowNodeHandler> handlers, ConcurrentQueue<string>? diagnostic = null) : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly ConcurrentQueue<string> _diagnostic = diagnostic ?? new();
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;
    private EditorRunSnapshot _snapshot = new(null, "idle", null, null, []);
    private bool _busy;
    public bool Busy { get { lock (_sync) return _busy; } }
    public EditorRunSnapshot Snapshot() { lock (_sync) return _snapshot with { Diagnostics = _diagnostic.ToArray() }; }

    public string? SelectEntry(JsonElement graph, string? entryId)
    {
        if (!string.IsNullOrWhiteSpace(entryId)) return entryId;
        var entries = handlers.Where(handler => handler.IsEntry).Select(handler => handler.Definition.Kind).ToHashSet(StringComparer.Ordinal);
        return graph.GetProperty("nodes").EnumerateArray().FirstOrDefault(node =>
            node.TryGetProperty("kind", out var kind) && (kind.GetString() is "dm" or "group" || entries.Contains(kind.GetString()!)))
            is var entry && entry.ValueKind == JsonValueKind.Object ? entry.GetProperty("id").GetString() : null;
    }
    public EditorValidation Validate(JsonElement graph, string? entryId)
    {
        var entry = SelectEntry(graph, entryId);
        if (entry is null) return new(false, null, [new("graph", "entry", "Selecione uma entrada disponível. Confira os plugins nas Configurações.")]);
        try { _ = TypedFlowCompiler.Compile(graph, entry, handlers); return new(true, entry, []); }
        catch (TypedFlowException error) { return new(false, entry, error.Issues); }
    }

    public async Task MutateAsync(Func<Task> mutation, CancellationToken token)
    {
        await _mutations.WaitAsync(token);
        try
        {
            if (Busy) throw new InvalidOperationException("Aguarde ou cancele a execução antes de alterar o graph ou as settings.");
            await mutation();
        }
        finally { _mutations.Release(); }
    }
    public async Task StartAsync(JsonElement graph, string? entryId, CancellationToken token)
    {
        await _mutations.WaitAsync(token);
        try
        {
            if (Busy) throw new InvalidOperationException("Já existe uma execução em andamento.");
            var validation = Validate(graph, entryId);
            if (!validation.Success) throw new TypedFlowException(validation.Issues);
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
    private async Task RunAsync(TypedFlow flow, CancellationToken token)
    {
        try
        {
            var result = await TypedFlowRunner.RunAsync(flow, token);
            var output = JsonSerializer.SerializeToElement(new
            {
                result.Success, result.Steps,
                outputs = result.Outputs.ToDictionary(node => node.Key, node => node.Value.ToDictionary(port => port.Key,
                    port => new { schema = port.Value.Type.ToJsonSchema(), value = port.Value.Value }))
            }, CortexSettingsStore.JsonOptions);
            lock (_sync) _snapshot = _snapshot with { State = result.Success ? "completed" : "failed", Result = output };
        }
        catch (OperationCanceledException) { lock (_sync) _snapshot = _snapshot with { State = "cancelled", Error = "Execução cancelada." }; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (_sync) _snapshot = _snapshot with { State = "failed", Error = error.Message }; }
        finally { lock (_sync) _busy = false; }
    }
    public void Cancel() { lock (_sync) _cancellation?.Cancel(); }
    public Task WaitAsync(CancellationToken token) { lock (_sync) return _task.WaitAsync(token); }
    public void Dispose() { Cancel(); _cancellation?.Dispose(); }
}
