using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CostTests
{
    [Fact]
    public void Cache_and_output_are_priced_once_reasoning_is_not_added_again()
    {
        var cost = CostProjection.Calculate(new(1_000, 200, 400, 100), Price());
        Assert.Equal(0.000164m, cost.Amount);
        Assert.Equal("USD", cost.Currency);
        Assert.Equal("list_price_estimate", cost.Status);
    }

    [Theory]
    [InlineData(null, 20L, 0L, "usage_missing")]
    [InlineData(100L, null, 0L, "usage_missing")]
    [InlineData(100L, 20L, null, "cache_usage_missing")]
    [InlineData(-1L, 20L, 0L, "usage_invalid")]
    [InlineData(100L, 20L, 101L, "usage_invalid")]
    public void Missing_or_invalid_usage_is_never_free(long? input, long? output, long? cached, string reason)
    {
        var cost = CostProjection.Calculate(new(input, output, cached, null), Price());
        Assert.Null(cost.Amount);
        Assert.Equal("unavailable", cost.Status);
        Assert.Equal(reason, cost.Reason);
    }

    [Fact]
    public void Equal_cache_rate_does_not_require_cache_counter()
    {
        var cost = CostProjection.Calculate(new(1_000_000, 0, null, null), Price() with
        { InputPerMillion = 0.042m, CachedInputPerMillion = 0.042m, OutputPerMillion = 0 });
        Assert.Equal(0.042m, cost.Amount);
    }

    [Fact]
    public void Repricing_produces_new_result_without_rewriting_previous_estimate()
    {
        var usage = new ProviderTokenUsage(1_000_000, 1_000_000, 0, 0);
        var originalPrice = Price();
        var original = CostProjection.Calculate(usage, originalPrice);
        var alternative = CostProjection.Calculate(usage, originalPrice with { Id = Guid.NewGuid(), OutputPerMillion = 1 });
        Assert.Equal(0.6m, original.Amount);
        Assert.Equal(1.1m, alternative.Amount);
        Assert.NotEqual(original.PriceSnapshotId, alternative.PriceSnapshotId);
    }

    [Theory]
    [InlineData("unmeasured_components")]
    [InlineData("currency")]
    [InlineData("negative_rate")]
    public void Unsupported_or_incomplete_price_is_not_a_numeric_estimate(string kind)
    {
        var price = kind switch
        {
            "unmeasured_components" => Price() with { CoversAllBillableComponents = false },
            "currency" => Price() with { Currency = "" },
            _ => Price() with { InputPerMillion = -1 }
        };
        Assert.Null(CostProjection.Calculate(new(10, 10, 0, null), price).Amount);
    }

    private static PriceSnapshot Price() => new(Guid.NewGuid(), "USD", "https://provider.example/pricing",
        DateTimeOffset.UnixEpoch, 0.1m, 0.01m, 0.5m, true);
}
