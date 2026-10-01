using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;
using System.Text.Json;
using Cortex.Evaluation.Tools;

namespace Cortex.Evaluation.Tests;

public sealed class ExperimentScheduleTests
{
    [Fact]
    public void Cli_preview_reports_schedule_without_authorizing_execution()
    {
        var (manifest, cases) = Setup();
        var request = JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.preview", payload = new { manifest, cases } },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = OfflineEvaluationTools.Invoke(request);
        Assert.True(response.Success);
        Assert.False(response.Persisted);
        Assert.Equal(12, response.Result!.Value.GetProperty("trialCount").GetInt32());
        Assert.False(response.Result.Value.GetProperty("executionAuthorized").GetBoolean());
    }

    [Fact]
    public void Same_pinned_manifest_cases_and_seed_produce_same_schedule()
    {
        var (manifest, cases) = Setup();
        var a = ExperimentSchedule.Create(manifest, cases);
        var b = ExperimentSchedule.Create(manifest, cases);
        Assert.Equal(a, b);
        Assert.Equal(12, a.Count);
        Assert.Equal(a.Count, a.Select(t => t.TrialKey).Distinct().Count());
    }

    [Fact]
    public void Interleaved_order_keeps_each_case_repetition_paired_across_candidates()
    {
        var (manifest, cases) = Setup();
        var schedule = ExperimentSchedule.Create(manifest, cases);
        foreach (var pair in schedule.Chunk(2))
        {
            Assert.Equal(pair[0].Case, pair[1].Case);
            Assert.Equal(pair[0].Repetition, pair[1].Repetition);
            Assert.NotEqual(pair[0].Candidate, pair[1].Candidate);
        }
    }

    [Fact]
    public void Foreign_scope_case_is_rejected()
    {
        var (manifest, cases) = Setup();
        cases[0] = cases[0] with { Scope = new(Guid.NewGuid(), Guid.NewGuid()) };
        Assert.Throws<InvalidDataException>(() => ExperimentSchedule.Create(manifest, cases));
    }

    [Fact]
    public void Duplicate_case_is_not_silently_counted_as_independent_data()
    {
        var (manifest, cases) = Setup();
        cases[1] = cases[0];
        Assert.Throws<InvalidDataException>(() => ExperimentSchedule.Create(manifest, cases));
    }

    [Fact]
    public void Trial_count_cannot_exceed_even_the_minimum_call_budget()
    {
        var (manifest, cases) = Setup(maxCalls: 11);
        Assert.Throws<InvalidDataException>(() => ExperimentSchedule.Create(manifest, cases));
    }

    [Fact]
    public void Host_plan_size_limit_is_enforced_before_allocating_trials()
    {
        var (manifest, cases) = Setup();
        Assert.Throws<InvalidDataException>(() => ExperimentSchedule.Create(manifest, cases, maximumTrials: 10));
    }

    [Fact]
    public void Schedule_cannot_plan_fewer_repetitions_than_the_pinned_policy_requires()
    {
        var (manifest, cases) = Setup(minimumRepetitions: 4);
        Assert.Throws<InvalidDataException>(() => ExperimentSchedule.Create(manifest, cases));
    }

    private static (ExperimentManifest Manifest, ScopedRevisionRef[] Cases) Setup(int maxCalls = 100, int minimumRepetitions = 3)
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        ScopedRevisionRef Ref() => new(scope, new(Guid.NewGuid(), new string('a', 64)));
        var baseline = Ref();
        return (new(scope, Ref(), baseline, Ref(), Ref(), Ref(), [Ref()], 3, 42,
            CandidateOrder.Interleaved, new(maxCalls, 100_000, 100_000, 5), new(minimumRepetitions, 5, null)), [Ref(), Ref()]);
    }
}
