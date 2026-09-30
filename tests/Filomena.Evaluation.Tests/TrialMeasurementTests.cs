using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class TrialMeasurementTests
{
    [Fact]
    public void Multi_call_recipe_counts_every_call_but_uses_pipeline_elapsed()
    {
        var id = Guid.NewGuid();
        var value = TrialMeasurement.Aggregate(id, TimeSpan.FromMilliseconds(150), [Call(id, 0.01m), Call(id, 0.02m)]);
        Assert.Equal(2, value.PhysicalCalls);
        Assert.Equal(0.03m, value.EstimatedCost);
        Assert.Equal("USD", value.Currency);
        Assert.Equal(200, value.InputTokens);
        Assert.Equal(20, value.OutputTokens);
        Assert.Equal(150, value.PipelineElapsed!.Value.TotalMilliseconds);
        Assert.False(value.HasUnresolvedAttempts);
        Assert.Equal("list_price_estimate", value.CostStatus);
    }

    [Fact]
    public void Missing_usage_on_one_call_is_not_zero_or_a_partial_total()
    {
        var id = Guid.NewGuid();
        var second = Call(id, null) with
        {
            Outcome = new("response_received", TimeSpan.FromMilliseconds(100),
            new(200, "model", null, null, "unavailable", null, "usage_missing"), null)
        };
        var value = TrialMeasurement.Aggregate(id, null, [Call(id, 0.01m), second]);
        Assert.Null(value.InputTokens);
        Assert.Null(value.OutputTokens);
        Assert.Null(value.EstimatedCost);
        Assert.Null(value.Currency);
        Assert.Null(value.PipelineElapsed);
        Assert.Equal(2, value.PhysicalCalls);
    }

    [Fact]
    public void Indeterminate_and_started_calls_remain_visible_without_refund_or_false_completion()
    {
        var id = Guid.NewGuid();
        var value = TrialMeasurement.Aggregate(id, null,
            [Call(id, 0.01m), new(Guid.NewGuid(), id, "indeterminate", null, null), new(Guid.NewGuid(), id, "started", null, null)]);
        Assert.Equal(3, value.PhysicalCalls);
        Assert.True(value.HasUnresolvedAttempts);
        Assert.Null(value.EstimatedCost);
        Assert.Null(value.InputTokens);
    }

    [Fact]
    public void Http_error_still_consumes_observed_usage_and_cost()
    {
        var id = Guid.NewGuid();
        var call = Call(id, 0.03m);
        call = call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { HttpStatus = 500 } } };
        var value = TrialMeasurement.Aggregate(id, null, [call]);
        Assert.Equal(0.03m, value.EstimatedCost);
        Assert.Equal(100, value.InputTokens);
        Assert.True(value.HasFailedResponses);
    }

    [Fact]
    public void Different_currencies_do_not_get_added()
    {
        var id = Guid.NewGuid();
        var second = Call(id, 0.02m);
        second = second with { Cost = second.Cost! with { Currency = "BRL" } };
        var value = TrialMeasurement.Aggregate(id, null, [Call(id, 0.01m), second]);
        Assert.Null(value.Currency);
        Assert.Null(value.EstimatedCost);
    }

    [Fact]
    public void Duplicated_attempt_and_wrong_trial_are_rejected()
    {
        var id = Guid.NewGuid();
        var call = Call(id, 0.01m);
        Assert.Throws<InvalidDataException>(() => TrialMeasurement.Aggregate(id, null, [call, call]));
        Assert.Throws<InvalidDataException>(() => TrialMeasurement.Aggregate(Guid.NewGuid(), null, [call]));
    }

    [Fact]
    public void No_calls_is_missing_measurement_not_free_execution()
    {
        var value = TrialMeasurement.Aggregate(Guid.NewGuid(), null, []);
        Assert.Equal(0, value.PhysicalCalls);
        Assert.Null(value.EstimatedCost);
        Assert.Null(value.InputTokens);
        Assert.Equal("unavailable", value.CostStatus);
    }

    internal static PhysicalAttemptMeasurement Call(Guid trial, decimal? cost) => new(Guid.NewGuid(), trial, "observed",
        new("response_received", TimeSpan.FromMilliseconds(100), new(200, "model", null, null, "available", new(100, 10, 0, 0), null), null),
        cost is null ? null : new(Guid.NewGuid(), "USD", cost, "list_price_estimate", null));

    [Fact]
    public void Overflow_is_unknown_and_does_not_erase_physical_calls_or_price_sources()
    {
        var id = Guid.NewGuid();
        var first = Call(id, decimal.MaxValue);
        first = first with { Outcome = first.Outcome! with { Observation = first.Outcome.Observation! with { Usage = new(long.MaxValue, 10, 0, 0) } } };
        var second = Call(id, 1m);
        var value = TrialMeasurement.Aggregate(id, TimeSpan.FromMilliseconds(100), [first, second]);
        Assert.Null(value.InputTokens);
        Assert.Equal(20, value.OutputTokens);
        Assert.Null(value.EstimatedCost);
        Assert.Equal(2, value.PhysicalCalls);
        Assert.Equal(new[] { first.Cost!.PriceSnapshotId, second.Cost!.PriceSnapshotId }, value.PriceSnapshotIds);
        Assert.Equal(new[] { first.AttemptId, second.AttemptId }, value.AttemptIds);
    }

    [Theory]
    [InlineData("unknown_state")]
    [InlineData("observed_missing_outcome")]
    [InlineData("started_with_outcome")]
    [InlineData("negative_usage")]
    [InlineData("negative_cost")]
    [InlineData("unavailable_with_amount")]
    public void Incoherent_persisted_measurements_are_rejected(string variant)
    {
        var id = Guid.NewGuid();
        var call = Call(id, 0.01m);
        call = variant switch
        {
            "unknown_state" => call with { State = "complete-ish" },
            "observed_missing_outcome" => call with { Outcome = null },
            "started_with_outcome" => call with { State = "started" },
            "negative_usage" => call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { Usage = new(-1, 10, 0, 0) } } },
            "negative_cost" => call with { Cost = call.Cost! with { Amount = -1m } },
            _ => call with { Cost = call.Cost! with { Status = "unavailable" } }
        };
        Assert.Throws<InvalidDataException>(() => TrialMeasurement.Aggregate(id, null, [call]));
    }
}
