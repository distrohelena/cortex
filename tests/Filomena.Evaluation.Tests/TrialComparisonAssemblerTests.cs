using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using System.Text.Json;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class TrialComparisonAssemblerTests
{
    private static readonly Guid Experiment = Guid.NewGuid();
    private static readonly RevisionRef Evaluator = new(Guid.NewGuid(), new string('a', 64));
    private static readonly TrialComparisonPolicy Policy = new(1, Evaluator, [new("quality", true), new("voice", false)]);
    private static readonly EvaluationCell Cell = new("input-revision", 0);

    [Fact]
    public void Two_calls_and_two_criteria_form_one_logical_observation_with_unknown_cost()
    {
        var report = Assemble([Trial()]);
        var candidate = Assert.Single(report.Statistics.Candidates);
        Assert.Equal(1, candidate.Observed);
        Assert.True(candidate.Eligible);
        Assert.Null(candidate.MeanCost);
        Assert.Null(candidate.Currency);
        Assert.Equal(150, candidate.MedianMilliseconds);
        Assert.Equal(2, Assert.Single(report.Trials).AttemptIds.Count);
        Assert.Equal(2, Assert.Single(report.Trials).AssessmentIds.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("wrong_evaluator")]
    [InlineData("wrong_binding")]
    public void Incomplete_or_mismatched_criterion_does_not_certify_trial(string defect)
    {
        var trial = Trial();
        var assessments = trial.Assessments.ToDictionary();
        if (defect == "missing") assessments.Remove("voice");
        else assessments["voice"] = defect switch
        {
            "ambiguous" => assessments["voice"] with { Selection = new("ambiguous", null, null, null) },
            "wrong_evaluator" => assessments["voice"] with { EvaluatorRevision = new(Guid.NewGuid(), new string('b', 64)) },
            _ => assessments["voice"] with { BindingId = Guid.NewGuid() }
        };
        var report = Assemble([trial with { Assessments = assessments }]);
        Assert.False(Assert.Single(report.Statistics.Candidates).Eligible);
        Assert.Empty(report.Statistics.ParetoCandidates);
        Assert.NotEmpty(Assert.Single(report.Trials).Failures);
    }

    [Fact]
    public void Policy_critical_failure_blocks_candidate_and_unscored_never_passes()
    {
        var trial = Trial();
        var assessments = trial.Assessments.ToDictionary();
        assessments["quality"] = assessments["quality"] with { Selection = assessments["quality"].Selection with { Outcome = "fail", Severity = 1 } };
        var report = Assemble([trial with { Assessments = assessments }]);
        Assert.Contains("critical_failure", Assert.Single(report.Statistics.Candidates).GateFailures);
        assessments["quality"] = assessments["quality"] with { Selection = assessments["quality"].Selection with { Outcome = "unscored", Severity = null } };
        report = Assemble([trial with { Assessments = assessments }]);
        Assert.Contains("unscored", Assert.Single(report.Statistics.Candidates).GateFailures);
    }

    [Fact]
    public void Missing_result_or_running_trial_cannot_contribute_zero_latency_success()
    {
        var trial = Trial();
        var report = Assemble([trial with { Measurement = trial.Measurement with { TrialState = "running" } }]);
        Assert.False(Assert.Single(report.Statistics.Candidates).Eligible);
        Assert.Null(Assert.Single(report.Statistics.Candidates).MedianMilliseconds);
    }

    [Fact]
    public void Policy_hash_mismatch_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TrialComparisonAssembler.Assemble(Experiment, ["candidate"], [Cell],
            [Trial()], new(Guid.NewGuid(), new string('b', 64)), Policy));
    }

    [Fact]
    public void Criteria_cannot_reuse_one_assessment_or_swap_its_request_identity()
    {
        var trial = Trial();
        var assessments = trial.Assessments.ToDictionary();
        var quality = assessments["quality"];
        assessments["voice"] = assessments["voice"] with
        {
            Selection = quality.Selection,
            AssessmentIds = quality.AssessmentIds
        };
        Assert.Throws<InvalidDataException>(() => Assemble([trial with { Assessments = assessments }]));
        assessments["voice"] = quality;
        var report = Assemble([trial with { Assessments = assessments }]);
        Assert.False(Assert.Single(report.Statistics.Candidates).Eligible);
    }

    [Fact]
    public void Reused_physical_call_is_rejected_even_when_trial_is_gated()
    {
        var first = Trial();
        var secondId = Guid.NewGuid();
        var calls = first.Measurement.Attempts.Select(call => call with { TrialId = secondId }).ToArray();
        var second = first with
        {
            TrialId = secondId,
            Cell = new("input-revision", 1),
            Measurement = new(Experiment, "running", "not_recorded",
                TrialMeasurement.Aggregate(secondId, null, calls), calls),
            Assessments = new Dictionary<string, TrialAssessmentSnapshot>()
        };
        Assert.Throws<InvalidDataException>(() => TrialComparisonAssembler.Assemble(Experiment, ["candidate"], [Cell, second.Cell],
            [first, second], new(Guid.NewGuid(), RevisionHasher.Hash(Policy.ToRevisionDocument())), Policy));
    }

    [Fact]
    public void Evaluator_hash_must_be_a_canonical_sha256_not_just_64_characters()
    {
        var policy = Policy with { EvaluatorRevision = Evaluator with { Sha256 = new string('z', 64) } };
        Assert.Throws<InvalidDataException>(() => TrialComparisonAssembler.Assemble(Experiment, ["candidate"], [Cell],
            [], new(Guid.NewGuid(), RevisionHasher.Hash(policy.ToRevisionDocument())), policy));
    }

    [Fact]
    public void Report_preserves_criterion_outcomes_and_accounting_even_when_trial_is_not_scored()
    {
        var trial = Trial();
        var assessments = trial.Assessments.ToDictionary();
        assessments["quality"] = assessments["quality"] with
        {
            Selection = assessments["quality"].Selection with { Outcome = "fail", Severity = 4 }
        };
        assessments.Remove("voice");
        var report = Assemble([trial with { Assessments = assessments }]);
        var json = JsonSerializer.SerializeToElement(Assert.Single(report.Trials));
        Assert.True(json.TryGetProperty("Criteria", out var criteria));
        Assert.Equal(2, criteria.GetArrayLength());
        Assert.Equal("quality", criteria[0].GetProperty("Id").GetString());
        Assert.True(criteria[0].GetProperty("Critical").GetBoolean());
        Assert.Equal("resolved", criteria[0].GetProperty("Status").GetString());
        Assert.Equal("fail", criteria[0].GetProperty("Outcome").GetString());
        Assert.Equal(4, criteria[0].GetProperty("Severity").GetInt32());
        Assert.Equal("assessment_missing", criteria[1].GetProperty("Status").GetString());
        Assert.Equal(JsonValueKind.Null, criteria[1].GetProperty("Outcome").ValueKind);
        Assert.True(json.TryGetProperty("Measurement", out var measurement));
        Assert.Equal(2, measurement.GetProperty("PhysicalCalls").GetInt32());
        Assert.Equal(200, measurement.GetProperty("InputTokens").GetInt64());
        Assert.Equal("unavailable", measurement.GetProperty("CostStatus").GetString());
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("EstimatedCost").ValueKind);
        Assert.False(Assert.Single(report.Statistics.Candidates).Eligible);
    }

    private static EvidenceComparisonReport Assemble(ResolvedComparisonTrial[] trials) => TrialComparisonAssembler.Assemble(
        Experiment, ["candidate"], [Cell], trials, new(Guid.NewGuid(), RevisionHasher.Hash(Policy.ToRevisionDocument())), Policy);
    private static ResolvedComparisonTrial Trial()
    {
        var id = Guid.NewGuid();
        var calls = new[] { TrialMeasurementTests.Call(id, null), TrialMeasurementTests.Call(id, null) };
        var measurement = new TrialMeasurementSnapshot(Experiment, "completed", "available",
            TrialMeasurement.Aggregate(id, TimeSpan.FromMilliseconds(150), calls), calls);
        var binding = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        TrialAssessmentSnapshot Assessment(string criterion)
        {
            var assessmentId = Guid.NewGuid();
            return new(binding, "evaluator_declaration", caseId, Evaluator,
                new("resolved", assessmentId, "pass", null), [assessmentId])
            {
                Request = new(Experiment, id, binding, criterion)
            };
        }
        return new(id, "candidate", Cell, measurement, new Dictionary<string, TrialAssessmentSnapshot>
        {
            ["quality"] = Assessment("quality"),
            ["voice"] = Assessment("voice")
        });
    }
}
