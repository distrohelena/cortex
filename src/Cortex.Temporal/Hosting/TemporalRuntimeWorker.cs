using Temporalio.Client;
using Temporalio.Worker;

namespace Cortex.Temporal.Hosting;

public sealed class TemporalRuntimeWorker(ITemporalClient client, TemporalRuntimeOptions options,
    IReadOnlyList<Type> workflows, IReadOnlyList<object> activities)
{
    public async Task RunAsync(CancellationToken stopWorker, Task? effectsReady = null)
    {
        options.Validate();
        var workflowOptions = new TemporalWorkerOptions(options.WorkflowTaskQueue);
        foreach (var workflow in workflows) workflowOptions.AddWorkflow(workflow);
        var effectOptions = new TemporalWorkerOptions(options.EffectTaskQueue);
        foreach (var activity in activities) effectOptions.AddAllActivities(activity.GetType(), activity);
        using var workflowWorker = new TemporalWorker(client, workflowOptions);
        using var effectWorker = new TemporalWorker(client, effectOptions);
        async Task RunEffects()
        {
            if (effectsReady is not null) await effectsReady.WaitAsync(stopWorker).ConfigureAwait(false);
            await effectWorker.ExecuteAsync(stopWorker).ConfigureAwait(false);
        }
        await Task.WhenAll(workflowWorker.ExecuteAsync(stopWorker), RunEffects()).ConfigureAwait(false);
    }
}
