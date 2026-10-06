using System.Text.Json;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorGraphRequest(JsonElement Graph, string? EntryId = null);

public static class EditorEndpoints
{
    public static void Map(WebApplication app, EditorRuntime runtime)
    {
        app.MapGet("/api/state", () => new
        {
            graph = runtime.Graph.Graph, entryId = runtime.Graph.EntryId, filePath = runtime.Graph.FilePath,
            catalog = EditorCatalog.Available(runtime.Plugins.Handlers), descriptions = EditorCatalog.Descriptions(runtime.Graph.Graph, runtime.Plugins.Handlers),
            runtime.SettingsError, runtime.StartupError, runtime.PendingRestart, busy = runtime.Execution.Busy,
            plugins = runtime.Plugins.Reports
        });
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
        app.MapGet("/api/run", () => runtime.Execution.Snapshot());
        app.MapPost("/api/run/cancel", () => { runtime.Execution.Cancel(); return runtime.Execution.Snapshot(); });
    }
}
