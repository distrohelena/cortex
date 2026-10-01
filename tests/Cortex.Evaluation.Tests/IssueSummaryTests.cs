using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class IssueSummaryTests
{
    [Fact]
    public void Repetitions_are_failures_but_not_independent_cases_and_candidates_stay_separate()
    {
        var report = Report([Trial("a", "case-a", 0, "fail", 4), Trial("a", "case-a", 1, "fail", 2),
            Trial("a", "case-b", 0, "pass", null), Trial("b", "case-a", 0, "pass", null)]);
        var result = IssueSummaryProjection.Create(report);
        var a = Assert.Single(result.Items, i => i.CandidateRevision == "a");
        Assert.Equal(2, a.FailedTrials);
        Assert.Equal(1, a.FailedCases);
        Assert.Equal(3, a.AssessedTrials);
        Assert.Equal(2d / 3, a.FailureRate);
        Assert.Equal(4, a.MaximumFailureSeverity);
        Assert.Equal(2, a.Evidence.Count);
        Assert.Equal(0, Assert.Single(result.Items, i => i.CandidateRevision == "b").FailedTrials);
        Assert.Null(a.EstimatedFailedTrialCost);
    }

    [Fact]
    public void Unscored_and_unresolved_are_not_success_or_confirmed_failure()
    {
        var unresolved = Trial("a", "case-a", 0, "pass", null);
        unresolved = unresolved with { Criteria = [new("quality", true, "assessment_unresolved", null, null, null)] };
        var result = IssueSummaryProjection.Create(Report([unresolved, Trial("a", "case-b", 0, "unscored", null)]));
        var issue = Assert.Single(result.Items);
        Assert.Equal(0, issue.AssessedTrials);
        Assert.Equal(0, issue.FailedTrials);
        Assert.Equal(1, issue.UnresolvedTrials);
        Assert.Equal(1, issue.UnscoredTrials);
        Assert.Null(issue.FailureRate);
    }

    [Fact]
    public void Unavailable_report_does_not_become_an_empty_successful_analysis()
    {
        var result = IssueSummaryProjection.Create(new(Guid.NewGuid(), new string('a', 64), "evidence_unavailable", null));
        Assert.Equal("evidence_unavailable", result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Priority_explains_criticality_without_automatically_selecting_a_change()
    {
        var normal = Trial("a", "case-a", 0, "fail", 5);
        normal = normal with { Criteria = [normal.Criteria[0] with { Critical = false, Id = "style" }] };
        var critical = Trial("a", "case-b", 0, "fail", 1);
        var result = IssueSummaryProjection.Create(Report([normal, critical]));
        Assert.Equal("quality", result.Items[0].Criterion);
        Assert.Equal(1, result.Items[0].CriticalFailures);
        Assert.Equal("critical-failures,cases,failures,severity,identity:v1", result.Method);
    }

    [Fact]
    public void Reusing_trial_or_assessment_cannot_inflate_recurrence()
    {
        var trial = Trial("a", "case-a", 0, "fail", 2);
        Assert.Throws<InvalidDataException>(() => IssueSummaryProjection.Create(Report([trial, trial])));
        var duplicateAssessment = Trial("a", "case-b", 0, "fail", 2) with { Criteria = trial.Criteria };
        Assert.Throws<InvalidDataException>(() => IssueSummaryProjection.Create(Report([trial, duplicateAssessment])));
    }

    [Fact]
    public void Cost_is_failed_trial_cost_not_savings_and_partial_or_mixed_currency_stays_unknown()
    {
        var a = Trial("a", "case-a", 0, "fail", 1);
        var b = Trial("a", "case-b", 0, "fail", 1);
        a = a with { Measurement = a.Measurement with { EstimatedCost = 0.01m, Currency = "USD" } };
        b = b with { Measurement = b.Measurement with { EstimatedCost = 0.02m, Currency = "USD" } };
        Assert.Equal(0.03m, Assert.Single(IssueSummaryProjection.Create(Report([a, b])).Items).EstimatedFailedTrialCost);
        var mixed = b with { Measurement = b.Measurement with { Currency = "BRL" } };
        var mixedIssue = Assert.Single(IssueSummaryProjection.Create(Report([a, mixed])).Items);
        Assert.Null(mixedIssue.EstimatedFailedTrialCost);
        Assert.Null(mixedIssue.Currency);
        var partial = b with { Measurement = b.Measurement with { EstimatedCost = null } };
        Assert.Null(Assert.Single(IssueSummaryProjection.Create(Report([a, partial])).Items).EstimatedFailedTrialCost);
    }

    [Fact]
    public void Inconsistent_criticality_or_selected_outcome_on_unknown_is_rejected()
    {
        var a = Trial("a", "case-a", 0, "fail", 1);
        var b = Trial("a", "case-b", 0, "fail", 1);
        b = b with { Criteria = [b.Criteria[0] with { Critical = false }] };
        Assert.Throws<InvalidDataException>(() => IssueSummaryProjection.Create(Report([a, b])));
        var unknown = a with { Criteria = [a.Criteria[0] with { Status = "assessment_unresolved" }] };
        Assert.Throws<InvalidDataException>(() => IssueSummaryProjection.Create(Report([unknown])));
    }

    internal static StoredComparisonReport Report(IReadOnlyList<TrialEvidenceGate> trials)
    {
        var revision = new RevisionRef(Guid.NewGuid(), new string('a', 64));
        return new(Guid.NewGuid(), new string('b', 64), "available", new(Guid.NewGuid(), new string('c', 64),
            [new(revision, Guid.NewGuid())], new(revision, new([], []), trials)));
    }

    private static TrialEvidenceGate Trial(string candidate, string input, int repetition, string outcome, int? severity)
    {
        var trialId = Guid.NewGuid();
        var assessment = Guid.NewGuid();
        var calls = new[] { TrialMeasurementTests.Call(trialId, null) };
        var measurement = TrialMeasurement.Aggregate(trialId, TimeSpan.FromMilliseconds(50), calls);
        return new(trialId, candidate, new(input, repetition), [], [assessment], measurement.AttemptIds,
            [new("quality", true, "resolved", assessment, outcome, severity)], measurement);
    }
}
