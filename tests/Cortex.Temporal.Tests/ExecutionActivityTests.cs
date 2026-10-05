using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Activities;
using Temporalio.Testing;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class ExecutionActivityTests
{
    [Fact]
    public async Task ActivityPassesHostScopeAndCancellation()
    {
        using var cancel = new CancellationTokenSource();
        var environment = new ActivityEnvironment { CancellationTokenSource = cancel };
        var scope = new ArtifactScope("installation", new("runtime", "character", "channel", "account", "conversation"), "execution", "revision");
        var reference = new ArtifactRef(1, scope, "snapshot", new string('0', 64));
        var command = new StepCommand(new(scope, "prompt", 2), reference);
        var driver = new RecordingDriver();
        await environment.RunAsync(() => new ExecutionActivities(driver).AdvanceAsync(command));
        Assert.Equal(command, driver.Command);
        Assert.Equal(cancel.Token, driver.Token);
    }
    private sealed class RecordingDriver : IFlowExecutionDriver
    {
        internal StepCommand? Command { get; private set; } internal CancellationToken Token { get; private set; }
        public Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token)
        { Command = command; Token = token; return Task.FromResult(new ExecutionDescriptor(command.Key.Scope, command.SnapshotRef, null, 3, ExecutionStatus.Completed, StepEffect.Pure, null, null, null)); }
        public Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token) => throw new NotSupportedException();
        public Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token) => throw new NotSupportedException();
        public Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token) => throw new NotSupportedException();
    }
}
