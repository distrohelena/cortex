using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

public sealed record ArtifactScope(string InstallationId, SessionKey Session, string ExecutionId, string ProjectRevision);
public sealed record ArtifactRef(int Version, ArtifactScope Scope, string Key, string Sha256);
public sealed record StepKey(ArtifactScope Scope, string NodeId, long Ordinal);
public enum ExecutionStatus { Ready, WaitingForHuman, Completed, Failed, Cancelled, ReconciliationRequired }
public enum StepEffect { Pure, Artifact, Inference, Memory, Boundary, Send, Feedback }
public sealed record FeedbackDescriptor(string RequestId, ArtifactRef RequestRef, string RequestDigest,
    string NodeId, long Ordinal, int? TimeoutSeconds);
public sealed record ExecutionDescriptor(ArtifactScope Scope, ArtifactRef SnapshotRef, string? NodeId, long Ordinal,
    ExecutionStatus Status, StepEffect Effect, FeedbackDescriptor? Feedback, ArtifactRef? OutcomeRef, string? Diagnostic);
public sealed record ExecutionStart(ArtifactScope Scope, string BatchId, ArtifactRef ProjectRef, ArtifactRef BatchRef,
    string StateId, long StateVersion, SessionEntryKind EntryKind);
public sealed record StepCommand(StepKey Key, ArtifactRef SnapshotRef);
public sealed record ArtifactPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record PageRequest(string? Cursor = null, int Limit = 100)
{
    public void Validate()
    {
        if (Limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(Limit));
    }
}
public sealed record FeedbackSubmission(string OperationId, ArtifactScope Scope, string RequestId,
    string RequestDigest, ArtifactRef ResponseRef, string AuthenticatedActorRef);
public sealed record SessionImportRequest(string CutoverId, string InstallationId, SessionSnapshot Snapshot,
    IReadOnlyList<SessionHistoryEntry> History, IReadOnlyList<string> SeenProviderIds,
    string SourceEpoch, long SourceCursor, string JournalFingerprint);
