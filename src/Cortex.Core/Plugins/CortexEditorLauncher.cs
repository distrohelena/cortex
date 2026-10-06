using System.Diagnostics;

namespace Cortex.Core.Plugins;

public static class CortexEditorLauncher
{
    public static ProcessStartInfo CreateStartInfo(string graphPath, string? entryId = null, string? editorExecutableOverride = null, bool openBrowser = true, bool useMode = false, string? applicationRoot = null)
    {
        var editorPath = editorExecutableOverride ?? CortexEditorInstallation.Read()?.EditorPath
            ?? throw new InvalidOperationException("Instale o editor do Cortex com tools/install-cortex-editor.ps1 antes de abri-lo.");
        editorPath = Path.GetFullPath(editorPath);
        var isDll = editorPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(isDll ? "dotnet" : editorPath) { UseShellExecute = false, CreateNoWindow = true };
        if (isDll) start.ArgumentList.Add(editorPath);
        start.ArgumentList.Add("--graph");
        start.ArgumentList.Add(Path.GetFullPath(graphPath));
        if (!string.IsNullOrWhiteSpace(entryId)) { start.ArgumentList.Add("--entry"); start.ArgumentList.Add(entryId); }
        if (useMode) start.ArgumentList.Add("--use");
        if (applicationRoot is not null) { start.ArgumentList.Add("--application-root"); start.ArgumentList.Add(Path.GetFullPath(applicationRoot)); }
        if (!openBrowser) start.ArgumentList.Add("--no-open");
        return start;
    }

    public static Process Open(string graphPath, string? entryId = null, string? editorExecutableOverride = null, bool openBrowser = true, bool useMode = false, string? applicationRoot = null) =>
        Process.Start(CreateStartInfo(graphPath, entryId, editorExecutableOverride, openBrowser, useMode, applicationRoot)) ?? throw new InvalidOperationException("Não foi possível iniciar o editor do Cortex.");
}
