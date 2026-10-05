using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Hosting;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Cortex.Temporal.Workflows;

[Workflow]
public sealed class SessionWorkflowV1
{
    private DurableSessionState state = null!;
    private SessionWorkflowInput input = null!;
    private bool ready;
    private bool mutating;
    private long change;

    [WorkflowRun]
    public async Task RunAsync(SessionWorkflowInput workflowInput)
    {
        input = workflowInput;
        input.ActivityPolicy.Validate();
        if (input.RotationEventLimit < 20) throw Failure("invalid-rotation-limit");
        var publication = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.ReadPublicationAsync(input.ProjectRef), Options());
        state = input.CarriedState ?? DurableSessionState.Create(input.InstallationId, input.Session, publication, input.MaxPendingBatches, Now);
        if (state.InstallationId != input.InstallationId || state.Session != input.Session || state.Publication.ProjectRef != input.ProjectRef)
            throw Failure("foreign-session-state");
        ready = true;
        while (true)
        {
            if (!mutating && SessionBatchPolicy.CanRotate(state, Workflow.AllHandlersFinished)
                && (Workflow.ContinueAsNewSuggested || Workflow.CurrentHistoryLength >= input.RotationEventLimit))
            {
                var next = input with { ProjectRef = state.Publication.ProjectRef, CarriedState = state };
                throw Workflow.CreateContinueAsNewException((SessionWorkflowV1 w) => w.RunAsync(next));
            }
            await Acquire();
            DurableSessionBatch? batch = null;
            PreparedSessionExecution? prepared = null;
            try
            {
                foreach (var buffer in state.Buffers.ToArray())
                {
                    var due = Earliest(buffer);
                    if (due is { } time && time <= Now)
                    {
                        var reason = buffer.QuietDueAt is { } quiet && quiet <= Now && (buffer.MaxWaitDueAt is null || quiet <= buffer.MaxWaitDueAt)
                            ? SessionBatchCloseReason.QuietPeriod : SessionBatchCloseReason.MaxWait;
                        state = SessionBatchPolicy.CloseBuffer(state, buffer.Batch.BatchId, reason, Now).State;
                    }
                }
                if (state.Queue.Count > 0)
                {
                    batch = state.Queue[0];
                    state = state with { ActiveBatch = batch, Queue = state.Queue.Skip(1).ToArray() };
                    prepared = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.PrepareExecutionAsync(batch, state.CurrentStateId, state.StateVersion, state.InstallationId, state.Session), Options());
                }
            }
            finally { Release(); }
            if (batch is not null && prepared is not null)
            {
                ExecutionDescriptor descriptor;
                if (prepared.Diagnostic is not null)
                {
                    descriptor = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.InitializeAsync(prepared.Start), Options());
                    descriptor = descriptor with { Status = ExecutionStatus.Failed, Diagnostic = prepared.Diagnostic };
                }
                else descriptor = await Workflow.ExecuteChildWorkflowAsync((FlowExecutionWorkflowV1 w) => w.RunAsync(new(prepared.Start, input.ActivityPolicy)),
                    new() { Id = DurableIdentity.ExecutionWorkflowId(prepared.Start.Scope, batch.BatchId), TaskQueue = input.WorkflowTaskQueue });
                await Acquire();
                try
                {
                    var command = new OutcomeCommitCommand(batch, descriptor, prepared.StartedAt, Now, state.CurrentStateId, state.StateVersion, state.AdmissionWatermark);
                    var committed = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.CommitOutcomeAsync(command), Options());
                    state = state with { ActiveBatch = null, AdmissionWatermark = committed.AdmissionWatermark, HistoryCount = committed.HistoryCount,
                        CurrentStateId = committed.TransitionApplied ? committed.TransitionTargetStateId! : state.CurrentStateId,
                        StateVersion = committed.TransitionApplied ? checked(state.StateVersion + 1) : state.StateVersion,
                        EnteredAt = committed.TransitionApplied ? command.CompletedAt : state.EnteredAt };
                }
                finally { Release(); }
                continue;
            }
            var observed = change;
            var nextDue = state.Buffers.Select(Earliest).Where(t => t is not null).Min();
            bool Changed() => change != observed || state.Queue.Count > 0 || (SessionBatchPolicy.CanRotate(state, Workflow.AllHandlersFinished)
                && (Workflow.ContinueAsNewSuggested || Workflow.CurrentHistoryLength >= input.RotationEventLimit));
            if (nextDue is { } deadline) await Workflow.WaitConditionAsync(Changed, deadline > Now ? deadline - Now : TimeSpan.FromMilliseconds(1));
            else await Workflow.WaitConditionAsync(Changed);
        }
    }

    [WorkflowQuery]
    public SessionSnapshot? Snapshot() => ready ? state.Snapshot() : null;
    [WorkflowQuery]
    public ActiveExecutionDescriptor? ActiveExecution() => ready && state.ActiveBatch is { } active
        ? new(new(state.InstallationId, state.Session, active.ExecutionId, active.Publication.Snapshot.ProjectRevision), active.BatchId,
            DurableIdentity.ExecutionWorkflowId(new(state.InstallationId, state.Session, active.ExecutionId, active.Publication.Snapshot.ProjectRevision), active.BatchId)) : null;

    [WorkflowUpdate]
    public async Task<SessionAdmission> ReceiveAsync(AdmissionDescriptor admission)
    {
        await Workflow.WaitConditionAsync(() => ready);
        await Acquire();
        try
        {
            var previous = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.FindAdmissionAsync(state.InstallationId, state.Session, admission), Options());
            if (previous is not null)
            {
                if (previous.Receipt.Watermark == state.AdmissionWatermark + 1) state = previous.Decision.State;
                else if (previous.Receipt.Watermark > state.AdmissionWatermark) throw Failure("admission-watermark-gap");
                return new(SessionAdmissionStatus.Duplicate, previous.Receipt.Watermark, null);
            }
            var decision = SessionBatchPolicy.Admit(state, admission, Now);
            if (decision.Admission.Status != SessionAdmissionStatus.Accepted) return decision.Admission;
            CommittedSessionAdmission committed;
            try { committed = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.CommitAdmissionAsync(new(state, admission, decision)), Options()); }
            catch (ActivityFailureException)
            {
                committed = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.FindAdmissionAsync(state.InstallationId, state.Session, admission), Options())
                    ?? throw Failure("admission-not-committed");
            }
            state = committed.Decision.State;
            return committed.Decision.Admission;
        }
        finally { Release(); }
    }

    [WorkflowUpdate]
    public async Task<SessionFlowProjectSnapshot> PublishAsync(ProjectPublicationDescriptor publication)
    {
        await Workflow.WaitConditionAsync(() => ready);
        await Acquire();
        try
        {
            var verified = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.ReadPublicationAsync(publication.ProjectRef), Options());
            var previous = state.Publication.Snapshot;
            if (verified.ProjectRef == state.Publication.ProjectRef) return previous;
            if (verified.Snapshot.ProjectId != previous.ProjectId || verified.Snapshot.ProjectVersion <= previous.ProjectVersion || verified.Snapshot.ProjectRevision == previous.ProjectRevision)
                throw Failure("invalid-project-publication");
            state = state with { Publication = verified };
            return verified.Snapshot;
        }
        finally { Release(); }
    }

    [WorkflowUpdate]
    public async Task<SessionSnapshot> ImportIdleAsync(ArtifactRef importRef)
    {
        await Workflow.WaitConditionAsync(() => ready);
        await Acquire();
        try
        {
            if (state.ImportedManifest == importRef) return state.Snapshot();
            if (state.AdmissionWatermark != 0 || state.HistoryCount != 0 || state.ActiveBatch is not null || state.Buffers.Count != 0 || state.Queue.Count != 0
                || importRef.Scope.InstallationId != state.InstallationId || importRef.Scope.Session != state.Session)
                throw Failure("session-not-empty-for-import");
            var restored = await Workflow.ExecuteActivityAsync((SessionActivities a) => a.ImportIdleAsync(importRef, state.Publication), Options());
            state = restored with { MaxPendingBatches = input.MaxPendingBatches };
            return state.Snapshot();
        }
        finally { Release(); }
    }

    [WorkflowUpdate]
    public async Task CancelExecutionAsync(string executionId)
    {
        await Workflow.WaitConditionAsync(() => ready);
        var active = ActiveExecution();
        if (active is null || active.Scope.ExecutionId != executionId) throw Failure("execution-not-active");
        await Workflow.GetExternalWorkflowHandle<FlowExecutionWorkflowV1>(active.WorkflowId).SignalAsync(w => w.RequestCancellationAsync("cancel-" + executionId));
    }

    private async Task Acquire() { await Workflow.WaitConditionAsync(() => !mutating); mutating = true; }
    private void Release() { mutating = false; change++; }
    private ActivityOptions Options() => TemporalActivityPolicy.For(StepEffect.Artifact, input.ActivityPolicy);
    private static DateTimeOffset Now => new(Workflow.UtcNow);
    private static DateTimeOffset? Earliest(DurableSessionBuffer b) => b.QuietDueAt is { } q && (b.MaxWaitDueAt is null || q <= b.MaxWaitDueAt) ? q : b.MaxWaitDueAt;
    private static ApplicationFailureException Failure(string code) => new(code, nonRetryable: true);
}
