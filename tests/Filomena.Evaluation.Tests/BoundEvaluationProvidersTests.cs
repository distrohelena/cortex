using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class BoundEvaluationProvidersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("usd")]
    public void Host_endpoint_requires_explicit_reservation_currency(string? currency)
    {
        using var source = Source();
        var access = Access();
        Assert.Throws<InvalidDataException>(() => BoundEvaluationProviders.Create(source, access,
            Guid.NewGuid(), Claim(access.Scope), [Endpoint() with { ReservationCurrency = currency }]));
    }

    [Fact]
    public void Constructing_bound_clients_does_not_read_credentials_or_contact_services()
    {
        using var source = Source();
        var access = Access();
        var credentialsRead = 0;
        using var providers = BoundEvaluationProviders.Create(source, access, Guid.NewGuid(), Claim(access.Scope),
            [Endpoint() with { Credential = () => { credentialsRead++; return "not-real"; } }]);
        Assert.NotNull(providers.Factory);
        Assert.Equal(0, credentialsRead);
    }

    [Fact]
    public void Proposer_cannot_construct_paid_transport()
    {
        using var source = Source();
        var access = Access(EvaluationRole.Proposer);
        Assert.Throws<UnauthorizedAccessException>(() => BoundEvaluationProviders.Create(source, access,
            Guid.NewGuid(), Claim(access.Scope), [Endpoint()]));
    }

    [Fact]
    public void Foreign_claim_cannot_construct_paid_transport()
    {
        using var source = Source();
        Assert.Throws<UnauthorizedAccessException>(() => BoundEvaluationProviders.Create(source, Access(),
            Guid.NewGuid(), Claim(new(Guid.NewGuid(), Guid.NewGuid())), [Endpoint()]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Paid_provider_cannot_use_a_free_or_invalid_reservation(int cost)
    {
        using var source = Source();
        var access = Access();
        Assert.Throws<InvalidDataException>(() => BoundEvaluationProviders.Create(source, access, Guid.NewGuid(),
            Claim(access.Scope), [Endpoint() with { Reservation = new(100, 100, cost) }]));
    }

    [Fact]
    public void Arbitrary_remote_endpoint_is_not_admitted()
    {
        using var source = Source();
        var access = Access();
        Assert.Throws<InvalidDataException>(() => BoundEvaluationProviders.Create(source, access, Guid.NewGuid(),
            Claim(access.Scope), [Endpoint() with { Endpoint = new("https://invalid.example/") }]));
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role = EvaluationRole.Evaluator) =>
        EvaluationAccess.FromHost("evaluation-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static TrialClaim Claim(EvaluationScope scope)
    {
        var revision = new ScopedRevisionRef(scope, new(Guid.NewGuid(), new string('a', 64)));
        return new(Guid.NewGuid(), Guid.NewGuid(), 1, revision, revision, 0);
    }
    private static EvaluationEndpointSettings Endpoint() => new("luna-host", "openai",
        new("https://api.openai.com/v1/responses"), () => "not-real", new(100, 100, 0.01m), TimeSpan.FromSeconds(30))
    { ReservationCurrency = "BRL" };
}
