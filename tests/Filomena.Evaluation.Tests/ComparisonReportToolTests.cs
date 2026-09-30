using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ComparisonReportToolTests
{
    [Theory]
    [InlineData("comparison.save", EvaluationRole.Proposer)]
    [InlineData("comparison.save", EvaluationRole.Operator)]
    [InlineData("comparison.show", EvaluationRole.Proposer)]
    [InlineData("comparison.show", EvaluationRole.Operator)]
    [InlineData("issues.list", EvaluationRole.Proposer)]
    [InlineData("issues.list", EvaluationRole.Operator)]
    [InlineData("bundle.export", EvaluationRole.Proposer)]
    [InlineData("bundle.export", EvaluationRole.Operator)]
    public async Task Derived_reports_require_evaluator(string operation, EvaluationRole role)
    {
        var result = await Invoke(operation, role, new { reportId = Guid.NewGuid() });
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("comparison.save")]
    [InlineData("comparison.show")]
    [InlineData("issues.list")]
    [InlineData("bundle.export")]
    public async Task Explicit_report_identity_required_before_storage(string operation)
    {
        var result = await Invoke(operation, EvaluationRole.Evaluator, new { reportId = Guid.Empty });
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static async Task<PersistedToolResponse> Invoke(string operation, EvaluationRole role, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        return await new PersistedEvaluationTools(source).InvokeAsync(
            EvaluationAccess.FromHost("reports", new(Guid.NewGuid(), Guid.NewGuid()), role),
            JsonSerializer.Serialize(new { contractVersion = 1, operation, payload }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);
    }
}
