using System.Text.Json;

namespace Cortex.Core.Flows.Sessions;

internal sealed record CompiledSessionFlowState(
    string Id,
    InstructionContext InstructionContext,
    IReadOnlyDictionary<SessionEntryKind, TypedFlow> Entries);

internal sealed record CompiledSessionFlowProject(
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    string InitialStateId,
    JsonElement Document,
    IReadOnlyDictionary<string, CompiledSessionFlowState> States,
    IReadOnlyList<string> StateIds)
{
    public SessionFlowProjectSnapshot Snapshot() =>
        new(ProjectId, ProjectVersion, ProjectRevision, InitialStateId, StateIds);
}

internal static class SessionFlowProjectCompiler
{
    private static readonly HashSet<string> SupportedKinds =
        ["dm", "group", "accumulate", "history", "memory", "interpretBoundaries", "prompt", "property", "count", "switch", "branch", "exit", "send"];

    public static CompiledSessionFlowProject Compile(SessionFlowProject source)
    {
        var issues = new List<SessionFlowProjectIssue>();
        void Issue(string stateId, string? entryId, string nodeId, string port, string reason) =>
            issues.Add(new(stateId, entryId, nodeId, port, reason));

        if (string.IsNullOrWhiteSpace(source.ProjectId)) Issue("project", null, "project", "projectId", "ProjectId is required.");
        if (source.ProjectVersion < 0) Issue("project", null, "project", "projectVersion", "ProjectVersion cannot be negative.");
        if (string.IsNullOrWhiteSpace(source.ProjectRevision)) Issue("project", null, "project", "projectRevision", "ProjectRevision is required.");

        try
        {
            var document = source.Document.Clone();
            var project = Unwrap(document);
            var personalityCore = InstructionContext.ReadText(project, "personalityCore");
            var interestProfile = InstructionContext.ReadText(project, "interestProfile");
            if (!project.TryGetProperty("version", out var schemaVersion) || !schemaVersion.TryGetInt32(out var version) || version != 1)
                Issue("project", null, "project", "version", "Use project schema version 1.");
            if (!project.TryGetProperty("stateScope", out var scope) || scope.ValueKind != JsonValueKind.String || scope.GetString() != "session")
                Issue("project", null, "project", "stateScope", "Only session state scope is executable.");
            if (!project.TryGetProperty("initialStateId", out var initialValue) || initialValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(initialValue.GetString()))
                Issue("project", null, "project", "initialStateId", "initialStateId is required.");
            if (!project.TryGetProperty("states", out var stateArray) || stateArray.ValueKind != JsonValueKind.Array || stateArray.GetArrayLength() is < 1 or > 16)
                Issue("project", null, "project", "states", "Use 1-16 states.");
            if (issues.Count > 0) throw new SessionFlowProjectException(issues);

            var initialStateId = initialValue.GetString()!;
            var stateElements = stateArray.EnumerateArray().ToArray();
            var stateIds = new List<string>(stateElements.Length);
            var stateById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var state in stateElements)
            {
                var id = RequiredText(state, "id");
                if (!stateById.TryAdd(id, state)) Issue(id, null, "state", "id", "State IDs must be unique.");
                else stateIds.Add(id);
            }
            if (!stateById.ContainsKey(initialStateId)) Issue("project", null, "project", "initialStateId", "Initial state does not exist.");

            var compiledStates = new Dictionary<string, CompiledSessionFlowState>(StringComparer.Ordinal);
            foreach (var (stateId, state) in stateById)
            {
                if (!state.TryGetProperty("graph", out var graph) || graph.ValueKind != JsonValueKind.Object)
                {
                    Issue(stateId, null, "graph", "config", "State graph is required.");
                    continue;
                }
                if (!graph.TryGetProperty("version", out var graphVersion) || !graphVersion.TryGetInt32(out var graphVersionNumber) || graphVersionNumber != 3)
                {
                    Issue(stateId, null, "graph", "version", "Executable state graphs must use version 3.");
                    continue;
                }
                if (!graph.TryGetProperty("nodes", out var nodeArray) || nodeArray.ValueKind != JsonValueKind.Array)
                {
                    Issue(stateId, null, "graph", "nodes", "Graph nodes are required.");
                    continue;
                }

                var nodes = nodeArray.EnumerateArray().ToArray();
                foreach (var node in nodes)
                {
                    var nodeId = RequiredText(node, "id");
                    var kind = RequiredText(node, "kind");
                    if (!SupportedKinds.Contains(kind)) Issue(stateId, null, nodeId, "kind", "Node is not executable in this slice: " + kind);
                }
                var entries = nodes.Where(node => Text(node, "kind") is "dm" or "group").ToArray();
                if (entries.Length == 0) Issue(stateId, null, "graph", "entry", "State needs a DM or group entry.");
                foreach (var duplicate in entries.GroupBy(node => Text(node, "kind")).Where(group => group.Count() > 1))
                    Issue(stateId, null, "graph", "entry", "Only one " + duplicate.Key + " entry is allowed per state.");

                var compiledEntries = new Dictionary<SessionEntryKind, TypedFlow>();
                foreach (var entry in entries)
                {
                    var entryId = RequiredText(entry, "id");
                    try
                    {
                        var flow = TypedFlowCompiler.Compile(graph, entryId);
                        ValidateAccumulatorPosition(stateId, entryId, flow, Issue);
                        compiledEntries[Text(entry, "kind") == "dm" ? SessionEntryKind.Dm : SessionEntryKind.Group] = flow;
                    }
                    catch (TypedFlowException exception)
                    {
                        foreach (var issue in exception.Issues) Issue(stateId, entryId, issue.NodeId, issue.Port, issue.Reason);
                    }
                }
                compiledStates[stateId] = new(stateId,
                    new(personalityCore, InstructionContext.ReadText(state, "participationGuidance"), interestProfile), compiledEntries);
            }

            ValidateStateTargets(compiledStates.Values, stateById.Keys.ToHashSet(StringComparer.Ordinal), Issue);
            if (issues.Count > 0) throw new SessionFlowProjectException(issues);
            return new(source.ProjectId, source.ProjectVersion, source.ProjectRevision, initialStateId, document,
                compiledStates, stateIds.AsReadOnly());
        }
        catch (SessionFlowProjectException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException or InvalidDataException)
        {
            Issue("project", null, "project", "document", exception.Message);
            throw new SessionFlowProjectException(issues);
        }
    }

    private static JsonElement Unwrap(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Project document must be an object.");
        if (!document.TryGetProperty("project", out var project)) return document;
        if (!document.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "filomena-local-project")
            throw new InvalidDataException("Project wrapper format is invalid.");
        return project;
    }

    private static void ValidateAccumulatorPosition(
        string stateId,
        string entryId,
        TypedFlow flow,
        Action<string, string?, string, string, string> issue)
    {
        var accumulators = flow.Nodes.Values.Where(node => node.Source.Kind == "accumulate").ToArray();
        if (accumulators.Length > 1)
        {
            issue(stateId, entryId, entryId, "then", "Each selected entry path may contain at most one accumulator.");
            return;
        }
        if (accumulators.Length == 0) return;
        var accumulator = accumulators[0];
        if (!flow.Graph.ControlEdges.Any(edge => edge.Source == new PortRef(entryId, "then") && edge.Target == new PortRef(accumulator.Source.Id, "exec")) ||
            !flow.Graph.DataEdges.Any(edge => edge.Source == new PortRef(entryId, "message") && edge.Target == new PortRef(accumulator.Source.Id, "message")))
            issue(stateId, entryId, accumulator.Source.Id, "exec", "Accumulator must directly receive the selected entry message and execution.");
    }

    private static void ValidateStateTargets(
        IEnumerable<CompiledSessionFlowState> states,
        HashSet<string> stateIds,
        Action<string, string?, string, string, string> issue)
    {
        foreach (var state in states)
            foreach (var (entryKind, flow) in state.Entries)
                foreach (var node in flow.Nodes.Values.Where(item => item.Source.Kind == "exit"))
                {
                    var settings = node.Source.Config.GetProperty("settings");
                    if (settings.TryGetProperty("targetStateId", out var target) && target.ValueKind == JsonValueKind.String)
                    {
                        if (!stateIds.Contains(target.GetString()!)) issue(state.Id, flow.EntryId, node.Source.Id, "targetStateId", "Target state does not exist.");
                        continue;
                    }
                    foreach (var mapping in settings.GetProperty("stateMapping").EnumerateObject())
                        if (!stateIds.Contains(mapping.Value.GetString()!)) issue(state.Id, flow.EntryId, node.Source.Id, "stateMapping", "Mapped target state does not exist for " + mapping.Name + ".");
                }
    }

    private static string RequiredText(JsonElement value, string key) =>
        Text(value, key) is { Length: > 0 } text ? text : throw new InvalidDataException(key + " is required.");

    private static string? Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
