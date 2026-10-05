using System.Text.Json;

namespace Cortex.Core.Plugins;

public sealed record PluginConfigurationField(string Name, string Label, string DefaultValue = "", bool Required = false);

public sealed record NodePluginManifest
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string AssemblyPath { get; init; } = "";
    public string EntryType { get; init; } = "";
    public int ContractVersion { get; init; } = NodePluginContext.CurrentContractVersion;
    public IReadOnlyList<PluginConfigurationField> ConfigurationFields { get; init; } = [];

    public static IReadOnlyList<NodePluginManifest> ReadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.plugin.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var manifest = JsonSerializer.Deserialize<NodePluginManifest>(File.ReadAllText(path), CortexSettingsStore.JsonOptions)
                    ?? throw new InvalidDataException("Manifesto de plugin vazio: " + path);
                if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.EntryType) || string.IsNullOrWhiteSpace(manifest.AssemblyPath))
                    throw new InvalidDataException("Manifesto de plugin inválido: " + path);
                return manifest with { AssemblyPath = NodePluginContext.ResolvePath(manifest.AssemblyPath, Path.GetDirectoryName(Path.GetFullPath(path))!) };
            }).ToArray();
    }
}
