using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

public sealed record AdmissionDescriptor(string InputKey, ArtifactRef InputRef, string Digest,
    SessionEntryKind EntryKind, SessionMessageDirection Direction, DateTimeOffset Timestamp);
public sealed record SessionEntryPolicy(SessionEntryKind Kind, string EntryId, string? AccumulatorNodeId,
    TimeSpan? QuietPeriod, int? MaxMessages, TimeSpan? MaxWait, bool HasSend);
public sealed record SessionStatePolicy(string StateId, SessionEntryPolicy Entry);
public sealed record ProjectPublicationDescriptor(SessionFlowProjectSnapshot Snapshot, ArtifactRef ProjectRef, IReadOnlyList<SessionStatePolicy> Entries);
public sealed record DurableSessionBatch(string BatchId, string ExecutionId, ProjectPublicationDescriptor Publication,
    string StateId, long StateVersion, SessionEntryPolicy Entry, IReadOnlyList<AdmissionDescriptor> Inputs,
    long FirstWatermark, long HistoryWatermark, DateTimeOffset ClosedAt, SessionBatchCloseReason CloseReason);
public sealed record DurableSessionBuffer(DurableSessionBatch Batch, DateTimeOffset? QuietDueAt, DateTimeOffset? MaxWaitDueAt);

public sealed record DurableSessionState(string InstallationId, SessionKey Session, ProjectPublicationDescriptor Publication,
    string CurrentStateId, long StateVersion, DateTimeOffset EnteredAt, long AdmissionWatermark, int HistoryCount,
    int MaxPendingBatches, long NextBatchOrdinal, long NextReviewOrdinal, long NextPreviewOrdinal,
    SessionEntryKind? EntryKind, IReadOnlyList<DurableSessionBuffer> Buffers, IReadOnlyList<DurableSessionBatch> Queue,
    DurableSessionBatch? ActiveBatch)
{
    public ArtifactRef? ImportedManifest { get; init; }
    public DurableActiveReview? ActiveReview { get; init; }
    public IReadOnlyDictionary<string, ArtifactRef> ReviewReceipts { get; init; } = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal);
    public static DurableSessionState Create(string installation, SessionKey session, ProjectPublicationDescriptor project, int limit, DateTimeOffset now)
    {
        if (limit is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(limit));
        _ = DurableIdentity.SessionWorkflowId(installation, session);
        return new(installation, session, project, project.Snapshot.InitialStateId, 0, now, 0, 0, limit, 0, 0, 0, null, [], [], null);
    }
    public SessionSnapshot Snapshot() => new(Session, Publication.Snapshot.ProjectId, Publication.Snapshot.ProjectVersion,
        Publication.Snapshot.ProjectRevision, CurrentStateId, StateVersion, EnteredAt, AdmissionWatermark, HistoryCount,
        Buffers.Count, Queue.Count + (ActiveBatch is null ? 0 : 1));
}

public sealed record SessionPolicyDecision(DurableSessionState State, SessionAdmission Admission, DurableSessionBatch? Batch);
public sealed record DurableActiveReview(PreparedSessionReview Prepared, string WorkflowId);
