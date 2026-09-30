using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class BudgetExposureToolTests
{
    [Fact]
    public async Task Proposer_cannot_read_operational_budget_accounting()
    {
        var result = await Invoke(EvaluationRole.Proposer, Guid.NewGuid());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData(EvaluationRole.Evaluator)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Scope_does_not_replace_explicit_experiment(EvaluationRole role)
    {
        var result = await Invoke(role, Guid.Empty);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, Guid experiment)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        return await new PersistedEvaluationTools(source).InvokeAsync(
            EvaluationAccess.FromHost("accounting", new(Guid.NewGuid(), Guid.NewGuid()), role),
            JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.accounting", payload = new { experimentId = experiment } }),
            TestContext.Current.CancellationToken);
    }
}
