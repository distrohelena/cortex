using Cortex.Core.Flows.Durable;

namespace Cortex.Temporal.Hosting;

public sealed record TemporalRuntimeOptions(string InstallationId, string TargetHost, string Namespace,
    string WorkflowTaskQueue, string EffectTaskQueue, string ArtifactDirectory)
{
    public int MaxPendingBatches { get; init; } = 256;

    public void Validate()
    {
        foreach (var value in new[] { InstallationId, TargetHost, Namespace, WorkflowTaskQueue, EffectTaskQueue, ArtifactDirectory })
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
                throw new ArgumentException("Temporal installation, endpoint, namespace, queues and private store must be explicit.");
        if (MaxPendingBatches is < 1 or > 4096) throw new ArgumentException("Pending batch limit must be between 1 and 4096.");
        if (WorkflowTaskQueue == EffectTaskQueue) throw new ArgumentException("Workflow and installation effect queues must differ.");
    }
}

public sealed record WorkflowActivityPolicy(string EffectTaskQueue, IReadOnlyDictionary<StepEffect, TimeSpan> StartToCloseTimeouts)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EffectTaskQueue) || StartToCloseTimeouts is null)
            throw new ArgumentException("An installation effect queue and explicit timeouts are required.");
        foreach (var effect in Enum.GetValues<StepEffect>())
            if (!StartToCloseTimeouts.TryGetValue(effect, out var timeout) || timeout <= TimeSpan.Zero || timeout == TimeSpan.MaxValue)
                throw new ArgumentException($"A finite positive timeout is required for {effect}.");
    }
}
