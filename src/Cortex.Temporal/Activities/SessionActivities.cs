using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Temporalio.Activities;

namespace Cortex.Temporal.Activities;

public sealed class SessionActivities(ISessionArtifactServices artifacts)
{
    [Activity]
    public Task<ProjectPublicationDescriptor> ReadPublicationAsync(ArtifactRef projectRef) => ActivityFailureBoundary.RunAsync(() => artifacts.ReadPublicationAsync(projectRef, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
    [Activity]
    public Task<CommittedSessionAdmission?> FindAdmissionAsync(string installation, SessionKey session, AdmissionDescriptor input) => ActivityFailureBoundary.RunAsync(() => artifacts.FindAdmissionAsync(installation, session, input, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
    [Activity]
    public Task<CommittedSessionAdmission> CommitAdmissionAsync(AdmissionCommitCommand command) => ActivityFailureBoundary.RunAsync(() => artifacts.CommitAdmissionAsync(command, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
    [Activity]
    public Task<PreparedSessionExecution> PrepareExecutionAsync(DurableSessionBatch batch, string state, long version, string installation, SessionKey session) =>
        ActivityFailureBoundary.RunAsync(() => artifacts.PrepareExecutionAsync(batch, state, version, installation, session, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
    [Activity]
    public Task<CommittedSessionOutcome> CommitOutcomeAsync(OutcomeCommitCommand command) => ActivityFailureBoundary.RunAsync(() => artifacts.CommitOutcomeAsync(command, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
    [Activity]
    public Task<DurableSessionState> ImportIdleAsync(ArtifactRef importRef, ProjectPublicationDescriptor publication) => ActivityFailureBoundary.RunAsync(() => artifacts.ImportIdleAsync(importRef, publication, ActivityExecutionContext.Current.CancellationToken), "session-activity-failed");
}
