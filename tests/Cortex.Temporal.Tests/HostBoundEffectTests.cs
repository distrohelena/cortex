using Cortex.Core.Flows.Durable;
using Cortex.Core.Memory;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Hosting;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class HostBoundEffectTests
{
    [Fact]
    public async Task ForeignSessionOrRevisionDoesNotResolveAndHarnessRequiresExplicitOptIn()
    {
        var project = SessionWorkflowTests.Project(); var session = SessionWorkflowTests.Session("bound"); var completion = new SessionWorkflowTests.Bindings(project);
        var binding = new HostExecutionSessionBinding("installation", session, project, completion, new UnavailableMemoryReader(), null, TimeProvider.System);
        Assert.Throws<ArgumentException>(() => new HostBoundExecutionBindings([binding]));
        var registry = new HostBoundExecutionBindings([binding], allowHarnessCompletions: true);
        var scope = new ArtifactScope("installation", session, "execution", project.ProjectRevision);
        Assert.Equal(scope, (await registry.ResolveAsync(scope, TestContext.Current.CancellationToken)).Scope);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await registry.ResolveAsync(scope with { Session = session with { AccountId = "foreign" } }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await registry.ResolveAsync(scope with { ProjectRevision = "foreign" }, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task PrivateProviderErrorIsSanitizedAtActivityBoundary()
    {
        var driver = new ThrowingDriver(); var scope = new ArtifactScope("installation", SessionWorkflowTests.Session("bound"), "execution", "revision");
        var command = new StepCommand(new(scope, "prompt", 1), new(1, scope, "snapshot", new string('0', 64)));
        var environment = new ActivityEnvironment();
        var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => environment.RunAsync(() => new ExecutionActivities(driver).AdvanceAsync(command)));
        Assert.DoesNotContain("PRIVATE PROVIDER SECRET", failure.ToString()); Assert.Equal("execution-activity-failed", failure.Message);
        Assert.True(failure.NonRetryable);
    }
    private sealed class ThrowingDriver : IFlowExecutionDriver
    {
        public Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token) => throw new InvalidOperationException("PRIVATE PROVIDER SECRET");
        public Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token) => throw new NotSupportedException();
        public Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token) => throw new NotSupportedException();
        public Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token) => throw new NotSupportedException();
    }
}
