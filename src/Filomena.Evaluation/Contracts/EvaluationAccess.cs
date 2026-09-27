namespace Filomena.Evaluation.Contracts;

public enum EvaluationRole
{
    Proposer,
    Evaluator,
    Operator
}

public enum EvaluationOperation
{
    Propose,
    Evaluate,
    Operate,
    ReadRevision,
    ReadArtifact,
    RegisterCase,
    RegisterTrial,
    AppendAssessment,
    RedactArtifact
}

/// <summary>
/// An immutable authorization decision resolved by the trusted host. It is not part of
/// an experiment manifest and has no public constructor for deserialization.
/// </summary>
public sealed class EvaluationAccess
{
    private readonly HashSet<EvaluationOperation> operations;

    private EvaluationAccess(string principal, EvaluationScope scope, EvaluationRole role)
    {
        Principal = principal;
        Scope = scope;
        Role = role;
        operations = role switch
        {
            EvaluationRole.Proposer =>
            [EvaluationOperation.Propose, EvaluationOperation.ReadRevision, EvaluationOperation.ReadArtifact,
                EvaluationOperation.RegisterCase],
            EvaluationRole.Evaluator =>
            [EvaluationOperation.Evaluate, EvaluationOperation.ReadRevision, EvaluationOperation.ReadArtifact,
                EvaluationOperation.RegisterCase, EvaluationOperation.RegisterTrial, EvaluationOperation.AppendAssessment],
            EvaluationRole.Operator => [EvaluationOperation.Operate, EvaluationOperation.RedactArtifact],
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
    }

    public string Principal { get; }
    public EvaluationScope Scope { get; }
    public EvaluationRole Role { get; }
    public bool CanReadHoldout => Role == EvaluationRole.Evaluator;

    /// <summary>Creates an access decision from the host's already-resolved identity and role.</summary>
    public static EvaluationAccess FromHost(string principal, EvaluationScope scope, EvaluationRole role)
    {
        if (string.IsNullOrWhiteSpace(principal) || principal.Length > 256 || principal.Any(char.IsControl))
            throw new ArgumentException("A valid host principal is required.", nameof(principal));
        if (scope.WorkspaceId == Guid.Empty || scope.ProjectId == Guid.Empty)
            throw new ArgumentException("A valid evaluation scope is required.", nameof(scope));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        return new(principal, scope, role);
    }

    public bool Can(EvaluationOperation operation) => operations.Contains(operation);

    public bool CanReadArtifact(bool isHoldout) => Can(EvaluationOperation.ReadArtifact) && (!isHoldout || CanReadHoldout);

    public void Demand(EvaluationOperation operation)
    {
        if (!Can(operation))
            throw new UnauthorizedAccessException("Evaluation operation is not authorized.");
    }

    public void DemandScope(EvaluationScope scope)
    {
        if (Scope != scope)
            throw new UnauthorizedAccessException("Evaluation scope is not authorized.");
    }

    public void DemandArtifactRead(bool isHoldout)
    {
        if (!CanReadArtifact(isHoldout))
            throw new UnauthorizedAccessException("Artifact is not available to this principal.");
    }
}
