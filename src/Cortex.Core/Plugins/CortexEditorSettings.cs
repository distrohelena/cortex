using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Core.Plugins;

public sealed record NodePluginSetting
{
    public string Id { get; init; } = "";
    public bool Enabled { get; init; }
    public JsonElement Configuration { get; init; } = JsonSerializer.SerializeToElement(new { });
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record CortexEditorSettings
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<NodePluginSetting> Plugins { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }

    public CortexEditorSettings WithInstalled(IEnumerable<NodePluginManifest> manifests)
    {
        var entries = Plugins.ToList();
        foreach (var manifest in manifests)
            if (entries.All(entry => entry.Id != manifest.Id))
                entries.Add(new() { Id = manifest.Id, Configuration = JsonSerializer.SerializeToElement(manifest.ConfigurationFields.ToDictionary(field => field.Name, field => field.DefaultValue)) });
        return this with { Plugins = entries };
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Plugins is null || Plugins.Count > 64 ||
            Plugins.Any(plugin => plugin is null || string.IsNullOrWhiteSpace(plugin.Id) || plugin.Configuration.ValueKind != JsonValueKind.Object) ||
            Plugins.Select(plugin => plugin.Id).Distinct(StringComparer.Ordinal).Count() != Plugins.Count)
            throw new InvalidDataException("Settings inválidas: use schemaVersion 1 e IDs de plugins únicos com configuration objeto.");
    }
}

public sealed record CortexSettingsReadResult(CortexEditorSettings Settings, string? Error = null, string? OriginalText = null);
