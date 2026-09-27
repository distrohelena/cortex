namespace Filomena.Evaluation.Contracts;

public sealed record EvidenceArtifactRef(Guid Id, string Sha256);

public sealed class EvidenceArtifactDocument
{
    private readonly byte[] content;

    public EvidenceArtifactDocument(string kind, bool isHoldout, ReadOnlySpan<byte> content)
    {
        Kind = kind;
        IsHoldout = isHoldout;
        this.content = content.ToArray();
    }

    public string Kind { get; }
    public bool IsHoldout { get; }
    public ReadOnlyMemory<byte> Content => content.ToArray();
}

public sealed record StoredEvidenceArtifact(EvidenceArtifactRef Reference, EvidenceArtifactDocument Document);

public sealed record EvidenceCaseDocument(string CaseKey, EvidenceArtifactRef Artifact);

public sealed record StoredEvidenceCase(Guid Id, EvidenceCaseDocument Document);

public enum AssessmentOutcome
{
    Pass,
    Fail,
    Unscored
}

/// <summary>Assessment input. The trusted evaluator principal and scope come from EvaluationAccess.</summary>
public sealed record Assessment(
    Guid CaseId,
    Guid? TrialId,
    string Criterion,
    RevisionRef EvaluatorRevision,
    string AuthorKind,
    AssessmentOutcome Outcome,
    int? Severity,
    IReadOnlyList<EvidenceArtifactRef> Evidence,
    Guid? SupersedesAssessmentId);
