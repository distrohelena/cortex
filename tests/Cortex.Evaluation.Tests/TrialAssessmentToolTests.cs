using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class TrialAssessmentToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Selection_requires_evaluator(EvaluationRole role)
    {
        var result = await Invoke(role, Payload());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("experimentId")]
    [InlineData("trialId")]
    [InlineData("bindingId")]
    [InlineData("criterion")]
    public async Task Context_is_required_before_storage(string key)
    {
        var payload = Payload();
        payload[key] = key == "criterion" ? "" : Guid.Empty;
        var result = await Invoke(EvaluationRole.Evaluator, payload);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static Dictionary<string, object?> Payload() => new()
    {
        ["experimentId"] = Guid.NewGuid(),
        ["trialId"] = Guid.NewGuid(),
        ["bindingId"] = Guid.NewGuid(),
        ["criterion"] = "correctness"
    };
    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        return await new PersistedEvaluationTools(source).InvokeAsync(
            EvaluationAccess.FromHost("selection", new(Guid.NewGuid(), Guid.NewGuid()), role),
            JsonSerializer.Serialize(new { contractVersion = 1, operation = "trial.assessment", payload }), TestContext.Current.CancellationToken);
    }
}
