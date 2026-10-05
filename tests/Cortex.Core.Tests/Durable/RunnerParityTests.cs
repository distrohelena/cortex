using Cortex.Core.Flows;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Flows.Durable;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class RunnerParityTests
{
    [Fact]
    public async Task RunnerAndIncrementalDriverProduceSameOutcome()
    {
        var flow = IncrementalKernelTests.Flow();
        var localModel = new IncrementalKernelTests.Model();
        var local = await TypedFlowRunner.RunAsync(flow, IncrementalKernelTests.Message(), localModel, TestContext.Current.CancellationToken);
        var durableModel = new IncrementalKernelTests.Model();
        var state = FlowExecutionKernel.Start(flow, IncrementalKernelTests.Message(), IncrementalKernelTests.Identity(), new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>());
        KernelStepResult step;
        do { step = await FlowExecutionKernel.AdvanceAsync(flow, state, new(durableModel), TestContext.Current.CancellationToken); }
        while (step.Status == ExecutionStatus.Ready);
        Assert.Equal(local.Success, step.Result.Success);
        Assert.Equal(local.Steps, step.Result.Steps);
        Assert.Equal(local.TargetStateId, step.Result.TargetStateId);
        Assert.Equal(local.Outputs["second"]["text"].Value.GetString(), step.Result.Outputs["second"]["text"].Value.GetString());
        Assert.Equal(localModel.Requests.Count, durableModel.Requests.Count);
    }
}
