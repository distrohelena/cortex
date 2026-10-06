using System.Text.Json;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorGraphRequest(JsonElement Graph, string? EntryId = null);

public sealed record UseStepRequest(Dictionary<string, Dictionary<string, JsonElement>>? Parameters = null);

public sealed record UseReviewRequest(Dictionary<string,JsonElement> Outputs);
public sealed record UseToolRequest(Dictionary<string,JsonElement>? Parameters = null);

public static class EditorEndpoints
{
    public static void Map(WebApplication app, EditorRuntime runtime)
    {
        app.MapGet("/api/state", () => new
        {
            graph = runtime.Graph.Graph, entryId = runtime.Use?.EntryId ?? runtime.Graph.EntryId, filePath = runtime.Graph.FilePath,
            catalog = EditorCatalog.Available(runtime.Plugins.Handlers, runtime.Plugins.LibrariesByKind), descriptions = EditorCatalog.Descriptions(runtime.Graph.Graph, runtime.Plugins.Handlers, runtime.Plugins.LibrariesByKind),
            runtime.SettingsError, runtime.StartupError, runtime.PendingRestart, busy = runtime.Use?.Busy ?? runtime.Execution.Busy,
            useMode = runtime.UseMode, usageFields = runtime.Use?.Fields, reviewOutputs = runtime.Use?.ReviewOutputs, modelChoices = runtime.Use?.Models, runState = runtime.Use?.Snapshot(),
            tools = runtime.Use?.Tools, toolRuns = runtime.Use?.ToolRuns,
            plugins = runtime.Plugins.Reports
        });
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
        { await runtime.Execution.MutateAsync(() => { runtime.Graph.Open(request.Graph, request.EntryId); return Task.CompletedTask; }, token); return new { success = true }; });
        app.MapPut("/api/graph", async (EditorGraphRequest request, CancellationToken token) =>
        { await runtime.Execution.MutateAsync(() => { runtime.Graph.Replace(request.Graph, request.EntryId); return Task.CompletedTask; }, token); return new { success = true }; });
        app.MapPost("/api/graph/save", async (CancellationToken token) =>
        {
            if (runtime.Graph.FilePath is null) return Results.Json(runtime.Graph.Graph);
            await runtime.Execution.MutateAsync(() => runtime.Graph.SaveAsync(token), token);
            return Results.Json(new { saved = true, path = runtime.Graph.FilePath });
        });
        app.MapPost("/api/validate", () => runtime.Execution.Validate(runtime.Graph.Graph, runtime.Graph.EntryId));
        app.MapPost("/api/run", async (CancellationToken token) =>
        { await runtime.Execution.StartAsync(runtime.Graph.Graph, runtime.Graph.EntryId, token); return runtime.Execution.Snapshot(); });
        app.MapGet("/api/run", () => runtime.Use?.Snapshot() ?? runtime.Execution.Snapshot());
        app.MapPost("/api/run/cancel", () => { if(runtime.Use is not null) { runtime.Use.Cancel(); return runtime.Use.Snapshot(); } runtime.Execution.Cancel(); return runtime.Execution.Snapshot(); });
    }
}
