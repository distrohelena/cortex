using System.Text.Json;
using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Core.Tests.Plugins;

public sealed class CortexSettingsStoreTests
{
    [Fact]
    public void DefaultPathIsSharedAndAbsolute()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cortex", "editor", "settings.json"), CortexSettingsStore.DefaultPath);
        Assert.True(Path.IsPathFullyQualified(CortexSettingsStore.DefaultPath));
    }

    [Fact]
    public async Task NewProfileDisablesInstalledPlugin()
    {
        var path = PrivatePath();
        var read = await new CortexSettingsStore(path).ReadAsync(TestContext.Current.CancellationToken);
        Assert.Null(read.Error);
        var merged = read.Settings.WithInstalled([new() { Id = "video", DisplayName = "Vídeo" }]);
        Assert.False(merged.Plugins.Single().Enabled);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task UnknownPluginConfigurationSurvivesAtomicSave()
    {
        var path = PrivatePath();
        var unknown = JsonSerializer.SerializeToElement(new { custom = new[] { 1, 2 }, nested = new { value = "á" } });
        var store = new CortexSettingsStore(path);
        await store.SaveAsync(new() { Plugins = [new() { Id = "other", Enabled = true, Configuration = unknown }] }, TestContext.Current.CancellationToken);
        var read = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Null(read.Error);
        Assert.True(JsonElement.DeepEquals(unknown, read.Settings.Plugins.Single().Configuration));
        Assert.True(read.Settings.Plugins.Single().Enabled);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task CorruptJsonIsReportedAndNotOverwrittenByRead()
    {
        var path = PrivatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = "{\"plugins\": oops";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var read = await new CortexSettingsStore(path).ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(read.Error);
        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(read.Settings.Plugins);
    }

    [Fact]
    public async Task ExplicitRepairPreservesCorruptOriginal()
    {
        var path = PrivatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "broken", TestContext.Current.CancellationToken);
        await new CortexSettingsStore(path).SaveAsync(new(), TestContext.Current.CancellationToken);
        var backup = Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.invalid-*.json").Single();
        Assert.Equal("broken", await File.ReadAllTextAsync(backup, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnsupportedVersionAndDuplicatePluginsAreRejected()
    {
        var store = new CortexSettingsStore(PrivatePath());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new() { SchemaVersion = 2 }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new() { Plugins = [new() { Id = "video" }, new() { Id = "video" }] }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RelativeConfiguredPathsUseConfigurationDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-settings-fixture");
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "models", "small.bin")), NodePluginContext.ResolvePath("models/small.bin", directory));
        Assert.Equal("ffmpeg", NodePluginContext.ResolvePath("ffmpeg", directory, allowCommandName: true));
    }

    private static string PrivatePath() => Path.Combine(Path.GetTempPath(), "cortex-editor-tests", Guid.NewGuid().ToString("N"), "settings.json");
}
