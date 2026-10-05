using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cortex.Editor.Tests;

[Trait("Category", "LocalSocket")]
public sealed class EditorApiTests
{
    [Fact]
    public async Task SessionAndSameOriginAreRequired()
    {
        var (app, client, _) = await StartAsync(false);
        await using (app)
        using (client)
        {
            using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
            Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("api/state", TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "http://foreign.invalid");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).StatusCode);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SavedPluginChangeIsPendingRestartAndDoesNotLoadDll()
    {
        var (app, client, runtime) = await StartAsync(false);
        await using (app)
        using (client)
        {
            Assert.Empty(runtime.Plugins.Handlers);
            var response = await client.PutAsJsonAsync("api/settings", new CortexEditorSettings
            { Plugins = [new() { Id = "fixture", Enabled = true }] }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            Assert.True(runtime.PendingRestart);
            Assert.Empty(runtime.Plugins.Handlers);
            Assert.False(runtime.Plugins.Reports.Single().Loaded);
            Assert.True((await runtime.SettingsStore.ReadAsync(TestContext.Current.CancellationToken)).Settings.Plugins.Single().Enabled);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task RunRejectsEditsAndCancelReleasesIt()
    {
        var (app, client, runtime) = await StartAsync(true, wait: true);
        await using (app)
        using (client)
        {
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture"), "input"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsync("api/run", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            Assert.True(runtime.Execution.Busy);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture"), "input"), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("api/settings", new CortexEditorSettings(), TestContext.Current.CancellationToken)).StatusCode);
            (await client.PostAsync("api/run/cancel", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            await runtime.Execution.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("cancelled", runtime.Execution.Snapshot().State);
            Assert.False(runtime.Execution.Busy);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task CorruptSettingsStartWithErrorAndRemainUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        await File.WriteAllTextAsync(path, "broken", TestContext.Current.CancellationToken);
        await using var app = await EditorHost.BuildAsync(new(SettingsPath: path, InstallationDirectory: root, OpenBrowser: false), TestContext.Current.CancellationToken);
        Assert.NotNull(app.Services.GetRequiredService<EditorRuntime>().SettingsError);
        Assert.Equal("broken", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private static async Task<(WebApplication App, HttpClient Client, EditorRuntime Runtime)> StartAsync(bool enabled, bool wait = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        var manifest = new NodePluginManifest
        {
            Id = "fixture", DisplayName = "Fixture", EntryType = "Cortex.Plugin.TestFixture.FixtureNodePlugin",
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "plugin-fixture", "Cortex.Plugin.TestFixture.dll")
        };
        await File.WriteAllTextAsync(Path.Combine(root, "plugins", "fixture.plugin.json"), JsonSerializer.Serialize(manifest, CortexSettingsStore.JsonOptions), TestContext.Current.CancellationToken);
        var store = new CortexSettingsStore(Path.Combine(root, "settings.json"));
        await store.SaveAsync(new() { Plugins = [new() { Id = "fixture", Enabled = enabled, Configuration = JsonSerializer.SerializeToElement(new { wait }) }] }, TestContext.Current.CancellationToken);
        var app = await EditorHost.BuildAsync(new(SettingsPath: store.FilePath, InstallationDirectory: root, OpenBrowser: false), TestContext.Current.CancellationToken);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Cortex-Session", app.Services.GetRequiredService<EditorSession>().Token);
        client.DefaultRequestHeaders.Add("Origin", address);
        return (app, client, app.Services.GetRequiredService<EditorRuntime>());
    }
}
