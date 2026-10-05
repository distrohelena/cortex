using Cortex.Core.Flows.Durable;
using Cortex.Temporal.Hosting;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class ActivityPolicyTests
{
    internal static WorkflowActivityPolicy Policy() => new("installation-effects", Enum.GetValues<StepEffect>().ToDictionary(x => x, _ => TimeSpan.FromSeconds(30)));
    [Theory]
    [InlineData(StepEffect.Inference)] [InlineData(StepEffect.Send)] [InlineData(StepEffect.Memory)] [InlineData(StepEffect.Boundary)]
    public void PaidAndSendEffectsHaveOneAttempt(StepEffect effect)
    {
        var options = TemporalActivityPolicy.For(effect, Policy());
        Assert.Equal(1, options.RetryPolicy!.MaximumAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), options.StartToCloseTimeout);
        Assert.Equal("installation-effects", options.TaskQueue);
    }
    [Theory] [InlineData(StepEffect.Pure)] [InlineData(StepEffect.Artifact)] [InlineData(StepEffect.Feedback)]
    public void ArtifactEffectsUseFiveAttemptsWithOneToThirtySecondsBackoff(StepEffect effect)
    {
        var policy = TemporalActivityPolicy.For(effect, Policy()).RetryPolicy!;
        Assert.Equal(5, policy.MaximumAttempts); Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaximumInterval);
    }
    [Fact]
    public void OptionsRequireExplicitNamespaceQueuesAndStore()
    {
        var options = new TemporalRuntimeOptions("installation", "localhost:7233", "test-namespace", "workflows", "installation-effects", "C:/private/artifacts");
        options.Validate();
        foreach (var invalid in new[] { options with { TargetHost = "" }, options with { Namespace = "" }, options with { ArtifactDirectory = "" },
            options with { EffectTaskQueue = "" }, options with { WorkflowTaskQueue = "" }, options with { MaxPendingBatches = 0 }, options with { MaxPendingBatches = 4097 } })
            Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Throws<ArgumentException>(() => TemporalActivityPolicy.For(StepEffect.Inference, Policy() with { StartToCloseTimeouts = new Dictionary<StepEffect, TimeSpan>() }));
    }
}
