using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Editor.Tests;

public sealed class ColdPluginTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FreshProcessLoadsInstalledVideoOnlyWhenEnabled(bool enabled, bool writeSettings)
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-cold-" + Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "plugins", "video");
        Directory.CreateDirectory(pluginDirectory);
        var repository = FindRepository();
        foreach (var file in new[] { "Cortex.Video.dll", "Cortex.Video.deps.json", "Cortex.Inference.dll", "cortex-video.plugin.json" })
            File.Copy(Path.Combine(repository, "src", "Cortex.Video", "bin", "Debug", "net10.0", file), Path.Combine(pluginDirectory, file));
        var settings = new CortexSettingsStore(Path.Combine(root, "settings.json"));
        if (writeSettings) await settings.SaveAsync(new() { Plugins = [new() { Id = "video", Enabled = enabled }] }, TestContext.Current.CancellationToken);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Cortex.Editor.Tests.runtimeconfig.json"), typeof(EditorHost).Assembly.Location,
            "--diagnostics", "--no-open", "--settings", settings.FilePath, "--installation", root }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await stderr);
        using var report = JsonDocument.Parse(await stdout);
        var assemblies = report.RootElement.GetProperty("loadedAssemblies").EnumerateArray().Select(a => a.GetString()).ToArray();
        Assert.Equal(enabled, assemblies.Contains("Cortex.Video"));
        if (!enabled) Assert.DoesNotContain("Cortex.Inference", assemblies);
        Assert.Equal(enabled, report.RootElement.GetProperty("reports")[0].GetProperty("loaded").GetBoolean());
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cortex.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found");
    }
}
