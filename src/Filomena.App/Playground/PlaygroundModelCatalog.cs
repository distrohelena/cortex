using System.Text.Json;

namespace Filomena.App.Playground;

internal sealed record CatalogModel(string Id, string Name);
internal sealed record CatalogProvider(string Id, string Name, CatalogModel[] Models);
internal sealed record PlaygroundModelCatalog(int Version, CatalogProvider[] Providers)
{
    public static PlaygroundModelCatalog Read(string? sourceDirectory)
    {
        var path = Path.Combine(sourceDirectory ?? AppContext.BaseDirectory, "model-catalog.json");
        string text;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 262144) throw new InvalidDataException();
            text = File.ReadAllText(path);
        }
        else
        {
            using var stream = typeof(PlaygroundModelCatalog).Assembly.GetManifestResourceStream("Filomena.App.Playground.model-catalog.json")!;
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        var catalog = JsonSerializer.Deserialize<PlaygroundModelCatalog>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (catalog is null || catalog.Version != 1 || catalog.Providers is null || catalog.Providers.Length > 50)
            throw new InvalidDataException();
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in catalog.Providers)
        {
            if (provider is null || !Valid(provider.Id, 120) || !Valid(provider.Name, 120)
                || !providers.Add(provider.Id) || provider.Models is null || provider.Models.Length > 200)
                throw new InvalidDataException();
            var models = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in provider.Models)
                if (model is null || !Valid(model.Id, 256) || !Valid(model.Name, 120) || !models.Add(model.Id))
                    throw new InvalidDataException();
        }
        return catalog;
    }

    private static bool Valid(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && value == value.Trim();
}
