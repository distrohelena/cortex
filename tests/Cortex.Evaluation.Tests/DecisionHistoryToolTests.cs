using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Comparison;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class DecisionHistoryToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Direct_reader_checks_authority_before_opening_database(EvaluationRole role)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DecisionHistoryReader(source).ReadAsync(
            EvaluationAccess.FromHost("reader", new(Guid.NewGuid(), Guid.NewGuid()), role),
            10, null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Direct_reader_checks_page_bounds_before_opening_database()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new DecisionHistoryReader(source).ReadAsync(
            EvaluationAccess.FromHost("reader", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator),
            101, null, null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"limit\":10}", EvaluationRole.Proposer, "unauthorized")]
    [InlineData("{\"limit\":10}", EvaluationRole.Operator, "unauthorized")]
    [InlineData("{\"limit\":0}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":101}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10,\"after\":\"00000000-0000-0000-0000-000000000000\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10,\"experimentId\":\"00000000-0000-0000-0000-000000000000\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10,\"projectId\":\"foreign\"}", EvaluationRole.Evaluator, "invalid_request")]
    [InlineData("{\"limit\":10,\"includeRedacted\":true}", EvaluationRole.Evaluator, "invalid_request")]
    public async Task History_checks_current_host_authority_and_bounds_before_database(string payload, EvaluationRole role, string error)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("history-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
        var result = await new PersistedEvaluationTools(source).InvokeAsync(access,
            "{\"contractVersion\":1,\"operation\":\"decision.list\",\"payload\":" + payload + "}", TestContext.Current.CancellationToken);
        Assert.Equal(error, result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }
}
