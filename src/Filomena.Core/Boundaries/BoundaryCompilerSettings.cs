using System.Text.Json;

namespace Filomena.Core.Boundaries;

public sealed record BoundaryCompilerSettings(string TaxonomyVersion, string GrammarVersion, string TemplatesVersion)
{
    public void Validate()
    {
        if (TaxonomyVersion != "v1" || GrammarVersion != BoundarySourceParserV1.Version || TemplatesVersion != "v1")
            throw new BoundaryCompilationException("", "unsupported", "invalid-compiler-configuration");
    }

    public static BoundaryCompilerSettings Read(JsonElement config)
    {
        var versions = config.GetProperty("settings");
        var settings = new BoundaryCompilerSettings(versions.GetProperty("taxonomyVersion").GetString()!,
            versions.GetProperty("grammarVersion").GetString()!, versions.GetProperty("templatesVersion").GetString()!);
        settings.Validate();
        return settings;
    }
}
