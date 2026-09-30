using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class AttemptPriceProjectionTests
{
    [Fact]
    public void Explicit_price_revision_matches_call_and_repricing_does_not_mutate_prior_evidence()
    {
        var (call, terms) = Fixture();
        var first = Project(call, terms);
        var second = Project(call, terms with { OutputPerMillion = 1m });
        Assert.Equal(0.000164m, first.Amount);
        Assert.Equal(0.000264m, second.Amount);
        Assert.NotEqual(first.PriceSnapshotId, second.PriceSnapshotId);
        Assert.Equal("list_price_estimate", first.Status);
        Assert.Null(call.Cost);
    }

    [Theory]
    [InlineData("provider", "price_provider_mismatch")]
    [InlineData("model", "price_model_mismatch")]
    [InlineData("request", "price_request_mismatch")]
    [InlineData("tier", "price_tier_mismatch")]
    [InlineData("time", "price_period_mismatch")]
    [InlineData("context", "price_context_mismatch")]
    [InlineData("missing_provenance", "attempt_provenance_missing")]
    [InlineData("missing_model", "price_model_mismatch")]
    [InlineData("missing_tier", "price_tier_mismatch")]
    [InlineData("incomplete_price", "price_components_incomplete")]
    public void Unsupported_price_match_never_produces_numeric_cost(string variant, string reason)
    {
        var (call, terms) = Fixture();
        switch (variant)
        {
            case "provider": terms = terms with { Provider = "jev" }; break;
            case "model": terms = terms with { Model = "different" }; break;
            case "request": terms = terms with { RequestSha256 = new string('b', 64) }; break;
            case "tier": terms = terms with { ServiceTier = "priority" }; break;
            case "time": terms = terms with { ValidUntil = call.StartedAt!.Value }; break;
            case "context": terms = terms with { MaximumInputTokens = 999 }; break;
            case "missing_provenance": call = call with { Provider = null }; break;
            case "missing_model": call = call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { Model = null } } }; break;
            case "missing_tier": call = call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { ServiceTier = null } } }; break;
            default: terms = terms with { CoversAllBillableComponents = false }; break;
        }
        var result = Project(call, terms);
        Assert.Null(result.Amount);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void Unknown_and_failed_attempts_are_not_free()
    {
        var (call, terms) = Fixture();
        Assert.Null(Project(call with { State = "started", Outcome = null }, terms).Amount);
        var failed = call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { HttpStatus = 500 } } };
        Assert.Equal("billing_outcome_unconfirmed", Project(failed, terms).Reason);
    }

    [Fact]
    public void Price_hash_and_contract_are_validated_before_projection()
    {
        var (call, terms) = Fixture();
        Assert.Throws<InvalidDataException>(() => AttemptPriceProjection.Calculate(call,
            new(Guid.NewGuid(), new string('f', 64)), terms));
        Assert.Throws<InvalidDataException>(() => Project(call, terms with { MaximumInputTokens = -1 }));
    }

    [Fact]
    public void Explicit_tier_independent_terms_do_not_invent_an_observed_tier()
    {
        var (call, terms) = Fixture();
        call = call with { Outcome = call.Outcome! with { Observation = call.Outcome.Observation! with { ServiceTier = null } } };
        Assert.Equal(0.000164m, Project(call, terms with { ServiceTier = null }).Amount);
        Assert.Null(call.Outcome!.Observation!.ServiceTier);
    }

    private static CostEstimate Project(PhysicalAttemptMeasurement call, AttemptPriceTerms terms) =>
        AttemptPriceProjection.Calculate(call, new(Guid.NewGuid(), RevisionHasher.Hash(terms.ToRevisionDocument())), terms);

    internal static (PhysicalAttemptMeasurement Call, AttemptPriceTerms Terms) Fixture()
    {
        var at = DateTimeOffset.Parse("2026-09-28T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var call = new PhysicalAttemptMeasurement(Guid.NewGuid(), Guid.NewGuid(), "observed",
            new("response_received", TimeSpan.FromMilliseconds(20),
                new(200, "fixture-model", null, null, "available", new(1000, 200, 400, 100), null) { ServiceTier = "standard" }, null), null)
        { Provider = "openai", RequestSha256 = new string('a', 64), StartedAt = at };
        var terms = new AttemptPriceTerms(1, "openai", "fixture-model", "standard", new string('a', 64),
            at.AddDays(-1), at.AddDays(1), 0, 2000, "USD", "https://provider.example/pricing", at,
            0.1m, 0.01m, 0.5m, true);
        return (call, terms);
    }
}
