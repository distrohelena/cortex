using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Hosting;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Cortex.Temporal.Workflows;

[Workflow]
public sealed class FlowExecutionWorkflowV1
{
    private ExecutionDescriptor? current;
    private WorkflowActivityPolicy policy = null!;
    private FeedbackResolution? selected;
    private FeedbackSubmission? selectedSubmission;
    private bool cancelRequested;
    private readonly Dictionary<string, (FeedbackSubmission Submission, FeedbackReceipt Receipt)> receipts = new(StringComparer.Ordinal);

    [WorkflowRun]
    public async Task<ExecutionDescriptor> RunAsync(ExecutionWorkflowInput input)
    {
        policy = input.ActivityPolicy;
        policy.Validate();
        try { current = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.InitializeAsync(input.Start), Options(StepEffect.Artifact)); }
        catch (ActivityFailureException) { throw Failure("execution-initialization-failed"); }
        while (current.Status is ExecutionStatus.Ready or ExecutionStatus.WaitingForHuman)
        {
            if (current.Status == ExecutionStatus.WaitingForHuman)
            {
                var request = current.Feedback ?? throw Failure("missing-feedback-request");
                if (selected is null && !cancelRequested)
                {
                    if (request.TimeoutSeconds is { } seconds)
                    {
                        var answered = await Workflow.WaitConditionAsync(() => selected is not null || cancelRequested, TimeSpan.FromSeconds(seconds));
                        if (!answered && selected is null) selected = new(request, "timeout", null);
                    }
                    else await Workflow.WaitConditionAsync(() => selected is not null || cancelRequested);
                }
                selected ??= new(request, "cancelled", null);
                var resolution = selected;
                var submission = selectedSubmission;
                try
                {
                    var command = Command();
                    current = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.ResolveFeedbackAsync(command, resolution), Options(StepEffect.Artifact));
                    if (submission is not null)
                        receipts.Add(submission.OperationId, (submission, new(submission.OperationId, submission.RequestId, resolution.Status, null)));
                }
                catch (ActivityFailureException)
                {
                    // Authorization may be revoked between validation and publication. Never publish a fabricated answer.
                    if (submission is not null)
                        receipts.Add(submission.OperationId, (submission, new(submission.OperationId, submission.RequestId, "rejected", "feedback-resolution-failed")));
                    current = current with { Status = ExecutionStatus.Failed, Diagnostic = "feedback-resolution-failed" };
                }
                selected = null; selectedSubmission = null;
                continue;
            }
            if (cancelRequested)
            {
                current = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.CancelAsync(current.SnapshotRef), Options(StepEffect.Artifact));
                break;
            }
            var step = Command();
            try
            {
                current = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.AdvanceAsync(step), Options(current.Effect));
            }
            catch (ActivityFailureException)
            {
                ExecutionDescriptor? recovered = null;
                try { recovered = await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.RecoverAsync(step.Key), Options(StepEffect.Artifact)); }
                catch (ActivityFailureException) { /* A recovery failure must not cause another paid effect or an endless workflow-task retry. */ }
                current = recovered ?? current with { Status = current.Effect == StepEffect.Send ? ExecutionStatus.ReconciliationRequired : ExecutionStatus.Failed, Diagnostic = "step-outcome-unconfirmed" };
            }
        }
        await Workflow.WaitConditionAsync(() => Workflow.AllHandlersFinished);
        return current;
    }

    [WorkflowQuery]
    public ExecutionDescriptor? Status() => current;

    [WorkflowUpdateValidator(nameof(SubmitFeedbackAsync))]
    public void ValidateSubmission(FeedbackSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.OperationId) || string.IsNullOrWhiteSpace(submission.AuthenticatedActorRef)) throw Failure("invalid-feedback-submission");
        if (receipts.TryGetValue(submission.OperationId, out var prior))
        {
            if (prior.Submission != submission) throw Failure("feedback-operation-conflict");
            return;
        }
        if (selectedSubmission?.OperationId == submission.OperationId)
        {
            if (selectedSubmission != submission) throw Failure("feedback-operation-conflict");
            return;
        }
        if (current is not { Status: ExecutionStatus.WaitingForHuman, Feedback: { } request }
            || current.Scope != submission.Scope || request.RequestId != submission.RequestId || request.RequestDigest != submission.RequestDigest
            || submission.ResponseRef.Scope != submission.Scope || selected is not null || cancelRequested)
            throw Failure("feedback-request-not-active");
    }

    [WorkflowUpdate]
    public async Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission)
    {
        ValidateSubmission(submission);
        if (receipts.TryGetValue(submission.OperationId, out var prior)) return prior.Receipt;
        if (selectedSubmission?.OperationId != submission.OperationId)
        {
            await Workflow.ExecuteActivityAsync((ExecutionActivities a) => a.ValidateFeedbackAsync(submission), Options(StepEffect.Artifact));
            // Another handler or the timer may have won while authorization was running.
            ValidateSubmission(submission);
            if (selectedSubmission?.OperationId != submission.OperationId)
            {
                selectedSubmission = submission;
                selected = new(current!.Feedback!, "answered", submission.ResponseRef);
            }
        }
        await Workflow.WaitConditionAsync(() => receipts.ContainsKey(submission.OperationId));
        return receipts[submission.OperationId].Receipt;
    }

    [WorkflowUpdate]
    public async Task CancelAsync(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw Failure("invalid-cancel-operation");
        cancelRequested = true;
        await Workflow.WaitConditionAsync(() => current is not null && current.Status is not (ExecutionStatus.Ready or ExecutionStatus.WaitingForHuman));
    }

    [WorkflowSignal]
    public Task RequestCancellationAsync(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw Failure("invalid-cancel-operation");
        cancelRequested = true;
        return Task.CompletedTask;
    }

    private StepCommand Command() => new(new(current!.Scope, current.NodeId ?? throw Failure("missing-step-node"), current.Ordinal), current.SnapshotRef);
    private ActivityOptions Options(StepEffect effect) => TemporalActivityPolicy.For(effect, policy);
    private static ApplicationFailureException Failure(string code) => new(code, nonRetryable: true);
}
