using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Personas;

public sealed record PersonaExtractionLimits(
    int MaxSourceBytes, int MaxResponseBytes, int MaxCandidates, int MaxTextCodeUnits);
public sealed record PersonaAttributedPassage(
    string PassageId, TextLocation Location, string SubjectRef, string Role);
public sealed record PersonaExtractionRequest(
    PersonaScope Scope, PersonaSource Source, string SubjectRef,
    IReadOnlyList<PersonaAttributedPassage> Passages,
    string RequestId, string RunId, string ConfigurationRevision,
    EvidenceMethod Method, string PromptRevision, PersonaExtractionLimits Limits);
public sealed record PersonaCandidateDraft(
    string PassageId, string Quotation, string ObservedContext, string ObservedBehavior,
    string Interpretation, IReadOnlyList<string> Alternatives, EvidenceConfidence Confidence);
public sealed record PersonaCandidateResponse(
    int ContractVersion, IReadOnlyList<PersonaCandidateDraft> Candidates);
public sealed record PersonaEvidenceCandidate(
    string CandidateId, string PassageId, TextLocation Location, string SubjectRef, string Role,
    string Quotation, string ObservedContext, string ObservedBehavior,
    string Interpretation, IReadOnlyList<string> Alternatives, EvidenceConfidence Confidence);
public sealed record PersonaCandidateBatch(
    string ArtifactKind, int ContractVersion, string ReviewStatus, string RequestSha256,
    PersonaExtractionRequest Request, IReadOnlyList<PersonaEvidenceCandidate> Candidates);

/// <summary>Structural integrity only; not source access or publication authority.</summary>
public sealed class ValidatedPersonaExtractionRequest
{
    private readonly byte[] bytes;
    internal ValidatedPersonaExtractionRequest(byte[] bytes)
    {
        this.bytes = bytes.ToArray();
        RequestSha256 = PersonaCandidateJson.Hash(this.bytes);
    }
    public PersonaExtractionRequest Snapshot => PersonaCandidateJson.Read<PersonaExtractionRequest>(bytes);
    public string RequestSha256 { get; }
}

/// <summary>Unapproved candidates, never an approved personality revision.</summary>
public sealed class ValidatedPersonaCandidateBatch
{
    private readonly byte[] bytes;
    internal ValidatedPersonaCandidateBatch(byte[] bytes)
    {
        this.bytes = bytes.ToArray();
        ArtifactSha256 = PersonaCandidateJson.Hash(this.bytes);
    }
    public PersonaCandidateBatch Batch => PersonaCandidateJson.Read<PersonaCandidateBatch>(bytes);
    public ReadOnlyMemory<byte> Bytes => bytes.ToArray();
    public string ArtifactSha256 { get; }
}

internal static class PersonaCandidateJson
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static byte[] Write<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    internal static T Read<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            Utf8.GetCharCount(bytes);
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new InvalidDataException("invalid_candidate_json");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("invalid_candidate_json");
        }
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static bool IsHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    internal static int ByteCount(string text)
    {
        if (text is null) throw new InvalidDataException("required_text_missing");
        try { return Utf8.GetByteCount(text); }
        catch (EncoderFallbackException) { throw new InvalidDataException("invalid_candidate_unicode"); }
    }

    internal static void Text(string? text, int max, bool optional = false)
    {
        if (text is null && optional) return;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("required_text_missing");
        ByteCount(text);
        if (text.Length > max) throw new InvalidDataException("candidate_text_limit_exceeded");
    }
}
