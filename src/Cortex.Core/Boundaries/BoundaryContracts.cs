using System.Runtime.CompilerServices;
using System.Text.Json;
using Cortex.Core.Memory;

[assembly: InternalsVisibleTo("Cortex.Core.Tests")]

namespace Cortex.Core.Boundaries;

public sealed class Boundary
{
    internal Boundary(MemoryItem source, string hash, BoundaryAdmissionResult admitted)
    {
        OriginalText = source.Content;
        Admission = admitted;
        TopicEvidence = admitted.Topic!.Value.Extract(source.Content);
        ConstraintEvidence = admitted.Constraint!.Value.Extract(source.Content);
        NormativeClauseSpan = admitted.NormativeClause!.Value;
        Source = new(source.Id, hash, source.ConversationNamespace, source.Subjects.Single(), source.SourceKind,
            Array.AsReadOnly((source.EvidenceEpisodeIds ?? []).ToArray()), source.ValidAt, source.InvalidAt, source.ExpiredAt);
        Compilation = new("v1", "v1", "no_teasing_about_topic:v1", "v1", "personal_boundary_disclosure:silent:v1");
    }

    public string Status => "compiled";
    public string Kind => BoundaryAdmission.Kind;
    public string OriginalText { get; }
    public string TopicEvidence { get; }
    public string ConstraintEvidence { get; }
    internal BoundarySourceSpan NormativeClauseSpan { get; }
    internal BoundarySource Source { get; }
    internal BoundaryCompilation Compilation { get; }
    internal BoundaryAdmissionResult Admission { get; }
}

internal sealed record BoundarySource(string MemoryId, string SourceHash, string ConversationNamespace,
    MemoryPerson CanonicalSubject, string SourceKind, IReadOnlyList<string> EvidenceEpisodeIds,
    DateTimeOffset? ValidAt, DateTimeOffset? InvalidAt, DateTimeOffset? ExpiredAt);
internal sealed record BoundaryCompilation(string TaxonomyVersion, string InterpreterContractVersion,
    string TemplateId, string TemplateVersion, string DisclosurePolicyVersion)
{
    public string GrammarVersion => BoundarySourceParserV1.Version;
}

public sealed class BoundaryCompilationException(string sourceMemoryId, string status, string diagnosticCode)
    : Exception($"Boundary compilation failed: {status}/{diagnosticCode}.")
{
    public string SourceMemoryId { get; } = sourceMemoryId;
    public string Status { get; } = status;
    public string DiagnosticCode { get; } = diagnosticCode;
}

public sealed record BoundaryInterpreterSettings(string Provider, string Model, string? ReasoningEffort,
    int MaxOutputTokens, string TaxonomyVersion, string InterpreterContractVersion, string TemplatesVersion)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(Model) || MaxOutputTokens is < 128 or > 8192 ||
            TaxonomyVersion != "v1" || InterpreterContractVersion != "v1" || TemplatesVersion != "v1" ||
            ReasoningEffort is not (null or "low" or "medium" or "high"))
            throw new BoundaryCompilationException("", "unsupported", "invalid-interpreter-configuration");
    }

    public static BoundaryInterpreterSettings Read(JsonElement config)
    {
        var versions = config.GetProperty("settings");
        var settings = new BoundaryInterpreterSettings(config.GetProperty("provider").GetString()!, config.GetProperty("model").GetString()!,
            config.TryGetProperty("reasoningEffort", out var effort) ? effort.GetString() : null,
            config.GetProperty("maxOutputTokens").GetInt32(), versions.GetProperty("taxonomyVersion").GetString()!,
            versions.GetProperty("interpreterContractVersion").GetString()!, versions.GetProperty("templatesVersion").GetString()!);
        settings.Validate();
        return settings;
    }
}
