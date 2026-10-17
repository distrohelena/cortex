using System.Collections.Concurrent;
using System.Net;
using Cortex.Core.Plugins;

namespace Cortex.Editor;

public sealed record EditorLaunchOptions(string? GraphPath = null, string? EntryId = null, string? SettingsPath = null,
    string? InstallationDirectory = null, int Port = 0, bool OpenBrowser = true, bool Diagnostics = false, bool UseMode = false, string? ApplicationRoot = null, string? PublicOrigin = null);

public sealed class EditorRuntime
{
    public required EditorGraphStore Graph { get; init; }
    public required CortexSettingsStore SettingsStore { get; init; }
    public required CortexEditorSettings SavedSettings { get; set; }
    public required CortexEditorSettings ActiveSettings { get; init; }
    public required IReadOnlyList<NodePluginManifest> Manifests { get; init; }
    public required LoadedNodePlugins Plugins { get; init; }
    public required EditorExecutionService Execution { get; init; }
    public EditorGraphNavigationRegistry Navigation { get; init; } = new();
    public bool UseMode { get; init; }
    public bool ReadOnly => UseMode || Navigation.CurrentReadOnly;
    public EditorUseService? Use { get; init; }
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
        if (options.PublicOrigin is not null && !options.UseMode) throw new ArgumentException("Public access requires a locked graph in Use mode.");
        var editorSession = new EditorSession(options.PublicOrigin);
        if(options.ApplicationRoot is not null && (!options.UseMode || !File.Exists(Path.Combine(Path.GetFullPath(options.ApplicationRoot), "index.html")))) throw new InvalidDataException("A consumer application requires Use mode and an index.html in its own web root.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = AppContext.BaseDirectory, WebRootPath = options.ApplicationRoot is null ? Path.Combine(AppContext.BaseDirectory, "wwwroot") : Path.GetFullPath(options.ApplicationRoot), Args = [] });
        if (options.Port is < 0 or > 65535) throw new ArgumentException("Invalid port.");
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
        { manifests = []; startupError = "Could not read plugin manifests: " + error.Message; }
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
        var entry = new EditorExecutionService(plugins.Definitions, plugins.BindHandlers).SelectEntry(graph.Graph, graph.EntryId);
        if (options.UseMode && entry is null) throw new InvalidDataException("Use mode requires a graph with an available entry.");
        var use = options.UseMode ? new EditorUseService(graph.Graph, entry!, plugins.Definitions, plugins.BindHandlers, diagnostic) : null;
        var runtime = new EditorRuntime
        {
            Graph = graph, SettingsStore = store, SavedSettings = settings, ActiveSettings = settings,
            SettingsError = read.Error, StartupError = startupError, Manifests = manifests,
            Plugins = plugins, Execution = new(plugins.Definitions, plugins.BindHandlers, diagnostic), UseMode = options.UseMode, Use = use
        };
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(editorSession);
        var app = builder.Build();
        var session = app.Services.GetRequiredService<EditorSession>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !session.Authorized(context))
            { context.Response.StatusCode = 403; return; }
            var isGraphNavigation = context.Request.Path == "/api/graph/navigate" || context.Request.Path == "/api/graph/back";
            if (options.UseMode && (context.Request.Path.StartsWithSegments("/api/settings") || context.Request.Method != "GET" && ((context.Request.Path.StartsWithSegments("/api/graph") && !isGraphNavigation) || context.Request.Path == "/api/run")))
            { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "The graph template is locked in Use mode." }); return; }
            if (!options.UseMode && context.Request.Path.StartsWithSegments("/api/use")) { context.Response.StatusCode = 404; return; }
            if (options.UseMode && options.ApplicationRoot is null && context.Request.Path == "/") { context.Response.Redirect("/use.html"); return; }
            try { await next(context); }
            catch (Cortex.Core.Flows.TypedFlowException error)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid graph.", issues = error.Issues }); }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { context.Response.StatusCode = error is InvalidOperationException ? 409 : 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        EditorEndpoints.Map(app, runtime);
        app.Lifetime.ApplicationStopping.Register(() => { runtime.Execution.Cancel(); use?.Cancel(); });
        app.Lifetime.ApplicationStopped.Register(() => { runtime.Execution.Dispose(); use?.Dispose(); plugins.Dispose(); });
        return app;
    }
}
