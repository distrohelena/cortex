using Cortex.Evaluation.Comparison;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class PairedComparisonTests
{
    [Fact]
    public void Preview_exposes_pinned_policy_and_does_not_authorize_execution()
    {
        EvaluationCell[] cells = [new("one", 0), new("two", 0)];
        var request = System.Text.Json.JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "comparison.preview",
            payload = new
            {
                candidates = new[] { "a", "b" },
                cells,
                trials = cells.SelectMany(c => Pair(c, "fail", "pass")).ToArray(),
                pairedPolicy = new PairedBootstrapPolicy(1, "a", 42, 1000, 2)
            }
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var result = Cortex.Evaluation.Tools.OfflineEvaluationTools.Invoke(request);
        Assert.True(result.Success, result.ErrorCode);
        Assert.False(result.Persisted);
        var difference = Assert.Single(result.Result!.Value.GetProperty("pairedDifferences").EnumerateArray());
        Assert.Equal("available", difference.GetProperty("status").GetString());
        Assert.Equal(42, difference.GetProperty("policy").GetProperty("seed").GetInt32());
        Assert.Equal(1, difference.GetProperty("meanCorrectnessDifference").GetDouble());
    }

    [Fact]
    public void Large_policy_work_product_is_rejected_without_bootstrap()
    {
        var cells = Enumerable.Range(0, 501).Select(i => new EvaluationCell($"case-{i}", 0)).ToArray();
        var trials = cells.SelectMany(c => Pair(c, "pass", "pass")).ToArray();
        Assert.Throws<InvalidDataException>(() => ExperimentComparer.Compare(["a", "b"], cells, trials, new(1, "a", 42, 10000, 2)));
    }

    [Fact]
    public void Repetitions_do_not_inflate_independent_case_count()
    {
        var cells = Enumerable.Range(0, 20).Select(i => new EvaluationCell("one", i)).ToArray();
        var report = Compare(cells, cells.SelectMany(c => Pair(c, "fail", "pass")).ToArray());
        var difference = Assert.Single(report.PairedDifferences);
        Assert.Equal("insufficient_cases", difference.Status);
        Assert.Equal(1, difference.CaseCount);
        Assert.Null(difference.ConfidenceLow);
        Assert.Null(difference.ConfidenceHigh);
    }

    [Fact]
    public void Case_balanced_difference_does_not_overweight_more_repeated_case()
    {
        var cells = Enumerable.Range(0, 20).Select(i => new EvaluationCell("many", i)).Append(new("single", 0)).ToArray();
        var trials = cells.SelectMany(c => c.CaseRevision == "many" ? Pair(c, "fail", "pass") : Pair(c, "pass", "fail")).ToArray();
        var difference = Assert.Single(Compare(cells, trials.ToArray()).PairedDifferences);
        Assert.Equal("available", difference.Status);
        Assert.Equal(0, difference.MeanCorrectnessDifference);
        Assert.Equal(-1, difference.ConfidenceLow);
        Assert.Equal(1, difference.ConfidenceHigh);
        Assert.Equal(2, difference.CaseCount);
    }

    [Fact]
    public void Constant_paired_improvement_has_exact_interval_and_preserves_unknown_cost()
    {
        EvaluationCell[] cells = [new("one", 0), new("two", 0), new("three", 0)];
        var trials = cells.SelectMany(c => Pair(c, "fail", "pass")).ToArray();
        var report = Compare(cells, trials);
        var difference = Assert.Single(report.PairedDifferences);
        Assert.Equal(1, difference.MeanCorrectnessDifference);
        Assert.Equal(1, difference.ConfidenceLow);
        Assert.Equal(1, difference.ConfidenceHigh);
        Assert.All(report.Candidates, candidate => Assert.Null(candidate.MeanCost));
        Assert.Equal(2, report.ParetoCandidates.Count);
    }

    [Fact]
    public void Ordering_does_not_change_seeded_case_bootstrap()
    {
        EvaluationCell[] cells = [new("z", 0), new("a", 0), new("b", 0)];
        var trials = cells.SelectMany(c => Pair(c, "pass", c.CaseRevision == "a" ? "fail" : "pass")).ToArray();
        var first = Assert.Single(Compare(cells, trials).PairedDifferences);
        var reordered = Assert.Single(Compare(cells.Reverse().ToArray(), trials.Reverse().ToArray()).PairedDifferences);
        Assert.Equal(first, reordered);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unscored")]
    [InlineData("critical")]
    [InlineData("error")]
    public void Invalid_evidence_cannot_gain_statistical_approval(string kind)
    {
        EvaluationCell[] cells = [new("one", 0), new("two", 0)];
        var trials = cells.SelectMany(c => Pair(c, "pass", "pass")).ToList();
        if (kind == "missing") trials.RemoveAt(3);
        else trials[3] = trials[3] with { Outcome = kind == "critical" ? "fail" : kind, CriticalFailure = kind == "critical" };
        var difference = Assert.Single(Compare(cells, trials).PairedDifferences);
        Assert.Equal("ineligible_evidence", difference.Status);
        Assert.Null(difference.MeanCorrectnessDifference);
        Assert.Null(difference.ConfidenceLow);
    }

    [Theory]
    [InlineData(0, 500, 2, "a")]
    [InlineData(1, 0, 2, "a")]
    [InlineData(1, 10001, 2, "a")]
    [InlineData(1, 500, 1, "a")]
    [InlineData(1, 500, 2, "foreign")]
    public void Unsupported_or_unbounded_policy_is_rejected(int version, int samples, int minimum, string baseline)
    {
        EvaluationCell[] cells = [new("one", 0)];
        Assert.Throws<InvalidDataException>(() => ExperimentComparer.Compare(["a", "b"], cells, Pair(cells[0], "pass", "pass"),
            new(version, baseline, 42, samples, minimum)));
    }

    private static ComparisonReport Compare(EvaluationCell[] cells, IReadOnlyList<ComparisonTrial> trials) =>
        ExperimentComparer.Compare(["a", "b"], cells, trials, new(1, "a", 42, 1000, 2));
    private static ComparisonTrial[] Pair(EvaluationCell cell, string a, string b) =>
        [new("a", cell, Guid.NewGuid(), a, false, 10, null, "USD"), new("b", cell, Guid.NewGuid(), b, false, 10, null, "USD")];
}
