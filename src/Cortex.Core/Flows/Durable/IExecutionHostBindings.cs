namespace Cortex.Core.Flows.Durable;

public interface IExecutionHostBindings
{
    ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token);
}
public interface IFlowExecutionDriver
{
    Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token);
    Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token);
    Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token);
    Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token);
    Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token);
}
