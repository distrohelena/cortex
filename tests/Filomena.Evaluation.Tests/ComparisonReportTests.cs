using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Evaluation.Comparison;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ComparisonReportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Decode_rejects_changed_bytes_foreign_context_or_incomplete_dependency_list()
    {
        var value = Fixture();
        var bytes = Encode(value);
        var hash = Hash(bytes);
        Assert.Equal(value.ExperimentId, ComparisonReportStore.Decode(bytes, hash, value.ExperimentId, value.Report.PolicyRevision, value.SourceEvidence).ExperimentId);
        Assert.Throws<InvalidDataException>(() => ComparisonReportStore.Decode(bytes, new string('f', 64), value.ExperimentId, value.Report.PolicyRevision, value.SourceEvidence));
        Assert.Throws<InvalidDataException>(() => ComparisonReportStore.Decode(bytes, hash, Guid.NewGuid(), value.Report.PolicyRevision, value.SourceEvidence));
        Assert.Throws<InvalidDataException>(() => ComparisonReportStore.Decode(bytes, hash, value.ExperimentId,
            new(Guid.NewGuid(), value.Report.PolicyRevision.Sha256), value.SourceEvidence));
        Assert.Throws<InvalidDataException>(() => ComparisonReportStore.Decode(bytes, hash, value.ExperimentId, value.Report.PolicyRevision, []));
    }

    [Theory]
    [InlineData("planSha256")]
    [InlineData("inputs")]
    [InlineData("statistics")]
    public void Missing_required_report_structure_is_rejected_even_with_matching_payload_hash(string property)
    {
        var value = Fixture();
        var json = JsonNode.Parse(Encode(value))!;
        var snapshot = json["snapshot"]!;
        if (property == "statistics") snapshot["report"]!.AsObject().Remove(property);
        else snapshot.AsObject().Remove(property);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(json, Json);
        Assert.Throws<InvalidDataException>(() => ComparisonReportStore.Decode(bytes, Hash(bytes), value.ExperimentId, value.Report.PolicyRevision, value.SourceEvidence));
    }

    [Fact]
    public void Save_request_copies_collections_without_changing_input_order()
    {
        var value = Fixture();
        var inputs = value.Inputs.ToList();
        var prices = new List<AttemptPriceBinding> { new(Guid.NewGuid(), value.Report.PolicyRevision) };
        var request = new ComparisonReportSaveRequest(Guid.NewGuid(), new(value.ExperimentId, value.Report.PolicyRevision, inputs) { Prices = prices });
        var snapshot = ComparisonReportStore.Validate(request);
        inputs.Clear();
        prices.Clear();
        Assert.Single(snapshot.Comparison.Inputs);
        Assert.Single(snapshot.Comparison.Prices);
    }

    private static ExperimentComparisonSnapshot Fixture()
    {
        var revision = new RevisionRef(Guid.NewGuid(), new string('a', 64));
        return new(Guid.NewGuid(), new string('b', 64), [new(revision, Guid.NewGuid())],
            new(revision, new([new("candidate", 0, 1, false, ["incomplete"], null, null, null, null, null, null, null)], []), []))
        { SourceEvidence = [new(Guid.NewGuid(), new string('c', 64))] };
    }
    private static byte[] Encode(ExperimentComparisonSnapshot value) => JsonSerializer.SerializeToUtf8Bytes(new ComparisonReportEnvelope(1, "experiment-comparison:v1", value), Json);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
