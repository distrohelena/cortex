using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Storage;
using Cortex.Evaluation.Tools;
using Npgsql;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class AssessmentPersistenceToolTests
{
    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public async Task Only_evaluator_can_append_assessment(EvaluationRole role)
    {
        using var source = Source();
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(role), Request(Valid()), TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("empty_case")]
    [InlineData("empty_revision")]
    [InlineData("bad_revision_hash")]
    [InlineData("null_revision_hash")]
    [InlineData("null_revision")]
    [InlineData("null_evidence_item")]
    [InlineData("empty_evidence_id")]
    [InlineData("bad_evidence_hash")]
    [InlineData("null_evidence_hash")]
    [InlineData("null_evidence")]
    [InlineData("unknown_outcome")]
    public async Task Malformed_assessment_never_attempts_persistence(string mutation)
    {
        using var source = Source();
        var assessment = Mutate(mutation);
        var result = await new PersistedEvaluationTools(source).InvokeAsync(Access(EvaluationRole.Evaluator), Request(assessment), TestContext.Current.CancellationToken);
        Assert.Equal("invalid_request", result.ErrorCode);
        Assert.Equal("not_attempted", result.PersistenceStatus);
    }

    [Theory]
    [InlineData("empty_revision")]
    [InlineData("null_evidence_item")]
    [InlineData("empty_evidence_id")]
    [InlineData("bad_evidence_hash")]
    [InlineData("null_revision_hash")]
    [InlineData("null_revision")]
    [InlineData("null_evidence_hash")]
    public async Task Direct_store_call_checks_all_references_before_connection(string mutation)
    {
        using var source = Source();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new PostgresEvaluationStore(source)
            .AppendAssessmentAsync(Access(EvaluationRole.Evaluator), Mutate(mutation), TestContext.Current.CancellationToken));
    }

    private static Assessment Mutate(string mutation)
    {
        var original = Valid();
        return mutation switch
        {
            "empty_case" => original with { CaseId = Guid.Empty },
            "empty_revision" => original with { EvaluatorRevision = new(Guid.Empty, new string('a', 64)) },
            "bad_revision_hash" => original with { EvaluatorRevision = new(Guid.NewGuid(), "bad") },
            "null_revision_hash" => original with { EvaluatorRevision = new(Guid.NewGuid(), null!) },
            "null_revision" => original with { EvaluatorRevision = null! },
            "null_evidence_item" => original with { Evidence = [null!] },
            "empty_evidence_id" => original with { Evidence = [new(Guid.Empty, new string('a', 64))] },
            "bad_evidence_hash" => original with { Evidence = [new(Guid.NewGuid(), "bad")] },
            "null_evidence_hash" => original with { Evidence = [new(Guid.NewGuid(), null!)] },
            "null_evidence" => original with { Evidence = null! },
            "unknown_outcome" => original with { Outcome = (AssessmentOutcome)99 },
            _ => throw new ArgumentException("Unknown fixture.")
        };
    }

    private static Assessment Valid() => new(Guid.NewGuid(), null, "participation", new(Guid.NewGuid(), new string('a', 64)),
        "human", AssessmentOutcome.Unscored, null, [new(Guid.NewGuid(), new string('b', 64))], null);
    private static string Request(Assessment value) => JsonSerializer.Serialize(new
    {
        contractVersion = 1,
        operation = "assessment.add",
        payload = value
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("assessment-test", new(Guid.NewGuid(), Guid.NewGuid()), role);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
