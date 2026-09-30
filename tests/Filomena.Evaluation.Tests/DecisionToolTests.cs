using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class DecisionToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Decision_is_evaluator_only_and_never_grants_activation(EvaluationRole role)
    {
        var result = await Invoke(role, Valid());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("contractVersion", 2)]
    [InlineData("decisionId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("experimentId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("outcome", "activate")]
    [InlineData("outcome", null)]
    [InlineData("rationale", "")]
    [InlineData("reopenCondition", "")]
    [InlineData("evidence", null)]
    public async Task Invalid_decision_is_rejected_before_storage(string key, object? value)
    {
        var request = Valid();
        request[key] = value;
        var result = await Invoke(EvaluationRole.Evaluator, request);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null_item")]
    [InlineData("bad_hash")]
    [InlineData("duplicate")]
    public async Task Evidence_requires_nonempty_unique_structural_references(string variant)
    {
        var request = Valid();
        var reference = new { id = Guid.NewGuid(), sha256 = new string('a', 64) };
        request["evidence"] = variant switch
        {
            "empty" => Array.Empty<object>(),
            "null_item" => new object?[] { null },
            "bad_hash" => new object[] { new { reference.id, sha256 = "bad" } },
            _ => new object[] { reference, reference }
        };
        var result = await Invoke(EvaluationRole.Evaluator, request);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static Dictionary<string, object?> Valid() => new()
    {
        ["contractVersion"] = 1,
        ["decisionId"] = Guid.NewGuid(),
        ["experimentId"] = Guid.NewGuid(),
        ["outcome"] = "no_change",
        ["rationale"] = "Insufficient evidence to change the profile.",
        ["reopenCondition"] = "An independently evaluated paired corpus becomes available.",
        ["evidence"] = new[] { new { id = Guid.NewGuid(), sha256 = new string('a', 64) } }
    };

    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("decision-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
        var request = JsonSerializer.Serialize(new { contractVersion = 1, operation = "decision.add", payload });
        return await new PersistedEvaluationTools(source).InvokeAsync(access, request, TestContext.Current.CancellationToken);
    }
}
