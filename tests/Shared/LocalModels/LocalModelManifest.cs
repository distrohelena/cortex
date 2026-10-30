using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cortex.TestSupport.LocalModels;

internal sealed record LocalModelAsset(Uri Url, string Sha256)
{
    internal string FileName => Url.Segments[^1];
}

internal sealed record LlamaCppPin(string Tag, IReadOnlyList<LocalModelAsset> Assets);

internal sealed record GgufPin(string Repo, string Revision, string File, string Sha256)
{
    internal Uri Url => new($"https://huggingface.co/{Repo}/resolve/{Revision}/{File}");
}

/// <summary>
/// The Laya package pin. <c>Torch</c>/<c>TorchIndexUrl</c> exist because PyPI only serves CPU torch for Windows;
/// the CUDA build has to come from PyTorch's own index before <c>laya</c> resolves its <c>torch</c> requirement.
/// </summary>
internal sealed record LayaPin(string Package, string ModelRevision, string Torch, Uri TorchIndexUrl);

/// <summary>Pinned local-model downloads from <c>config/local-models.json</c>, shared with <c>tools/start-openjev.ps1</c>.</summary>
internal sealed partial record LocalModelManifest(LlamaCppPin LlamaCpp, IReadOnlyDictionary<string, GgufPin> Gguf, LayaPin Laya)
{
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cortex.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException($"Cortex.slnx was not found above '{AppContext.BaseDirectory}'.");
    }

    internal static LocalModelManifest Load()
    {
        var path = Path.Combine(RepositoryRoot(), "config", "local-models.json");
        try { return Parse(File.ReadAllText(path)); }
        catch (InvalidDataException error) { throw new InvalidDataException($"{path}: {error.Message}", error); }
    }

    internal static LocalModelManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = Object(document.RootElement, "manifest");

        var llama = Object(Property(root, "llamaCpp"), "llamaCpp");
        var assetsElement = Property(llama, "assets");
        if (assetsElement.ValueKind != JsonValueKind.Array || assetsElement.GetArrayLength() == 0)
            throw new InvalidDataException("llamaCpp.assets must be a non-empty array.");
        var assets = assetsElement.EnumerateArray().Select((asset, i) => new LocalModelAsset(
            HttpsUri(Property(Object(asset, $"llamaCpp.assets[{i}]"), "url"), $"llamaCpp.assets[{i}].url"),
            Sha256(Property(asset, "sha256"), $"llamaCpp.assets[{i}].sha256"))).ToArray();
        if (assets.Select(a => a.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assets.Length)
            throw new InvalidDataException("llamaCpp.assets file names must be distinct.");
        var tag = Text(Property(llama, "tag"), "llamaCpp.tag");
        if (!TagPattern().IsMatch(tag)) throw new InvalidDataException($"llamaCpp.tag '{tag}' is not a llama.cpp build tag.");

        var gguf = new Dictionary<string, GgufPin>(StringComparer.Ordinal);
        foreach (var model in Object(Property(root, "gguf"), "gguf").EnumerateObject())
        {
            var where = $"gguf.{model.Name}";
            var entry = Object(model.Value, where);
            var file = Text(Property(entry, "file"), $"{where}.file");
            if (file != Path.GetFileName(file) || !file.EndsWith(".gguf", StringComparison.Ordinal))
                throw new InvalidDataException($"{where}.file '{file}' must be a bare .gguf file name.");
            gguf.Add(model.Name, new GgufPin(Text(Property(entry, "repo"), $"{where}.repo"),
                Revision(Property(entry, "revision"), $"{where}.revision"), file, Sha256(Property(entry, "sha256"), $"{where}.sha256")));
        }

        var laya = Object(Property(root, "laya"), "laya");
        var package = Text(Property(laya, "package"), "laya.package");
        if (!package.StartsWith("laya[serve]==", StringComparison.Ordinal)) throw new InvalidDataException($"laya.package '{package}' must pin laya[serve]==<version>.");
        var torch = Text(Property(laya, "torch"), "laya.torch");
        if (!torch.StartsWith("torch==", StringComparison.Ordinal)) throw new InvalidDataException($"laya.torch '{torch}' must pin torch==<version>.");
        return new LocalModelManifest(new LlamaCppPin(tag, assets), gguf,
            new LayaPin(package, Revision(Property(laya, "modelRevision"), "laya.modelRevision"), torch, HttpsUri(Property(laya, "torchIndexUrl"), "laya.torchIndexUrl")));
    }

    internal GgufPin Model(string key) =>
        Gguf.TryGetValue(key, out var pin) ? pin : throw new ArgumentException($"Unknown local model '{key}'. Pinned: {string.Join(", ", Gguf.Keys)}.", nameof(key));

    private static JsonElement Object(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new InvalidDataException($"{where} must be an object.");

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : throw new InvalidDataException($"Missing '{name}'.");

    private static string Text(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()) ? element.GetString()!
            : throw new InvalidDataException($"{where} must be a non-empty string.");

    private static string Sha256(JsonElement element, string where)
    {
        var value = Text(element, where);
        return Sha256Pattern().IsMatch(value) ? value : throw new InvalidDataException($"{where} '{value}' must be 64 lowercase hex characters.");
    }

    private static string Revision(JsonElement element, string where)
    {
        var value = Text(element, where);
        return RevisionPattern().IsMatch(value) ? value : throw new InvalidDataException($"{where} '{value}' must be a 40-character commit sha.");
    }

    private static Uri HttpsUri(JsonElement element, string where)
    {
        var value = Text(element, where);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Query.Length == 0
            ? uri : throw new InvalidDataException($"{where} '{value}' must be an absolute https URL without userinfo or query.");
    }

    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256Pattern();
    [GeneratedRegex("^[0-9a-f]{40}$")] private static partial Regex RevisionPattern();
    [GeneratedRegex("^b[0-9]+$")] private static partial Regex TagPattern();
}
