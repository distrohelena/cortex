using System.Text.Json;
using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Comparison;

public sealed record AttemptPriceTerms(int ContractVersion, string Provider, string Model, string? ServiceTier,
    string RequestSha256, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, long MinimumInputTokens,
    long MaximumInputTokens, string Currency, string Source, DateTimeOffset CapturedAt,
    decimal InputPerMillion, decimal CachedInputPerMillion, decimal OutputPerMillion, bool CoversAllBillableComponents)
{
    public RevisionDocument ToRevisionDocument() => new(ContractVersion, "attempt-price-terms", JsonSerializer.SerializeToElement(this));
}

public static class AttemptPriceProjection
{
    public static CostEstimate Calculate(PhysicalAttemptMeasurement attempt, RevisionRef revision, AttemptPriceTerms terms)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(terms);
        if (revision.Id == Guid.Empty || terms.ContractVersion != 1 ||
            terms.Provider is not ("openai" or "jev" or "lmstudio" or "laya") ||
            string.IsNullOrWhiteSpace(terms.Model) || terms.Model.Length > 256 || terms.Model.Any(char.IsControl) ||
            terms.ServiceTier is not null && (string.IsNullOrWhiteSpace(terms.ServiceTier) || terms.ServiceTier.Length > 128 || terms.ServiceTier.Any(char.IsControl)) ||
            terms.RequestSha256 is not { Length: 64 } || terms.RequestSha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
            terms.ValidFrom == default || terms.ValidUntil <= terms.ValidFrom || terms.MinimumInputTokens < 0 ||
            terms.MaximumInputTokens < terms.MinimumInputTokens || terms.Currency is not { Length: 3 } ||
            terms.Currency.Any(c => c is < 'A' or > 'Z') || !Uri.TryCreate(terms.Source, UriKind.Absolute, out _) ||
            terms.CapturedAt == default || terms.InputPerMillion < 0 || terms.CachedInputPerMillion < 0 || terms.OutputPerMillion < 0 ||
            RevisionHasher.Hash(terms.ToRevisionDocument()) != revision.Sha256)
            throw new InvalidDataException("Invalid or unpinned attempt price terms.");
        CostEstimate Unknown(string reason) => new(revision.Id, terms.Currency, null, "unavailable", reason);
        if (attempt.State != "observed" || attempt.Outcome?.Status != "response_received" ||
            attempt.Outcome.Observation is not { HttpSucceeded: true } observation)
            return Unknown("billing_outcome_unconfirmed");
        if (attempt.Provider is null || attempt.RequestSha256 is null || attempt.StartedAt is null or { Ticks: 0 })
            return Unknown("attempt_provenance_missing");
        if (attempt.Provider != terms.Provider) return Unknown("price_provider_mismatch");
        if (observation.Model != terms.Model) return Unknown("price_model_mismatch");
        if (attempt.RequestSha256 != terms.RequestSha256) return Unknown("price_request_mismatch");
        // Null is absence, not an inferred standard tier. Explicit attempt terms with a
        // null tier declare tier-independent applicability, not observed tier evidence.
        if (terms.ServiceTier is not null && observation.ServiceTier != terms.ServiceTier)
            return Unknown("price_tier_mismatch");
        if (attempt.StartedAt < terms.ValidFrom || attempt.StartedAt >= terms.ValidUntil)
            return Unknown("price_period_mismatch");
        if (observation.Usage?.InputTokens is not long input) return Unknown("usage_missing");
        if (input < terms.MinimumInputTokens || input > terms.MaximumInputTokens)
            return Unknown("price_context_mismatch");
        return CostProjection.Calculate(observation.Usage, new(revision.Id, terms.Currency, terms.Source, terms.CapturedAt,
            terms.InputPerMillion, terms.CachedInputPerMillion, terms.OutputPerMillion, terms.CoversAllBillableComponents));
    }
}
