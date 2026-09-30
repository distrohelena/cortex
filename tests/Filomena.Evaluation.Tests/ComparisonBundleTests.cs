using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ComparisonBundleTests
{
    [Fact]
    public void Summary_is_hashed_repeatable_scoped_and_not_an_activation_package()
    {
        var access = Access(EvaluationRole.Evaluator);
        var report = IssueSummaryTests.Report([]);
        var first = ComparisonBundleExporter.Create(access, report);
        var second = ComparisonBundleExporter.Create(access, report);
        Assert.Equal("available", first.Status);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(RevisionHasher.Hash(first.Document!), first.Sha256);
        Assert.Equal("comparison-summary-bundle", first.Document!.Kind);
        var payload = first.Document.Payload;
        Assert.Equal(access.Scope.ProjectId, payload.GetProperty("scope").GetProperty("projectId").GetGuid());
        Assert.Equal(report.ReportId, payload.GetProperty("sourceReport").GetProperty("id").GetGuid());
        Assert.Equal(report.Sha256, payload.GetProperty("sourceReport").GetProperty("sha256").GetString());
        Assert.False(payload.GetProperty("activationAuthorized").GetBoolean());
        Assert.False(payload.TryGetProperty("prompts", out _));
        Assert.False(payload.TryGetProperty("credentials", out _));
        Assert.False(payload.TryGetProperty("artifactPayloads", out _));
    }

    [Theory]
    [InlineData(EvaluationRole.Proposer)]
    [InlineData(EvaluationRole.Operator)]
    public void Export_requires_host_evaluator(EvaluationRole role) => Assert.Throws<UnauthorizedAccessException>(() =>
        ComparisonBundleExporter.Create(Access(role), IssueSummaryTests.Report([])));

    [Theory]
    [InlineData("evidence_unavailable")]
    [InlineData("invalid_record")]
    public void Unavailable_source_produces_no_export_payload_or_hash(string status)
    {
        var result = ComparisonBundleExporter.Create(Access(EvaluationRole.Evaluator),
            new(Guid.NewGuid(), new string('a', 64), status, null));
        Assert.Equal(status, result.Status);
        Assert.Null(result.Document);
        Assert.Null(result.Sha256);
    }

    private static EvaluationAccess Access(EvaluationRole role) => EvaluationAccess.FromHost("export",
        new(Guid.NewGuid(), Guid.NewGuid()), role);
}
