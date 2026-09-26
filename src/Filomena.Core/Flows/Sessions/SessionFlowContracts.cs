using System.Text.Json;

namespace Filomena.Core.Flows.Sessions;

public sealed record SessionFlowProject(
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    JsonElement Document);

public sealed record SessionKey(
    string RuntimeId,
    string CharacterId,
    string ChannelId,
    string AccountId,
    string NativeConversationId);

public enum SessionEntryKind { Dm, Group }

public enum SessionMessageDirection { Inbound, Outbound }

public sealed record SessionMessageEnvelope(
    string ProviderMessageId,
    string ChannelId,
    string AccountId,
    string NativeConversationId,
    DateTimeOffset Timestamp,
    SessionMessageDirection Direction,
    SessionEntryKind EntryKind,
    JsonElement Message);

public enum SessionAdmissionStatus { Accepted, Duplicate, Rejected }

public sealed record SessionAdmission(
    SessionAdmissionStatus Status,
    long? AdmissionWatermark,
    string? Reason);

public sealed record SessionFlowProjectSnapshot(
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    string InitialStateId,
    IReadOnlyList<string> StateIds);

public sealed record SessionSnapshot(
    SessionKey Session,
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    string CurrentStateId,
    long StateVersion,
    DateTimeOffset EnteredAt,
    long AdmissionWatermark,
    int HistoryCount,
    int OpenBufferCount,
    int PendingExecutionCount);

public sealed record SessionHistoryEntry(
    long AdmissionWatermark,
    string ProviderMessageId,
    DateTimeOffset Timestamp,
    SessionMessageDirection Direction,
    SessionEntryKind EntryKind,
    string ChannelId,
    string AccountId,
    string NativeConversationId,
    JsonElement Message);

public enum SessionBatchCloseReason
{
    Immediate,
    QuietPeriod,
    MaxMessages,
    MaxWait,
}

public sealed record SessionFlowOutcome(
    string ExecutionId,
    string BatchId,
    SessionKey Session,
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    string StateId,
    long StateVersion,
    SessionEntryKind EntryKind,
    SessionBatchCloseReason CloseReason,
    IReadOnlyList<string> ProviderMessageIds,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool Success,
    string? Error,
    string? TransitionTargetStateId,
    bool TransitionApplied,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Outputs)
{
    public IReadOnlyDictionary<string, ParticipationReassessment> InitialInterpretations { get; init; } =
        new Dictionary<string, ParticipationReassessment>();
}

public sealed record SessionTraceEvent(
    string EventName,
    DateTimeOffset Timestamp,
    string ExecutionId,
    string BatchId,
    SessionKey Session,
    string ProjectId,
    long ProjectVersion,
    string ProjectRevision,
    string StateId,
    long StateVersion,
    string? NodeId,
    string? Exit,
    string? TransitionTargetStateId,
    SessionBatchCloseReason? CloseReason,
    double DurationMilliseconds,
    string? ErrorCode)
{
    public string BatchStateId => StateId;
    public long BatchStateVersion => StateVersion;
    public string CurrentSessionStateId { get; init; } = StateId;
    public long CurrentSessionStateVersion { get; init; } = StateVersion;
    public IReadOnlyDictionary<string, string>? MemoryMetadata { get; init; }
}

public sealed record SessionFlowProjectIssue(
    string StateId,
    string? EntryId,
    string NodeId,
    string Port,
    string Reason);

public sealed class SessionFlowProjectException(IReadOnlyList<SessionFlowProjectIssue> issues)
    : Exception("Session flow project is not executable.")
{
    public IReadOnlyList<SessionFlowProjectIssue> Issues { get; } = issues;
}
