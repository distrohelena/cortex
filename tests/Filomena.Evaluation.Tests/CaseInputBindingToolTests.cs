using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CaseInputBindingToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Operator)]
    [InlineData(EvaluationRole.Proposer)]
    public async Task Binding_requires_host_evaluator(EvaluationRole role)
    {
        var result = await Invoke(role, Valid());
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("bindingId")]
    [InlineData("caseId")]
    [InlineData("caseArtifact")]
    [InlineData("inputRevision")]
    [InlineData("provenanceRevision")]
    public async Task Missing_binding_identity_cannot_reach_storage(string field)
    {
        var payload = Valid();
        payload[field] = field.EndsWith("Id", StringComparison.Ordinal) ? Guid.Empty : null;
        var result = await Invoke(EvaluationRole.Evaluator, payload);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    private static Dictionary<string, object?> Valid() => new()
    {
        ["bindingId"] = Guid.NewGuid(),
        ["caseId"] = Guid.NewGuid(),
        ["caseArtifact"] = new { id = Guid.NewGuid(), sha256 = new string('a', 64) },
        ["inputRevision"] = new { id = Guid.NewGuid(), sha256 = new string('b', 64) },
        ["provenanceRevision"] = new { id = Guid.NewGuid(), sha256 = new string('c', 64) }
    };

    private static async Task<PersistedToolResponse> Invoke(EvaluationRole role, object payload)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
        var access = EvaluationAccess.FromHost("binding-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
        return await new PersistedEvaluationTools(source).InvokeAsync(access,
            JsonSerializer.Serialize(new { contractVersion = 1, operation = "case.bind_input", payload }), TestContext.Current.CancellationToken);
    }
}
