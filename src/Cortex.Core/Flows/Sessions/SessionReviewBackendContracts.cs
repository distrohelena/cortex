using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

public interface ISessionReviewBackend
{
    Task<SessionParticipationReviewResult> ReviewAsync(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, CancellationToken token);
    Task<SessionResponsePreviewResult> PreviewAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token);
    Task<SessionReviewedResponseResult> ContinueAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token);
}

/// <summary>Host-issued private completion data; never reconstruct this from a public review report.</summary>
internal sealed record DurableParticipationReviewV1(ArtifactScope Scope, ParticipationReviewDefinition Definition,
    SessionParticipationReviewResult Result, IReadOnlyDictionary<string, string> CompletedPromptResponses);

public enum SessionExecutionPurpose { Normal, ParticipationReview, ResponsePreview, ReviewedResponse }

public sealed record SessionReviewPolicy(int MessageHorizon = 32, int AttemptBudget = 3, int Capacity = 128)
{
    public void Validate()
    {
        if (MessageHorizon < 1 || AttemptBudget < 1 || Capacity < 1) throw new ArgumentOutOfRangeException(nameof(MessageHorizon));
    }
}

/// <summary>Private request artifact, including original identity and review cut.</summary>
public sealed record SessionReviewOperationV1(string OperationId, string OriginalMessageId, SessionExecutionPurpose Purpose,
    ParticipationReviewDefinition? Definition, ArtifactRef? ReviewRef)
{
    public SessionReviewPolicy Policy { get; init; } = new();
}
public sealed record ReviewPreparationCommand(string InstallationId, SessionSnapshot Snapshot, ArtifactRef PublicationRef, ArtifactRef RequestRef, long Ordinal);
public sealed record PreparedSessionReview(ExecutionStart Start, SessionSnapshot Snapshot, SessionExecutionPurpose Purpose, ArtifactRef RequestRef, string OperationId);
public sealed record ReviewCommitCommand(PreparedSessionReview Prepared, ExecutionDescriptor Execution, SessionSnapshot Current, DateTimeOffset CompletedAt);
public sealed record CommittedSessionReview(ArtifactRef ResultRef, string? TransitionTargetStateId, bool TransitionApplied, long AdmissionWatermark, int HistoryCount);

public interface ISessionReviewArtifactServices
{
    Task<PreparedSessionReview> PrepareReviewAsync(ReviewPreparationCommand command, CancellationToken token);
    Task<CommittedSessionReview> CommitReviewAsync(ReviewCommitCommand command, CancellationToken token);
}
