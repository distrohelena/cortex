using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class AttemptAdmissionTests
{
    private static readonly Guid Owner = Guid.Parse("ddcc6614-b0dd-43ea-9e68-a082b1ba493c");
    private static AttemptAdmissionState Ready => new("running", false, "running", Owner, 3, false);

    [Fact]
    public void Current_owner_can_reserve_exactly_the_remaining_budget()
    {
        var result = AttemptAdmission.Reserve(Ready, Owner, 3, new(2, 200, 100, 0.1m),
            new(1, 100, 50, 0.05m), new(100, 50, 0.05m));
        Assert.Equal(new BudgetConsumption(2, 200, 100, 0.1m), result);
    }

    [Theory]
    [InlineData("planned", "running")]
    [InlineData("completed", "running")]
    [InlineData("indeterminate", "running")]
    [InlineData("cancelled", "running")]
    [InlineData("running", "pending")]
    [InlineData("running", "completed")]
    [InlineData("running", "indeterminate")]
    public void Non_running_work_cannot_admit_a_physical_call(string experiment, string trial) =>
        Denied(Ready with { ExperimentState = experiment, TrialState = trial });

    [Fact]
    public void Cancellation_blocks_new_calls() => Denied(Ready with { CancelRequested = true });

    [Fact]
    public void Unresolved_prior_attempt_is_not_implicitly_retried() =>
        Denied(Ready with { HasUnresolvedAttempt = true });

    [Fact]
    public void Old_generation_cannot_spend_after_a_new_claim() => Denied(Ready with { LeaseGeneration = 4 });

    [Fact]
    public void Different_worker_cannot_use_current_generation() => Denied(Ready with { LeaseOwner = Guid.NewGuid() });

    [Fact]
    public void Empty_owner_and_zero_generation_are_not_a_lease() =>
        Assert.Throws<InvalidOperationException>(() => AttemptAdmission.Reserve(
            Ready with { LeaseOwner = Guid.Empty, LeaseGeneration = 0 }, Guid.Empty, 0,
            new(2, 200, 100, 0.1m), new(0, 0, 0, 0), new(100, 50, 0.05m)));

    private static void Denied(AttemptAdmissionState state) =>
        Assert.Throws<InvalidOperationException>(() => AttemptAdmission.Reserve(state, Owner, 3,
            new(2, 200, 100, 0.1m), new(0, 0, 0, 0), new(100, 50, 0.05m)));
}
