using Cortex.Evaluation.Contracts;
using System.Text.Json;

namespace Cortex.Evaluation.Comparison;

public sealed record ComparisonBundleExport(string Status, RevisionDocument? Document, string? Sha256);

public static class ComparisonBundleExporter
{
    // Pure packaging after the scoped store has revalidated this report. The scope
    // label is provenance, not an access token or proof supplied by bundle consumers.
    public static ComparisonBundleExport Create(EvaluationAccess access, StoredComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(report);
        access.Demand(EvaluationOperation.Evaluate);
        var issues = IssueSummaryProjection.Create(report);
        if (issues.Status != "available") return new(issues.Status, null, null);
        var snapshot = report.Snapshot!;
        var document = new RevisionDocument(1, "comparison-summary-bundle", JsonSerializer.SerializeToElement(new
        {
            scope = access.Scope,
            sourceReport = new { id = report.ReportId, sha256 = report.Sha256 },
            experimentId = snapshot.ExperimentId,
            planSha256 = snapshot.PlanSha256,
            policyRevision = snapshot.Report.PolicyRevision,
            statistics = snapshot.Report.Statistics,
            issues,
            priceProjections = snapshot.PriceProjections,
            artifactReferences = snapshot.SourceEvidence,
            contentMode = "summary_and_references",
            classification = "private_evaluation_evidence",
            activationAuthorized = false
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return new("available", document, RevisionHasher.Hash(document));
    }
}
