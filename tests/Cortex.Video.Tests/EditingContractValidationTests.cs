using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class EditingContractValidationTests
{
    [Theory, MemberData(nameof(EditingSerializationTests.Fixtures), MemberType = typeof(EditingSerializationTests))]
    public void ValidSnapshotsRespectTheContract(string name)
    {
        var issues = EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture(name)), EditingBundleValidationTests.Context(name));
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("duplicate_beat", "edit.id_duplicate")]
    [InlineData("unknown_asset", "edit.reference_unknown")]
    [InlineData("wrong_aspect", "edit.aspect_mismatch")]
    [InlineData("exact_meaning", "edit.search_query")]
    [InlineData("silent_duration", "edit.duration_required")]
    [InlineData("weight_sum", "edit.weights_sum")]
    [InlineData("audio_conflict", "edit.audio_fields")]
    public void MalformedIntentReportsSpecificIssues(string mutation, string code)
    {
        var value = Node("script.json");
        switch (mutation)
        {
            case "duplicate_beat": value["beats"]![1]!["id"] = "hook"; break;
            case "unknown_asset": value["beats"]![0]!["visual"]!["source"] = "missing"; break;
            case "wrong_aspect": value["project"]!["aspect_ratio"] = "16:9"; break;
            case "exact_meaning": value["beats"]![1]!["audio"]!["search"]!["match"] = "exact"; break;
            case "silent_duration": value["beats"]![0]!["visual"]!.AsObject().Remove("duration_sec"); break;
            case "weight_sum": value["beats"]![1]!["audio"]!["selection"]!["score"]!["delivery"] = .1; break;
            case "audio_conflict": value["beats"]![0]!["audio"]!["script"] = "Unexpected speech"; break;
        }
        var issues = EditingContractValidator.Validate(Parse(value));
        Assert.Contains(issues, issue => issue.Code == code && issue.Path.StartsWith('/'));
    }

    [Theory]
    [InlineData("selected", "edit.reference_unknown")]
    [InlineData("range", "edit.interval_invalid")]
    [InlineData("alignment", "edit.pending_alignment")]
    [InlineData("score", "edit.score_unmeasured")]
    public void PlansCannotHideMissingEvidence(string mutation, string code)
    {
        var value = Node("edit_plan.json");
        switch (mutation)
        {
            case "selected": value["beats"]![1]!["selected_candidate_id"] = "missing"; break;
            case "range": value["beats"]![1]!["candidates"]![0]!["source_out"] = 999; break;
            case "alignment": value["beats"]![2]!["status"] = "matched"; break;
            case "score": value["beats"]![1]!["candidates"]![0]!["match"]!["take_score"] = .99; break;
        }
        Assert.Contains(EditingContractValidator.Validate(Parse(value), EditingBundleValidationTests.Context("edit_plan.json")), issue => issue.Code == code);
    }

    [Fact]
    public void ProviderFailureDoesNotCreateANoSuitableClipRecordingReason()
    {
        var plan = Node("edit_plan.json"); plan["beats"]![3]!["audio"]!["search"]!["status"] = "failed";
        var context = EditingBundleValidationTests.Context("recording_tasks.json") with { Plan = (EditPlanDocument)Parse(plan) };
        Assert.Contains(EditingContractValidator.Validate(Parse(Node("recording_tasks.json")), context), issue => issue.Code == "edit.search_not_completed");
    }

    [Fact]
    public void TranscriptOnlyEvidenceCannotClaimAudioQualityAndMalformedScoresAreIssues()
    {
        var value = Node("edit_plan.json");
        value["beats"]![1]!["candidates"]![0]!["match"]!["evaluated_metrics"]!["audio_quality"] = .9;
        Assert.Contains(EditingContractValidator.Validate(Parse(value), EditingBundleValidationTests.Context("edit_plan.json")), issue => issue.Code == "edit.score_modality");
        value["beats"]![1]!["candidates"]![0]!["match"]!["semantic_score"] = "high";
        Assert.Contains(EditingContractValidator.Validate(Parse(value), EditingBundleValidationTests.Context("edit_plan.json")), issue => issue.Code == "edit.score_range");
    }

    [Fact]
    public void OnlyZeroWeightUnavailableMetricsCanBeExcludedFromAFullScore()
    {
        var script = Node("script.json");
        script["beats"]![1]!["audio"]!["selection"]!["score"] = JsonNode.Parse("{\"semantic_match\":1,\"audio_quality\":0}");
        var value = Node("edit_plan.json"); value["beats"]![1]!["candidates"]![0]!["match"]!["take_score"] = .94;
        var context = EditingBundleValidationTests.Context("edit_plan.json") with { Script = (EditScriptDocument)Parse(script) };
        Assert.DoesNotContain(EditingContractValidator.Validate(Parse(value), context), issue => issue.Code is "edit.score_unmeasured" or "edit.score_weighted");
    }

    [Fact]
    public void ExactEvidenceAndFocusRequestsCannotContradictTheirIntent()
    {
        var script = Node("script.json");
        script["beats"]![1]!["audio"]!["search"] = JsonNode.Parse("{\"speaker\":\"helena\",\"match\":\"exact\",\"text\":\"Uma frase completamente diferente.\"}");
        var plan = Node("edit_plan.json"); plan["beats"]![1]!["candidates"]![0]!["match"]!["mode"] = "exact";
        var context = EditingBundleValidationTests.Context("edit_plan.json") with { Script = (EditScriptDocument)Parse(script) };
        Assert.Contains(EditingContractValidator.Validate(Parse(plan), context), issue => issue.Code == "edit.exact_wording");
        script["beats"]![0]!["visual"]!["focus"]!["type"] = "invented_effect";
        Assert.Contains(EditingContractValidator.Validate(Parse(script)), issue => issue.Code == "edit.focus_invalid");
    }

    [Fact]
    public void NullSemanticCollectionMembersFailWhileAnnotationNullsRemainAllowed()
    {
        var script = Node("script.json"); script["beats"]![0] = null;
        Assert.Throws<InvalidDataException>(() => Parse(script));
    }

    [Fact]
    public void MalformedExtensionEvidenceReturnsAnIssueInsteadOfCrashing()
    {
        var plan = Node("edit_plan.json"); plan["beats"]![1]!["candidates"]![0]!["match"]!["mode"] = 3;
        Assert.Contains(EditingContractValidator.Validate(Parse(plan), EditingBundleValidationTests.Context("edit_plan.json")), issue => issue.Code == "edit.match_evidence");
    }

    internal static JsonNode Node(string name) => JsonNode.Parse(EditingSerializationTests.Fixture(name).GetRawText())!;
    internal static EditingDocument Parse(JsonNode value) => EditingJson.Parse(JsonSerializer.SerializeToElement(value));
}
