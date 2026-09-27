using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CompletionTrialExecutorTests
{
    [Fact]
    public async Task Proposer_is_rejected_before_any_database_or_provider_access()
    {
        using var source = Source();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CompletionTrialExecutor(source)
            .ExecuteNextAsync(Access(EvaluationRole.Proposer), Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_experiment_is_rejected_before_database_access()
    {
        using var source = Source();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CompletionTrialExecutor(source)
            .ExecuteNextAsync(Access(), Guid.Empty, [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_does_not_claim_or_execute_work()
    {
        using var source = Source();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CompletionTrialExecutor(source)
            .ExecuteNextAsync(Access(), Guid.NewGuid(), [], new CancellationToken(true)));
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role = EvaluationRole.Evaluator) =>
        EvaluationAccess.FromHost("test", new(Guid.NewGuid(), Guid.NewGuid()), role);
}
