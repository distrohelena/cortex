using Filomena.Evaluation.Contracts;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public sealed record CompletionTrialMaterial(ConfigurationProfile Profile, CompletionEvaluationCase Input)
{
    public static CompletionTrialMaterial FromStoredDocuments(EvaluationAccess access, TrialClaim claim,
        ScopedRevisionRef targetReference, RevisionDocument target, RevisionDocument profile,
        RevisionDocument input)
    {
        access.Demand(EvaluationOperation.Evaluate);
        access.DemandScope(targetReference.Scope);
        access.DemandScope(claim.Candidate.Scope);
        access.DemandScope(claim.Case.Scope);
        Validate(targetReference, target, "completion-target");
        Validate(claim.Candidate, profile, "configuration-profile");
        Validate(claim.Case, input, "completion-input");
        if (!target.Payload.TryGetProperty("nodeId", out var node) || node.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(node.GetString()) || node.GetString()!.Length > 256 ||
            node.GetString()!.Any(char.IsControl))
            throw new InvalidDataException("Stored completion target requires a valid node identity.");
        ConfigurationProfile configuration;
        try
        {
            configuration = profile.Payload.Deserialize<ConfigurationProfile>(ProfileJson.Options)
                ?? throw new InvalidDataException("Stored profile is unavailable.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Stored profile schema is invalid.", error);
        }
        if (ProfileValidator.Validate(configuration).Count != 0 ||
            RevisionHasher.Hash(configuration.ToRevisionDocument()) != claim.Candidate.Revision.Sha256)
            throw new InvalidDataException("Stored profile cannot be reconstructed without changing its revision.");
        return new(configuration, new(access.Scope, node.GetString()!, claim.Case.Revision, input));
    }

    private static void Validate(ScopedRevisionRef reference, RevisionDocument document, string kind)
    {
        if (reference.Revision.Id == Guid.Empty || document.ContractVersion != 1 || document.Kind != kind ||
            document.Payload.ValueKind != JsonValueKind.Object || RevisionHasher.Hash(document) != reference.Revision.Sha256)
            throw new InvalidDataException("Stored completion material is invalid or differs from its pinned revision.");
    }
}
