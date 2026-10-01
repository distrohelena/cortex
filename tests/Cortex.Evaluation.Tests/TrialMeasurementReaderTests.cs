using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class TrialMeasurementReaderTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Measurement_reader_does_not_grant_evaluation_evidence_to_other_roles(EvaluationRole role)
    {
        var result = await Invoke(role, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_identity_fails_before_storage(bool emptyExperiment)
    {
        var result = await Invoke(EvaluationRole.Evaluator,
            emptyExperiment ? Guid.Empty : Guid.NewGuid(), emptyExperiment ? Guid.NewGuid() : Guid.Empty);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, Guid experimentId, Guid trialId)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("measurement", new(Guid.NewGuid(), Guid.NewGuid()), role);
        return await new PersistedEvaluationTools(source).InvokeAsync(access, System.Text.Json.JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "trial.measurement",
            payload = new { experimentId, trialId }
        }), TestContext.Current.CancellationToken);
    }
}
