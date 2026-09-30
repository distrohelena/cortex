using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class TrialAssessmentSelectionTests
{
    private static readonly Guid Case = Guid.NewGuid();
    private static readonly Guid Trial = Guid.NewGuid();
    private static readonly RevisionRef Evaluator = new(Guid.NewGuid(), new string('a', 64));

    [Fact]
    public void Empty_evidence_is_missing_not_pass()
    {
        var result = Resolve([]);
        Assert.Equal("missing", result.Status);
        Assert.Null(result.Outcome);
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("fail")]
    [InlineData("unscored")]
    public void Single_terminal_preserves_outcome_and_severity(string outcome)
    {
        var row = Row(outcome) with { Severity = 3 };
        var result = Resolve([row]);
        Assert.Equal("resolved", result.Status);
        Assert.Equal(row.Id, result.AssessmentId);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(3, result.Severity);
    }

    [Fact]
    public void Explicit_supersession_selects_terminal_even_if_result_is_worse()
    {
        var old = Row("pass");
        var current = Row("fail") with { Supersedes = old.Id };
        var result = Resolve([current, old]);
        Assert.Equal(current.Id, result.AssessmentId);
        Assert.Equal("fail", result.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Concurrent_assessments_are_ambiguous_even_if_one_is_more_favorable(bool branch)
    {
        var old = Row("fail");
        var left = Row("pass") with { Supersedes = branch ? old.Id : null };
        var right = Row("fail") with { Supersedes = branch ? old.Id : null };
        var result = Resolve(branch ? [left, old, right] : [left, right]);
        Assert.Equal("ambiguous", result.Status);
        Assert.Null(result.Outcome);
        Assert.Null(result.AssessmentId);
    }

    [Fact]
    public void Wrong_revision_terminal_does_not_revive_superseded_approved_version()
    {
        var old = Row("pass");
        var current = Row("fail") with { Supersedes = old.Id, EvaluatorRevision = new(Guid.NewGuid(), new string('b', 64)) };
        var result = Resolve([old, current]);
        Assert.Equal("evaluator_revision_mismatch", result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void Redacted_terminal_cannot_fall_back_to_old_pass()
    {
        var old = Row("pass");
        var current = Row("fail") with { Supersedes = old.Id, EvidenceAvailable = false };
        var result = Resolve([old, current]);
        Assert.Equal("evidence_unavailable", result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void Missing_predecessor_and_cycles_cannot_resolve()
    {
        var row = Row("pass") with { Supersedes = Guid.NewGuid() };
        Assert.Equal("incomplete_lineage", Resolve([row]).Status);
        var a = Row("pass");
        var b = Row("pass") with { Supersedes = a.Id };
        a = a with { Supersedes = b.Id };
        Assert.Equal("invalid_lineage", Resolve([a, b]).Status);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("trial")]
    [InlineData("criterion")]
    public void Evidence_from_another_context_is_rejected(string mismatch)
    {
        var row = Row("pass");
        row = mismatch switch
        {
            "case" => row with { CaseId = Guid.NewGuid() },
            "trial" => row with { TrialId = null },
            _ => row with { Criterion = "different" }
        };
        Assert.Throws<InvalidDataException>(() => Resolve([row]));
    }

    private static TrialAssessmentEvidence Row(string outcome) => new(Guid.NewGuid(), Case, Trial, "correctness", Evaluator,
        outcome, null, null, true);
    private static TrialAssessmentSelection Resolve(TrialAssessmentEvidence[] rows) =>
        TrialAssessmentSelection.Resolve(Case, Trial, "correctness", Evaluator, rows);
}
