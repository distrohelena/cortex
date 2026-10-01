using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ExperimentComparisonToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Comparison_requires_host_evaluator(EvaluationRole role)
    {
        var result = await Invoke(role, Payload());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("experiment")]
    [InlineData("policy")]
    [InlineData("inputs")]
    [InlineData("binding")]
    public async Task Comparison_requires_explicit_revision_and_case_binding_before_storage(string defect)
    {
        var payload = Payload();
        if (defect == "experiment") payload["experimentId"] = Guid.Empty;
        else if (defect == "policy") payload["policyRevision"] = new RevisionRef(Guid.NewGuid(), "not-a-hash");
        else if (defect == "inputs") payload["inputs"] = Array.Empty<object>();
        else payload["inputs"] = new[] { new { inputRevision = Ref(), bindingId = Guid.Empty } };
        var result = await Invoke(EvaluationRole.Evaluator, payload);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static RevisionRef Ref() => new(Guid.NewGuid(), new string('a', 64));

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing_attempt")]
    [InlineData("bad_revision")]
    public async Task Invalid_price_binding_is_rejected_before_storage(string defect)
    {
        var payload = Payload();
        var id = defect == "missing_attempt" ? Guid.Empty : Guid.NewGuid();
        var revision = defect == "bad_revision" ? new RevisionRef(Guid.NewGuid(), "bad") : Ref();
        var price = new { attemptId = id, priceRevision = revision };
        payload["prices"] = defect == "duplicate" ? new[] { price, price } : new[] { price };
        var result = await Invoke(EvaluationRole.Evaluator, payload);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }
    private static Dictionary<string, object> Payload() => new()
    {
        ["experimentId"] = Guid.NewGuid(),
        ["policyRevision"] = Ref(),
        ["inputs"] = new[] { new { inputRevision = Ref(), bindingId = Guid.NewGuid() } }
    };
    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        return await new PersistedEvaluationTools(source).InvokeAsync(
            EvaluationAccess.FromHost("comparison", new(Guid.NewGuid(), Guid.NewGuid()), role),
            JsonSerializer.Serialize(new { contractVersion = 1, operation = "experiment.compare", payload }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);
    }
}
