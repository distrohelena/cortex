using System.Text.Json;

namespace Cortex.Core.Plugins;

public sealed record CortexEditorInstallation
{
    public int SchemaVersion { get; init; } = 1;
    public string EditorPath { get; init; } = "";
    public string PluginDirectory { get; init; } = "";
    public static string DefaultPath => Path.Combine(CortexSettingsStore.DefaultDirectory, "installation.json");
    public static CortexEditorInstallation? Read(string? path = null)
    {
        path = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(path)) return null;
        var installation = JsonSerializer.Deserialize<CortexEditorInstallation>(File.ReadAllText(path), CortexSettingsStore.JsonOptions)
            ?? throw new InvalidDataException("Instalação do editor inválida.");
        if (installation.SchemaVersion != 1 || string.IsNullOrWhiteSpace(installation.EditorPath) || string.IsNullOrWhiteSpace(installation.PluginDirectory))
            throw new InvalidDataException("Instalação do editor inválida.");
        return installation with
        {
            EditorPath = NodePluginContext.ResolvePath(installation.EditorPath, Path.GetDirectoryName(path)!),
            PluginDirectory = NodePluginContext.ResolvePath(installation.PluginDirectory, Path.GetDirectoryName(path)!)
        };
    }
}
