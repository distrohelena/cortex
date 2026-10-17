using System.Text.Json;

namespace Cortex.Editor;

public sealed record EditorRegisteredGraph(
    string Reference,
    JsonElement Graph,
    string? EntryId,
    bool CanEdit,
    Func<JsonElement, CancellationToken, Task>? SaveAsync);

public sealed record EditorGraphNavigationFrame(
    EditorGraphSnapshot Caller,
    string? CallerReference,
    bool CallerCanEdit);

/// <summary>Host-owned allowlist for logical function references and caller context.</summary>
public sealed class EditorGraphNavigationRegistry
{
    private readonly Dictionary<string, EditorRegisteredGraph> _graphs = new(StringComparer.Ordinal);
    private readonly Stack<EditorGraphNavigationFrame> _callers = [];

    public string? CurrentReference { get; private set; }
    public bool CurrentCanEdit { get; private set; }
    public bool CanGoBack => _callers.Count > 0;
    public int Depth => _callers.Count;
    public bool CurrentReadOnly => CurrentReference is not null && !CurrentCanEdit;

    public void Register(string reference, JsonElement graph, string? entryId = null, bool canEdit = false,
        Func<JsonElement, CancellationToken, Task>? saveAsync = null)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A graph reference is required.", nameof(reference));
        if (canEdit && saveAsync is null) throw new ArgumentException("Editable function graphs require a host save callback.", nameof(saveAsync));
        var validated = new EditorGraphStore();
        validated.Replace(graph, entryId);
        var registered = new EditorRegisteredGraph(reference, validated.Graph, entryId, canEdit, saveAsync);
        if (!_graphs.TryAdd(reference, registered)) throw new ArgumentException("Graph reference is already registered: " + reference, nameof(reference));
    }

    public bool TryResolve(string reference, out EditorRegisteredGraph graph) => _graphs.TryGetValue(reference, out graph!);

    public bool TryGetCurrent(out EditorRegisteredGraph graph)
    {
        if (CurrentReference is not null && _graphs.TryGetValue(CurrentReference, out graph!)) return true;
        graph = null!;
        return false;
    }

    public void ResetNavigation()
    {
        _callers.Clear();
        CurrentReference = null;
        CurrentCanEdit = false;
    }

    public void PublishSavedGraph(string reference, JsonElement graph, string? entryId)
    {
        if (!_graphs.TryGetValue(reference, out var registered))
            throw new InvalidOperationException("Function graph reference is no longer registered.");
        if (!registered.CanEdit || registered.SaveAsync is null)
            throw new InvalidOperationException("Function graph is not registered for editing.");
        var validated = new EditorGraphStore();
        validated.Replace(graph, entryId);
        _graphs[reference] = registered with { Graph = validated.Graph, EntryId = entryId };
    }

    public void Navigate(EditorGraphSnapshot caller, EditorRegisteredGraph target)
    {
        _callers.Push(new(caller, CurrentReference, CurrentCanEdit));
        CurrentReference = target.Reference;
        CurrentCanEdit = target.CanEdit;
    }

    public EditorGraphNavigationFrame Return()
    {
        if (!_callers.TryPop(out var frame)) throw new InvalidOperationException("There is no caller graph to return to.");
        CurrentReference = frame.CallerReference;
        CurrentCanEdit = frame.CallerCanEdit;
        return frame;
    }
}
