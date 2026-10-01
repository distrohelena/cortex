using Cortex.Evaluation.Execution;

namespace Cortex.Evaluation.Comparison;

/// <summary>Rates already resolved for the observed model, tier and context range.
/// Extra billing components must be accounted for before CoversAllBillableComponents can be true.</summary>
public sealed record PriceSnapshot(Guid Id, string Currency, string Source, DateTimeOffset CapturedAt,
    decimal InputPerMillion, decimal CachedInputPerMillion, decimal OutputPerMillion, bool CoversAllBillableComponents);

public sealed record CostEstimate(Guid PriceSnapshotId, string Currency, decimal? Amount, string Status, string? Reason);

public static class CostProjection
{
    public static CostEstimate Calculate(ProviderTokenUsage? usage, PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);
        CostEstimate Unknown(string reason) => new(price.Id, price.Currency, null, "unavailable", reason);
        if (price.Id == Guid.Empty || price.Currency.Length != 3 || price.Currency.Any(c => c is < 'A' or > 'Z') ||
            !Uri.TryCreate(price.Source, UriKind.Absolute, out _) || price.CapturedAt == default ||
            price.InputPerMillion < 0 || price.CachedInputPerMillion < 0 || price.OutputPerMillion < 0)
            return Unknown("price_invalid");
        if (!price.CoversAllBillableComponents) return Unknown("price_components_incomplete");
        if (usage?.InputTokens is not long input || usage.OutputTokens is not long output)
            return Unknown("usage_missing");
        if (input < 0 || output < 0 || usage.CachedInputTokens < 0 || usage.CachedInputTokens > input ||
            usage.ReasoningTokens < 0 || usage.ReasoningTokens > output)
            return Unknown("usage_invalid");
        if (usage.CachedInputTokens is null && price.InputPerMillion != price.CachedInputPerMillion)
            return Unknown("cache_usage_missing");
        var cached = usage.CachedInputTokens ?? 0;
        try
        {
            var amount = checked(((input - cached) * price.InputPerMillion + cached * price.CachedInputPerMillion +
                output * price.OutputPerMillion) / 1_000_000m);
            // Reasoning tokens are a subset of output, not additional output.
            return new(price.Id, price.Currency, amount, "list_price_estimate", null);
        }
        catch (OverflowException)
        {
            return Unknown("cost_overflow");
        }
    }
}
