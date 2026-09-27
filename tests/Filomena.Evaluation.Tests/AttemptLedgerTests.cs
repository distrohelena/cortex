using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class AttemptLedgerTests
{
    [Theory]
    [InlineData("unsupported", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 10)]
    [InlineData("openai", "not-a-hash", 10)]
    [InlineData("openai", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", -1)]
    public async Task Invalid_call_intent_is_rejected_before_opening_database(string provider, string hash, int bytes)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("evaluator", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
        var ledger = new AttemptLedger(source, access, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new(100, 100, 0.01m));
        await Assert.ThrowsAsync<InvalidDataException>(() => ledger.BeginAsync(new(provider, hash, bytes), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Proposer_cannot_create_a_paid_dispatch_journal()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("proposer", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Proposer);
        Assert.Throws<UnauthorizedAccessException>(() => new AttemptLedger(source, access,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new(100, 100, 0.01m)));
    }

    [Fact]
    public void Unknown_cost_is_rejected_before_database_access()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("evaluator", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
        Assert.Throws<InvalidDataException>(() => new AttemptLedger(source, access,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new(100, 100, null)));
    }
}
