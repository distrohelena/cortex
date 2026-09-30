using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Npgsql;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class InvestigationHistoryTests
{
    [Theory]
    [InlineData("no_change")]
    [InlineData("investigate")]
    [InlineData("recommend_candidate")]
    public void Decision_preserves_reason_and_reopening_condition_without_activation(string outcome)
    {
        var input = Valid() with { Outcome = outcome };
        var result = InvestigationHistory.Validate(input);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal("Evidence is not sufficient.", result.Rationale);
        Assert.Equal("New independent evidence.", result.ReopenCondition);
        Assert.Equal(input.ExperimentId, result.ExperimentId);
    }

    [Fact]
    public void Evidence_is_snapshotted_and_ordered_as_a_set()
    {
        var first = new EvidenceArtifactRef(Guid.Parse("00000000-0000-0000-0000-000000000001"), new string('a', 64));
        var second = new EvidenceArtifactRef(Guid.Parse("00000000-0000-0000-0000-000000000002"), new string('b', 64));
        var mutable = new[] { second, first };
        var result = InvestigationHistory.Validate(Valid() with { Evidence = mutable });
        mutable[0] = first;
        Assert.Equal(new[] { first, second }, result.Evidence);
    }

    [Fact]
    public async Task Direct_service_denies_non_evaluator_before_connection()
    {
        using var source = Source();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InvestigationHistory(source).AddAsync(
            EvaluationAccess.FromHost("test", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Operator), Valid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Direct_service_rejects_invalid_data_before_connection()
    {
        using var source = Source();
        await Assert.ThrowsAsync<ArgumentException>(() => new InvestigationHistory(source).AddAsync(
            EvaluationAccess.FromHost("test", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator),
            Valid() with { Rationale = new string('x', 4097) }, TestContext.Current.CancellationToken));
    }

    private static InvestigationDecision Valid() => new(1, Guid.NewGuid(), Guid.NewGuid(), "no_change",
        "Evidence is not sufficient.", "New independent evidence.", [new(Guid.NewGuid(), new string('a', 64))]);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Timeout=1");
}
