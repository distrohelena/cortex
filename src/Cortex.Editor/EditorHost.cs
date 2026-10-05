using System.Collections.Concurrent;
using System.Net;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorLaunchOptions(string? GraphPath = null, string? EntryId = null, string? SettingsPath = null,
    string? InstallationDirectory = null, int Port = 0, bool OpenBrowser = true, bool Diagnostics = false);

public sealed class EditorRuntime
{
    public required EditorGraphStore Graph { get; init; }
    public required CortexSettingsStore SettingsStore { get; init; }
    public required CortexEditorSettings SavedSettings { get; set; }
    public required CortexEditorSettings ActiveSettings { get; init; }
    public required IReadOnlyList<NodePluginManifest> Manifests { get; init; }
    public required LoadedNodePlugins Plugins { get; init; }
    public required EditorExecutionService Execution { get; init; }
    public string? SettingsError { get; set; }
    public string? StartupError { get; set; }
    public bool PendingRestart => !System.Text.Json.JsonElement.DeepEquals(
        System.Text.Json.JsonSerializer.SerializeToElement(SavedSettings, CortexSettingsStore.JsonOptions),
        System.Text.Json.JsonSerializer.SerializeToElement(ActiveSettings, CortexSettingsStore.JsonOptions));
}

public static class EditorHost
{
    public static async Task<WebApplication> BuildAsync(EditorLaunchOptions options, CancellationToken token = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"), Args = [] });
        if (options.Port is < 0 or > 65535) throw new ArgumentException("Porta inválida.");
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, options.Port));
        if (options.Diagnostics) builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json =>
        { json.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase; });
        var store = new CortexSettingsStore(options.SettingsPath);
        var read = await store.ReadAsync(token);
        var installation = options.InstallationDirectory is null ? CortexEditorInstallation.Read() : null;
        var manifestDirectory = options.InstallationDirectory is null ? installation?.PluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins") : Path.Combine(Path.GetFullPath(options.InstallationDirectory), "plugins");
        IReadOnlyList<NodePluginManifest> manifests;
        string? startupError = null;
        try { manifests = NodePluginManifest.ReadDirectory(manifestDirectory); }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
        { manifests = []; startupError = "Não foi possível ler os manifestos: " + error.Message; }
        var settings = read.Settings.WithInstalled(manifests);
        var diagnostic = new ConcurrentQueue<string>();
        var plugins = NodePluginLoader.Load(settings, manifests, store.DirectoryPath, line =>
        {
            diagnostic.Enqueue(line.Length > 65536 ? line[..65536] : line);
            while (diagnostic.Count > 128) diagnostic.TryDequeue(out _);
        });
        var graph = new EditorGraphStore();
        if (options.GraphPath is not null)
            try { await graph.OpenFileAsync(options.GraphPath, options.EntryId, token); }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { startupError = error.Message; }
        var runtime = new EditorRuntime
        {
            Graph = graph, SettingsStore = store, SavedSettings = settings, ActiveSettings = settings,
            SettingsError = read.Error, StartupError = startupError, Manifests = manifests,
            Plugins = plugins, Execution = new(plugins.Handlers, diagnostic)
        };
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<EditorSession>();
        var app = builder.Build();
        var session = app.Services.GetRequiredService<EditorSession>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !session.Authorized(context))
            { context.Response.StatusCode = 403; return; }
            try { await next(context); }
            catch (Cortex.Core.Flows.TypedFlowException error)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Graph inválido.", issues = error.Issues }); }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { context.Response.StatusCode = error is InvalidOperationException ? 409 : 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        EditorEndpoints.Map(app, runtime);
        app.Lifetime.ApplicationStopping.Register(runtime.Execution.Cancel);
        app.Lifetime.ApplicationStopped.Register(() => { runtime.Execution.Dispose(); plugins.Dispose(); });
        return app;
    }
}
