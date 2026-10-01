using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class IssueHistoryTests
{
    [Theory]
    [InlineData("{\"reportIds\":[]}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"reportIds\":[\"00000000-0000-0000-0000-000000000000\"]}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"reportIds\":[\"11111111-1111-1111-1111-111111111111\",\"11111111-1111-1111-1111-111111111111\"]}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{}", EvaluationRole.Proposer, "unauthorized")]
    [InlineData("{}", EvaluationRole.Operator, "unauthorized")]
    public async Task History_query_requires_explicit_unique_reports_and_host_evaluator(string payload, EvaluationRole role, string error)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("history", new(Guid.NewGuid(), Guid.NewGuid()), role);
        var result = await new PersistedEvaluationTools(source).InvokeAsync(access,
            "{\"contractVersion\":1,\"operation\":\"issues.history\",\"payload\":" + payload + "}", TestContext.Current.CancellationToken);
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public void Repeated_report_snapshot_does_not_multiply_trials_or_cost()
    {
        var original = Report(Trial("fail"));
        var duplicate = original with { ReportId = Guid.NewGuid() };
        var result = IssueHistoryProjection.Create([original, duplicate]);
        var group = Assert.Single(result.Groups);
        var issue = Assert.Single(group.Items);
        Assert.Equal("available", result.Status);
        Assert.Equal(2, group.ReportIds.Count);
        Assert.Equal(1, group.UniqueTrials);
        Assert.Equal(1, issue.FailedTrials);
        Assert.Equal(0.01m, issue.EstimatedFailedTrialCost);
    }

    [Fact]
    public void Independent_experiments_with_identical_plan_and_policy_accumulate_real_trials()
    {
        var first = Report(Trial("fail"));
        var second = Report(Trial("pass"));
        var group = Assert.Single(IssueHistoryProjection.Create([first, second]).Groups);
        var issue = Assert.Single(group.Items);
        Assert.Equal(2, group.UniqueTrials);
        Assert.Equal(2, issue.AssessedTrials);
        Assert.Equal(1, issue.FailedCases);
        Assert.Equal(0.5, issue.FailureRate);
    }

    [Fact]
    public void Different_plan_policy_or_input_binding_never_merges()
    {
        var first = Report(Trial("fail"));
        var plan = Report(Trial("pass"));
        plan = plan with { Snapshot = plan.Snapshot! with { PlanSha256 = new string('d', 64) } };
        var policy = Report(Trial("pass"));
        policy = policy with { Snapshot = policy.Snapshot! with { Report = policy.Snapshot.Report with { PolicyRevision = new(Guid.NewGuid(), new string('e', 64)) } } };
        var input = Report(Trial("pass"));
        input = input with { Snapshot = input.Snapshot! with { Inputs = [new(input.Snapshot.Inputs[0].InputRevision, Guid.NewGuid())] } };
        Assert.Equal(4, IssueHistoryProjection.Create([first, plan, policy, input]).Groups.Count);
    }

    [Fact]
    public void Changed_assessment_for_same_trial_is_conflict_not_latest_or_best_wins()
    {
        var first = Report(Trial("fail"));
        var trial = first.Snapshot!.Report.Trials[0];
        var changed = first with
        {
            ReportId = Guid.NewGuid(),
            Snapshot = first.Snapshot with
            {
                Report = first.Snapshot.Report with
                { Trials = [trial with { Criteria = [trial.Criteria[0] with { Outcome = "pass" }] }] }
            }
        };
        var result = IssueHistoryProjection.Create([first, changed]);
        Assert.Equal("partial", result.Status);
        var group = Assert.Single(result.Groups);
        Assert.Equal("conflicting_evidence", group.Status);
        Assert.Empty(group.Items);
    }

    [Fact]
    public void Unavailable_evidence_is_explicit_and_not_empty_success()
    {
        var unavailable = new StoredComparisonReport(Guid.NewGuid(), new string('a', 64), "evidence_unavailable", null);
        var result = IssueHistoryProjection.Create([unavailable]);
        Assert.Equal("partial", result.Status);
        Assert.Equal("evidence_unavailable", Assert.Single(result.Reports).Status);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public void Trial_identity_cannot_move_between_experiments()
    {
        var first = Report(Trial("fail"));
        var changed = first with { ReportId = Guid.NewGuid(), Snapshot = first.Snapshot! with { ExperimentId = Guid.NewGuid() } };
        Assert.Equal("conflicting_evidence", Assert.Single(IssueHistoryProjection.Create([first, changed]).Groups).Status);
    }

    [Fact]
    public void Different_trials_for_same_experiment_cell_are_not_independent_repetitions()
    {
        var first = Report(Trial("fail"));
        var second = Report(Trial("pass"));
        second = second with { Snapshot = second.Snapshot! with { ExperimentId = first.Snapshot!.ExperimentId } };
        Assert.Equal("conflicting_evidence", Assert.Single(IssueHistoryProjection.Create([first, second]).Groups).Status);
    }

    [Fact]
    public void Same_policy_with_conflicting_criticality_is_withheld_as_conflict()
    {
        var first = Report(Trial("fail"));
        var trial = Trial("fail");
        var second = Report(trial with { Criteria = [trial.Criteria[0] with { Critical = false }] });
        Assert.Equal("conflicting_evidence", Assert.Single(IssueHistoryProjection.Create([first, second]).Groups).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reusing_assessment_or_physical_attempt_across_trials_is_not_new_evidence(bool reuseAssessment)
    {
        var firstTrial = Trial("fail");
        var other = Trial("fail");
        other = reuseAssessment ? other with { Criteria = firstTrial.Criteria, AssessmentIds = firstTrial.AssessmentIds }
            : other with { Measurement = other.Measurement with { AttemptIds = firstTrial.Measurement.AttemptIds } };
        var result = IssueHistoryProjection.Create([Report(firstTrial), Report(other)]);
        Assert.Equal("conflicting_evidence", Assert.Single(result.Groups).Status);
        Assert.Empty(result.Groups[0].Items);
    }

    [Fact]
    public async Task Larger_than_bounded_report_set_is_rejected_before_storage()
    {
        var access = EvaluationAccess.FromHost("history", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
        await Assert.ThrowsAsync<ArgumentException>(() => new IssueHistoryReader(null!).ReadAsync(access,
            Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray(), TestContext.Current.CancellationToken));
    }

    internal static StoredComparisonReport Report(TrialEvidenceGate trial)
    {
        var basis = IssueSummaryTests.Report([trial]);
        var revision = new RevisionRef(Guid.Parse("11111111-1111-1111-1111-111111111111"), new string('a', 64));
        return basis with
        {
            Snapshot = basis.Snapshot! with
            {
                Inputs = [new(revision, Guid.Parse("22222222-2222-2222-2222-222222222222"))],
                Report = basis.Snapshot.Report with { PolicyRevision = revision }
            }
        };
    }

    internal static TrialEvidenceGate Trial(string outcome)
    {
        var id = Guid.NewGuid();
        var assessment = Guid.NewGuid();
        var measurement = TrialMeasurement.Aggregate(id, TimeSpan.FromMilliseconds(5), [TrialMeasurementTests.Call(id, null)])
            with
        { EstimatedCost = 0.01m, Currency = "BRL" };
        return new(id, "candidate", new("case", 0), [], [assessment], measurement.AttemptIds,
            [new("correctness", true, "resolved", assessment, outcome, 1)], measurement);
    }
}
