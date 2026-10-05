using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

public sealed record AdmissionCommitCommand(DurableSessionState Before, AdmissionDescriptor Input, SessionPolicyDecision Decision);
public sealed record CommittedSessionAdmission(AdmissionDescriptor Input, SessionPolicyDecision Decision, LedgerAdmission Receipt);
public sealed record PreparedSessionExecution(ExecutionStart Start, DateTimeOffset StartedAt, string? Diagnostic);
public sealed record OutcomeCommitCommand(DurableSessionBatch Batch, ExecutionDescriptor Execution, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, string CurrentStateId, long CurrentStateVersion, long AdmissionWatermark);
public sealed record CommittedSessionOutcome(ArtifactRef OutcomeRef, string? TransitionTargetStateId, bool TransitionApplied, long AdmissionWatermark, int HistoryCount);

public interface ISessionArtifactServices
{
    Task<ProjectPublicationDescriptor> ReadPublicationAsync(ArtifactRef projectRef, CancellationToken token);
    Task<CommittedSessionAdmission?> FindAdmissionAsync(string installation, SessionKey session, AdmissionDescriptor input, CancellationToken token);
    Task<CommittedSessionAdmission> CommitAdmissionAsync(AdmissionCommitCommand command, CancellationToken token);
    Task<PreparedSessionExecution> PrepareExecutionAsync(DurableSessionBatch batch, string currentState, long currentVersion, string installation, SessionKey session, CancellationToken token);
    Task<CommittedSessionOutcome> CommitOutcomeAsync(OutcomeCommitCommand command, CancellationToken token);
    Task<DurableSessionState> ImportIdleAsync(ArtifactRef importRef, ProjectPublicationDescriptor publication, CancellationToken token);
}
