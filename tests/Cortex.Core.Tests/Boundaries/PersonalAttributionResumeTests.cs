using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class PersonalAttributionResumeTests
{
    [Fact]
    public void Completed_peer_is_not_repeated_and_failure_is_not_a_valid_observation()
    {
        PersonalAttributionSpikeLive.Row Row(string strategy, string status) =>
            new(strategy, "", "08", "", 1, "", false, status, null, null, null, null, 1, null);
        var rows = new[] { Row("A", "technical_error"), Row("C", "valid") };
        Assert.Equal(new[] { "A" }, PersonalAttributionResume.Pending(rows, "08", 1));
        Assert.Empty(PersonalAttributionResume.Pending([.. rows, Row("A", "valid")], "08", 1));
    }

    [Fact]
    public void Three_technical_failures_stop_fixture_strategy_across_repetitions()
    {
        var rows = Enumerable.Range(1, 3).Select(n => new PersonalAttributionSpikeLive.Row(
            "A", "", "08", "", n, "", false, n == 1 ? "technical_error" : "transport_failure",
            null, null, null, null, 1, null)).ToArray();
        Assert.Throws<InvalidOperationException>(() => PersonalAttributionResume.Pending(rows, "08", 1));
    }
}
