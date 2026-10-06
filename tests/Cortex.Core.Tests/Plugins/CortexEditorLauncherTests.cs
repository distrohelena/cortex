using Cortex.Core.Plugins;
using Xunit;

namespace Cortex.Core.Tests.Plugins;

public sealed class CortexEditorLauncherTests
{
    [Fact]
    public void DllLaunchKeepsUnicodeAndSpacePathsAsDistinctArguments()
    {
        var graph = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meu vídeo", "graph.json"));
        var editor = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meu editor", "Cortex.Editor.dll"));
        var start = CortexEditorLauncher.CreateStartInfo(graph, "entrada", editor);
        Assert.Equal("dotnet", start.FileName);
        Assert.Equal(new[] { editor, "--graph", graph, "--entry", "entrada" }, start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void ExecutableOverrideDoesNotRequireEditingSharedSettings()
    {
        var editor = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Cortex.Editor.exe"));
        var start = CortexEditorLauncher.CreateStartInfo(Path.GetFullPath("graph.json"), null, editor);
        Assert.Equal(editor, start.FileName);
        Assert.DoesNotContain("--settings", start.ArgumentList);
        Assert.DoesNotContain("--installation", start.ArgumentList);
    }
}
