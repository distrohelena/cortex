using Cortex.Core.Flows.Durable;
using Temporalio.Common;
using Temporalio.Workflows;

namespace Cortex.Temporal.Hosting;

public static class TemporalActivityPolicy
{
    public static ActivityOptions For(StepEffect effect, WorkflowActivityPolicy policy)
    {
        policy.Validate();
        if (!Enum.IsDefined(effect)) throw new ArgumentException("Unknown step effect.", nameof(effect));
        return new ActivityOptions
        {
            TaskQueue = policy.EffectTaskQueue,
            StartToCloseTimeout = policy.StartToCloseTimeouts[effect],
            RetryPolicy = new RetryPolicy
            {
                MaximumAttempts = effect is StepEffect.Pure or StepEffect.Artifact ? 5 : 1,
                InitialInterval = TimeSpan.FromSeconds(1),
                MaximumInterval = TimeSpan.FromSeconds(30),
            },
        };
    }
}
