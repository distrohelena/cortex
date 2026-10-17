using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Core.Tests.Plugins;

using System.Runtime.CompilerServices;
using System.Runtime.Loader;

public sealed class NodePluginLoaderTests
{
    [Fact]
    public void FrameworkAssembliesAreSharedButPackagedSystemAssembliesLoadFromThePlugin()
    {
        Assert.True(NodePluginLoadContext.IsPlatform("System.Runtime"));
        Assert.True(NodePluginLoadContext.IsPlatform("System.Text.Json"));
        Assert.True(NodePluginLoadContext.IsPlatform("netstandard"));
        // A Windows-only package a plugin ships next to its DLL (helengine caption measuring uses it).
        Assert.False(NodePluginLoadContext.IsPlatform("System.Drawing.Common"));
        Assert.False(NodePluginLoadContext.IsPlatform("helengine.video"));
    }

    [Fact]
    public void DisabledMissingDllIsNeverOpened()
    {
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture" }] },
            [Manifest() with { AssemblyPath = "missing.dll", EntryType = "No.Such.Type" }], Path.GetTempPath());
        Assert.Empty(runtime.Definitions);
        Assert.Empty(runtime.LibrariesByKind);
        Assert.Null(runtime.Reports.Single().Error);
        Assert.False(runtime.Reports.Single().Loaded);
    }

    [Fact]
    public async Task EnabledFixtureUsesHostCoreAndRunsExistingRunner()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture"), [Manifest()], Path.GetTempPath());
        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        var handler = Assert.Single(runtime.BindHandlers());
        Assert.Equal(typeof(FlowNodeHandler), handler.GetType());
        Assert.Equal(new NodeLibrary("fixture", "Fixture"), runtime.LibrariesByKind[handler.Definition.Kind]);
        var graph = JsonSerializer.SerializeToElement(new { version = 4, nodes = new[] { new { id = "input", kind = "fixture" } }, edges = Array.Empty<object>() });
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "input", runtime.BindHandlers()), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Equal("fixture-output", result.Outputs["input"]["value"].Value.GetString());
    }

    [Fact]
    public void MetadataDiscoveryNeverCallsRuntimeHandlerFactory()
    {
        var configuration = JsonSerializer.SerializeToElement(new { runtimeFactoryThrows = true });
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture", Enabled = true, Configuration = configuration }] },
            [Manifest()], Path.GetTempPath());

        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        Assert.Equal("fixture", Assert.Single(runtime.Definitions).Kind);
        Assert.Throws<InvalidOperationException>(() => runtime.BindHandlers());
    }

    [Fact]
    public void BindingAcceptsIndependentlyConstructedEquivalentCompoundSchemas()
    {
        var configuration = JsonSerializer.SerializeToElement(new { compoundSchema = true });
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture", Enabled = true, Configuration = configuration }] },
            [Manifest()], Path.GetTempPath());

        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        Assert.Single(runtime.BindHandlers());
    }

    [Fact]
    public void BindingRejectsAChangedNestedCompoundSchema()
    {
        var configuration = JsonSerializer.SerializeToElement(new { compoundSchema = true, compoundMismatch = true });
        using var runtime = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture", Enabled = true, Configuration = configuration }] },
            [Manifest()], Path.GetTempPath());

        Assert.True(runtime.Reports.Single().Loaded, runtime.Reports.Single().Error);
        Assert.Throws<InvalidDataException>(() => runtime.BindHandlers());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalRejectsBindingAndReleasesCollectiblePluginContext(bool bindFirst)
    {
        var (runtime, context) = CreateDisposedRuntime(bindFirst);
        Assert.Throws<ObjectDisposedException>(() => runtime.BindHandlers());
        Assert.True(WaitForUnload(context));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (LoadedNodePlugins Runtime, WeakReference Context) CreateDisposedRuntime(bool bindFirst)
    {
        var runtime = NodePluginLoader.Load(Enabled("fixture"), [Manifest()], Path.GetTempPath());
        var context = AssemblyLoadContext.All.Single(item => item.Name?.StartsWith("CortexPlugin-", StringComparison.Ordinal) == true);
        var weak = new WeakReference(context, trackResurrection: true);
        if (bindFirst) _ = runtime.BindHandlers();
        runtime.Dispose();
        return (runtime, weak);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForUnload(WeakReference context)
    {
        for (var attempt = 0; attempt < 20 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(10);
        }
        return !context.IsAlive;
    }

    [Fact]
    public void UnsupportedContractIsReported()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture"), [Manifest() with { EntryType = "Cortex.Plugin.TestFixture.UnsupportedPlugin" }], Path.GetTempPath());
        Assert.Empty(runtime.Definitions);
        Assert.NotNull(runtime.Reports.Single().Error);
    }

    [Fact]
    public void OneBrokenPluginDoesNotRemoveAnother()
    {
        using var runtime = NodePluginLoader.Load(Enabled("fixture", "broken"),
            [Manifest(), Manifest() with { Id = "broken", AssemblyPath = "missing.dll" }], Path.GetTempPath());
        Assert.Single(runtime.Definitions);
        Assert.NotNull(runtime.Reports.Single(report => report.Id == "broken").Error);
    }

    [Fact]
    public void DuplicateAndBuiltinKindsAreRejected()
    {
        var configuration = JsonSerializer.SerializeToElement(new { kind = "prompt" });
        using var reserved = NodePluginLoader.Load(new() { Plugins = [new() { Id = "fixture", Enabled = true, Configuration = configuration }] }, [Manifest()], Path.GetTempPath());
        Assert.Empty(reserved.Definitions);
        Assert.NotNull(reserved.Reports.Single().Error);
        using var duplicate = NodePluginLoader.Load(Enabled("fixture", "second"), [Manifest(), Manifest() with { Id = "second" }], Path.GetTempPath());
        Assert.Single(duplicate.Definitions);
        Assert.NotNull(duplicate.Reports.Single(report => report.Id == "second").Error);
        Assert.Equal(new NodeLibrary("fixture", "Fixture"), duplicate.LibrariesByKind["fixture"]);
    }

    private static CortexEditorSettings Enabled(params string[] ids) => new() { Plugins = ids.Select(id => new NodePluginSetting { Id = id, Enabled = true }).ToArray() };
    public static NodePluginManifest Manifest() => new()
    {
        Id = "fixture", DisplayName = "Fixture", AssemblyPath = Path.Combine(AppContext.BaseDirectory, "plugin-fixture", "Cortex.Plugin.TestFixture.dll"),
        EntryType = "Cortex.Plugin.TestFixture.FixtureNodePlugin"
    };
}
