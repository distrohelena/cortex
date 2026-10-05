using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Core.Tests.Plugins;

public sealed class NodePluginLoaderTests
{
    [Fact]
    public void DisabledMissingDllIsNeverOpened()
    {
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture" }] },
            [Manifest() with { AssemblyPath = "missing.dll", EntryType = "No.Such.Type" }], Path.GetTempPath());
        Assert.Empty(runtime.Handlers);
        Assert.Null(runtime.Reports.Single().Error);
        Assert.False(runtime.Reports.Single().Loaded);
    }

    [Fact]
    public async Task EnabledFixtureUsesHostCoreAndRunsExistingRunner()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture"), [Manifest()], Path.GetTempPath());
        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        var handler = Assert.Single(runtime.Handlers);
        Assert.Equal(typeof(FlowNodeHandler), handler.GetType());
        var graph = JsonSerializer.SerializeToElement(new { version = 3, nodes = new[] { new { id = "input", kind = "fixture" } }, edges = Array.Empty<object>() });
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "input", runtime.Handlers), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("fixture-output", result.Outputs["input"]["value"].Value.GetString());
    }

    [Fact]
    public void UnsupportedContractIsReported()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture"), [Manifest() with { EntryType = "Cortex.Plugin.TestFixture.UnsupportedPlugin" }], Path.GetTempPath());
        Assert.Empty(runtime.Handlers);
        Assert.NotNull(runtime.Reports.Single().Error);
    }

    [Fact]
    public void OneBrokenPluginDoesNotRemoveAnother()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture", "broken"),
            [Manifest(), Manifest() with { Id = "broken", AssemblyPath = "missing.dll" }], Path.GetTempPath());
        Assert.Single(runtime.Handlers);
        Assert.NotNull(runtime.Reports.Single(report => report.Id == "broken").Error);
    }

    [Fact]
    public void DuplicateAndBuiltinKindsAreRejected()
    {
        var configuration = JsonSerializer.SerializeToElement(new { kind = "prompt" });
        using var reserved = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture", Enabled = true, Configuration = configuration }] }, [Manifest()], Path.GetTempPath());
        Assert.Empty(reserved.Handlers);
        Assert.NotNull(reserved.Reports.Single().Error);
        using var duplicate = NodePluginLoader.Load(Enabled("fixture", "second"), [Manifest(), Manifest() with { Id = "second" }], Path.GetTempPath());
        Assert.Single(duplicate.Handlers);
        Assert.NotNull(duplicate.Reports.Single(report => report.Id == "second").Error);
    }

    private static CortexEditorSettings Enabled(params string[] ids) => new() { Plugins = ids.Select(id => new NodePluginSetting { Id = id, Enabled = true }).ToArray() };
    public static NodePluginManifest Manifest() => new()
    {
        Id = "fixture", DisplayName = "Fixture", AssemblyPath = Path.Combine(AppContext.BaseDirectory, "plugin-fixture", "Cortex.Plugin.TestFixture.dll"),
        EntryType = "Cortex.Plugin.TestFixture.FixtureNodePlugin"
    };
}
