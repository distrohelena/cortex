using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ExperimentCancellationTests
{
    [Theory]
    [InlineData(EvaluationRole.Operator)]
    [InlineData(EvaluationRole.Evaluator)]
    public async Task Authorized_cancellation_still_requires_explicit_experiment(EvaluationRole role)
    {
        using var source = Source();
        var access = Access(role);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ExperimentCoordinator(source).CancelAsync(
            access, Guid.Empty, TestContext.Current.CancellationToken));
        var result = await new PersistedEvaluationTools(source).InvokeAsync(access, Request(Guid.Empty), TestContext.Current.CancellationToken);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Fact]
    public async Task Proposer_cannot_cancel_other_work_using_an_experiment_id()
    {
        using var source = Source();
        var access = Access(EvaluationRole.Proposer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExperimentCoordinator(source).CancelAsync(
            access, Guid.NewGuid(), TestContext.Current.CancellationToken));
        var result = await new PersistedEvaluationTools(source).InvokeAsync(access, Request(Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
    private static EvaluationAccess Access(EvaluationRole role) =>
        EvaluationAccess.FromHost("canceller", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static string Request(Guid id) => JsonSerializer.Serialize(new
    {
        contractVersion = 1,
        operation = "experiment.cancel",
        payload = new { experimentId = id }
    });
}
