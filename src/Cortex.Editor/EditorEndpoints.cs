using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorGraphRequest(JsonElement Graph, string? EntryId = null);
public sealed record EditorGraphNavigateRequest(JsonElement Graph, string? EntryId, string NodeId, string Reference);
public sealed record EditorConnectRequest(JsonElement Graph, string Kind, string Source, string SourcePort, string Target, string TargetPort);

public sealed record UseStepRequest(Dictionary<string, Dictionary<string, JsonElement>>? Parameters = null);

public sealed record UseReviewRequest(Dictionary<string,JsonElement> Outputs);
public sealed record UseToolRequest(Dictionary<string,JsonElement>? Parameters = null);

public static class EditorEndpoints
{
    public static void Map(WebApplication app, EditorRuntime runtime)
    {
        app.MapGet("/api/state", () => State(runtime));
        app.MapPost("/api/use/step", async (UseStepRequest request, CancellationToken token) =>
        { await runtime.Use!.AdvanceAsync(request.Parameters ?? [], token); return runtime.Use.Snapshot(); });
        app.MapPost("/api/use/review", async (UseReviewRequest request, CancellationToken token) =>
        { await runtime.Use!.ReviewAsync(request.Outputs, token); return runtime.Use.Snapshot(); });
        app.MapPost("/api/use/reset", async (CancellationToken token) =>
        { await runtime.Use!.ResetAsync(token); return runtime.Use.Snapshot(); });
        app.MapPost("/api/use/tools/{id}", async (string id, UseToolRequest request, CancellationToken token) =>
        { var use=runtime.Use!; await use.RunToolAsync(id,request.Parameters??[],token); return use.ToolSnapshot(id); });
        app.MapGet("/api/use/tools/{id}", (string id) => runtime.Use!.ToolSnapshot(id));
        app.MapGet("/api/use/tools/{id}/artifact", (string id) =>
        { var artifact=runtime.Use!.ToolArtifact(id); return artifact is null ? Results.NotFound() : Results.File(artifact.Path,artifact.ContentType,enableRangeProcessing:true); });
        app.MapGet("/api/settings", () => new { settings = runtime.SavedSettings, manifests = runtime.Manifests, reports = runtime.Plugins.Reports, settingsPath = runtime.SettingsStore.FilePath, runtime.SettingsError, runtime.PendingRestart });
        app.MapPut("/api/settings", async (CortexEditorSettings settings, CancellationToken token) =>
        {
            await runtime.Execution.MutateAsync(async () =>
            {
                settings.Validate();
                // Preserve unknown/root options from the current settings if the UI omits them.
                var missing = runtime.SavedSettings.Plugins.Where(old => settings.Plugins.All(current => current.Id != old.Id));
                settings = settings with { Plugins = settings.Plugins.Concat(missing).ToArray(), Extra = settings.Extra ?? runtime.SavedSettings.Extra };
                await runtime.SettingsStore.SaveAsync(settings, token);
                runtime.SavedSettings = settings;
                runtime.SettingsError = null;
            }, token);
            return new { runtime.PendingRestart };
        });
        app.MapPost("/api/graph/open", async (EditorGraphRequest request, CancellationToken token) =>
        { if(runtime.ReadOnly)return Results.Json(new { error = "This graph is read-only." },statusCode:403); await runtime.Execution.MutateAsync(() => { runtime.Graph.Open(request.Graph, request.EntryId); runtime.Navigation.ResetNavigation(); return Task.CompletedTask; }, token); return Results.Json(new { success = true }); });
        app.MapPut("/api/graph", async (EditorGraphRequest request, CancellationToken token) =>
        { if(runtime.ReadOnly)return Results.Json(new { error = "This graph is read-only." },statusCode:403); await runtime.Execution.MutateAsync(() => { runtime.Graph.Replace(request.Graph, request.EntryId); return Task.CompletedTask; }, token); return Results.Json(new { success = true }); });
        app.MapPost("/api/graph/save", async (CancellationToken token) =>
        {
            if (runtime.ReadOnly) return Results.Json(new { error = "This graph is read-only." }, statusCode: 403);
            if (runtime.Navigation.TryGetCurrent(out var registered) && registered.CanEdit && registered.SaveAsync is not null)
            {
                var savedGraph = runtime.Graph.Graph.Clone();
                var savedEntryId = runtime.Graph.EntryId;
                var savedReference = registered.Reference;
                await registered.SaveAsync(savedGraph, token);
                runtime.Navigation.PublishSavedGraph(savedReference, savedGraph, savedEntryId);
                return Results.Json(new { saved = true, reference = savedReference });
            }
            if (runtime.Graph.FilePath is null) return Results.Json(runtime.Graph.Graph);
            await runtime.Execution.MutateAsync(() => runtime.Graph.SaveAsync(token), token);
            return Results.Json(new { saved = true, path = runtime.Graph.FilePath });
        });
        app.MapPost("/api/graph/navigate", (EditorGraphNavigateRequest request) => Navigate(runtime, request));
        app.MapPost("/api/graph/back", () => Return(runtime));
        app.MapPost("/api/validate", () => runtime.Execution.Validate(runtime.Graph.Graph, runtime.Graph.EntryId));
        app.MapPost("/api/graph/describe", (EditorGraphRequest request) => EditorCatalog.DescriptionsDefinitions(request.Graph, runtime.Plugins.Definitions, runtime.Plugins.LibrariesByKind, reference => runtime.Navigation.TryResolve(reference, out _)));
        app.MapPost("/api/graph/validate-connection", (EditorConnectRequest request) => ValidateConnection(request, runtime.Plugins.Definitions));
        app.MapPost("/api/run", async (CancellationToken token) =>
        { if(runtime.ReadOnly)return Results.Json(new { error = "This graph is read-only." },statusCode:403); await runtime.Execution.StartAsync(runtime.Graph.Graph, runtime.Graph.EntryId, token); return Results.Json(runtime.Execution.Snapshot()); });
        app.MapGet("/api/run", () => runtime.Use?.Snapshot() ?? runtime.Execution.Snapshot());
        app.MapPost("/api/run/cancel", () => { if(runtime.Use is not null) { runtime.Use.Cancel(); return runtime.Use.Snapshot(); } runtime.Execution.Cancel(); return runtime.Execution.Snapshot(); });
    }

    private static object State(EditorRuntime runtime) => new
    {
        graph = runtime.Graph.Graph,
        entryId = runtime.UseMode && runtime.Navigation.Depth == 0 && runtime.Use is not null ? runtime.Use.EntryId : runtime.Graph.EntryId,
        filePath = runtime.Graph.FilePath,
        catalog = EditorCatalog.AvailableDefinitions(runtime.Plugins.Definitions, runtime.Plugins.LibrariesByKind, reference => runtime.Navigation.TryResolve(reference, out _)),
        descriptions = EditorCatalog.DescriptionsDefinitions(runtime.Graph.Graph, runtime.Plugins.Definitions, runtime.Plugins.LibrariesByKind, reference => runtime.Navigation.TryResolve(reference, out _)),
        runtime.SettingsError, runtime.StartupError, runtime.PendingRestart, busy = runtime.Use?.Busy ?? runtime.Execution.Busy,
        useMode = runtime.UseMode, readOnly = runtime.ReadOnly, canGoBack = runtime.Navigation.CanGoBack, navigationDepth = runtime.Navigation.Depth,
        usageFields = runtime.Use?.Fields, reviewOutputs = runtime.Use?.ReviewOutputs, modelChoices = runtime.Use?.Models, runState = runtime.Use?.Snapshot(),
        tools = runtime.Use?.Tools, toolRuns = runtime.Use?.ToolRuns, plugins = runtime.Plugins.Reports
    };

    private static IResult Navigate(EditorRuntime runtime, EditorGraphNavigateRequest request)
    {
        if (!runtime.Navigation.TryResolve(request.Reference, out var target))
            return Results.BadRequest(new { error = "Function graph reference is not registered by this host." });
        EditorGraphSnapshot? trustedReadonlyCaller = null;
        if (runtime.ReadOnly)
        {
            if (runtime.Navigation.TryGetCurrent(out var registeredCaller))
                trustedReadonlyCaller = new(registeredCaller.Graph.Clone(), registeredCaller.EntryId, runtime.Graph.FilePath);
            else if (runtime.UseMode && runtime.Use is not null && runtime.Navigation.CurrentReference is null)
                trustedReadonlyCaller = runtime.Graph.Capture();
            else
                return Results.Conflict(new { error = "The read-only caller graph is not available from the host." });
        }
        var callerGraph = trustedReadonlyCaller is not null ? trustedReadonlyCaller.Graph : request.Graph;
        var callerEntryId = trustedReadonlyCaller is not null ? trustedReadonlyCaller.EntryId : request.EntryId;
        var descriptions = TypedFlowCompiler.Describe(callerGraph, runtime.Plugins.Definitions);
        if (!descriptions.TryGetValue(request.NodeId, out var caller) || caller.NavigableGraphReference != request.Reference)
            return Results.BadRequest(new { error = "The selected call does not declare this registered function graph reference." });
        var callerFilePath = runtime.Graph.FilePath;
        EditorGraphSnapshot snapshot;
        if (trustedReadonlyCaller is not null)
            snapshot = trustedReadonlyCaller with { FilePath = callerFilePath };
        else
        {
            runtime.Graph.Replace(callerGraph, callerEntryId);
            snapshot = runtime.Graph.Capture() with { FilePath = callerFilePath };
        }
        runtime.Navigation.Navigate(snapshot, target);
        runtime.Graph.Open(target.Graph, target.EntryId);
        return Results.Json(State(runtime));
    }

    private static IResult Return(EditorRuntime runtime)
    {
        if (!runtime.Navigation.CanGoBack) return Results.Conflict(new { error = "There is no caller graph to return to." });
        var frame = runtime.Navigation.Return();
        runtime.Graph.Restore(frame.Caller);
        return Results.Json(State(runtime));
    }

    private static IResult ValidateConnection(EditorConnectRequest request, IReadOnlyList<NodeDefinition> definitions)
    {
        var candidateGraph = WithCandidateConnection(request);
        var descriptions = TypedFlowCompiler.Describe(candidateGraph, definitions);
        if (!descriptions.TryGetValue(request.Source, out var source) || !descriptions.TryGetValue(request.Target, out var target))
            return Results.BadRequest(new { error = "Select two available nodes before connecting ports." });
        if (request.Kind == "execution")
        {
            var hasOutput = source.ControlOutputs.Any(port => port.Name == request.SourcePort);
            var hasInput = target.ControlInputs.Any(port => port.Name == request.TargetPort);
            return hasOutput && hasInput ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Execution port is unavailable for this node." });
        }
        if (request.Kind != "data") return Results.BadRequest(new { error = "Connection kind is invalid." });
        var output = source.Outputs.FirstOrDefault(port => port.Id == request.SourcePort);
        var input = target.Inputs.FirstOrDefault(port => port.Id == request.TargetPort);
        if (output is null || input is null) return Results.BadRequest(new { error = "Data port is unavailable for this node configuration." });
        if (output.Type is null || input.Type is null) return Results.BadRequest(new { error = "Resolve the node configuration before connecting this port." });
        return output.Type.IsCompatibleWith(input.Type)
            ? Results.Ok(new { success = true })
            : Results.BadRequest(new { error = "Data port types are incompatible." });
    }

    private static JsonElement WithCandidateConnection(EditorConnectRequest request)
    {
        var graph = JsonNode.Parse(request.Graph.GetRawText())?.AsObject() ?? throw new InvalidDataException("Graph document is invalid.");
        var key = request.Kind == "execution" ? "controlEdges" : "edges";
        if (graph[key] is not JsonArray edges) graph[key] = edges = new JsonArray();
        for (var index = edges.Count - 1; index >= 0; index--)
        {
            var edge = edges[index];
            if (edge is not JsonObject candidate) continue;
            if (candidate["target"]?.GetValue<string>() == request.Target && candidate["targetPort"]?.GetValue<string>() == request.TargetPort)
                edges.RemoveAt(index);
        }
        edges.Add(new JsonObject
        {
            ["source"] = request.Source, ["sourcePort"] = request.SourcePort,
            ["target"] = request.Target, ["targetPort"] = request.TargetPort
        });
        return JsonSerializer.SerializeToElement(graph);
    }
}
