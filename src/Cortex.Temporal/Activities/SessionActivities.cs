using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Temporalio.Activities;

namespace Cortex.Temporal.Activities;

public sealed class SessionActivities(ISessionArtifactServices artifacts)
{
    [Activity]
    public Task<ProjectPublicationDescriptor> ReadPublicationAsync(ArtifactRef projectRef) => artifacts.ReadPublicationAsync(projectRef, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<CommittedSessionAdmission?> FindAdmissionAsync(string installation, SessionKey session, AdmissionDescriptor input) => artifacts.FindAdmissionAsync(installation, session, input, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<CommittedSessionAdmission> CommitAdmissionAsync(AdmissionCommitCommand command) => artifacts.CommitAdmissionAsync(command, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<PreparedSessionExecution> PrepareExecutionAsync(DurableSessionBatch batch, string state, long version, string installation, SessionKey session) =>
        artifacts.PrepareExecutionAsync(batch, state, version, installation, session, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<CommittedSessionOutcome> CommitOutcomeAsync(OutcomeCommitCommand command) => artifacts.CommitOutcomeAsync(command, ActivityExecutionContext.Current.CancellationToken);
    [Activity]
    public Task<DurableSessionState> ImportIdleAsync(ArtifactRef importRef, ProjectPublicationDescriptor publication) => artifacts.ImportIdleAsync(importRef, publication, ActivityExecutionContext.Current.CancellationToken);
}
