using Filomena.Evaluation.Comparison;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ComparisonTests
{
    [Fact]
    public void Two_correct_repetitions_out_of_three_are_not_perfect_consistency()
    {
        var report = ExperimentComparer.Compare(["a"], Cells(3), [Trial("a", 0), Trial("a", 1), Trial("a", 2, "fail")]);
        var a = Assert.Single(report.Candidates);
        Assert.Equal(2d / 3, a.Correctness);
        Assert.Equal(3, a.Observed);
        Assert.True(a.ConfidenceLow < a.Correctness);
        Assert.True(a.ConfidenceHigh > a.Correctness);
    }

    [Fact]
    public void Incomplete_candidate_cannot_win_by_avoiding_hard_cases()
    {
        var report = ExperimentComparer.Compare(["a", "b"], Cells(2), [Trial("a", 0), Trial("b", 0), Trial("b", 1)]);
        Assert.False(report.Candidates.Single(c => c.Id == "a").Eligible);
        Assert.Contains("incomplete", report.Candidates.Single(c => c.Id == "a").GateFailures);
        Assert.DoesNotContain("a", report.ParetoCandidates);
    }

    [Fact]
    public void Critical_failure_blocks_cheap_candidate_before_pareto()
    {
        var bad = Trial("cheap", 0, "fail") with { CriticalFailure = true, Cost = 0.000001m };
        var report = ExperimentComparer.Compare(["cheap", "sound"], Cells(1), [bad, Trial("sound", 0)]);
        Assert.False(report.Candidates.Single(c => c.Id == "cheap").Eligible);
        Assert.Equal(["sound"], report.ParetoCandidates);
    }

    [Fact]
    public void Reused_attempt_cannot_count_as_independent_repetition()
    {
        var original = Trial("a", 0);
        Assert.Throws<InvalidDataException>(() => ExperimentComparer.Compare(["a"], Cells(2),
            [original, original with { Cell = new("case", 1) }]));
    }

    [Fact]
    public void Unknown_cost_prevents_claiming_financial_dominance()
    {
        var report = ExperimentComparer.Compare(["a", "b"], Cells(1),
            [Trial("a", 0) with { Cost = null }, Trial("b", 0) with { Cost = 0.001m }]);
        Assert.Equal(2, report.ParetoCandidates.Count);
        Assert.Null(report.Candidates.Single(c => c.Id == "a").MeanCost);
    }

    [Fact]
    public void Different_currencies_are_not_implicitly_converted()
    {
        var report = ExperimentComparer.Compare(["a", "b"], Cells(1),
            [Trial("a", 0), Trial("b", 0) with { Currency = "BRL", Cost = 0.0001m }]);
        Assert.Equal(2, report.ParetoCandidates.Count);
    }

    [Fact]
    public void Unscored_is_not_quality_approval()
    {
        var report = ExperimentComparer.Compare(["a"], Cells(1), [Trial("a", 0, "unscored")]);
        var candidate = Assert.Single(report.Candidates);
        Assert.False(candidate.Eligible);
        Assert.Null(candidate.Correctness);
        Assert.Empty(report.ParetoCandidates);
    }

    [Fact]
    public void Dominance_uses_paired_complete_cases_cost_and_latency()
    {
        var report = ExperimentComparer.Compare(["a", "b"], Cells(2),
        [Trial("a", 0), Trial("a", 1) with { Milliseconds = 300 },
         Trial("b", 0) with { Cost = 0.1m, Milliseconds = 400 }, Trial("b", 1) with { Cost = 0.1m, Milliseconds = 500 }]);
        Assert.Equal(["a"], report.ParetoCandidates);
        var a = report.Candidates.Single(c => c.Id == "a");
        Assert.Equal(200, a.MedianMilliseconds);
        Assert.Equal(300, a.P95Milliseconds);
    }

    [Fact]
    public void Unexpected_case_or_duplicate_cell_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => ExperimentComparer.Compare(["a"], Cells(1), [Trial("a", 2)]));
        Assert.Throws<InvalidDataException>(() => ExperimentComparer.Compare(["a"], Cells(1), [Trial("a", 0), Trial("a", 0)]));
    }

    private static EvaluationCell[] Cells(int count) => Enumerable.Range(0, count).Select(i => new EvaluationCell("case", i)).ToArray();
    private static ComparisonTrial Trial(string candidate, int repetition, string outcome = "pass") =>
        new(candidate, new("case", repetition), Guid.NewGuid(), outcome, false, 100, 0.01m, "USD");
}
