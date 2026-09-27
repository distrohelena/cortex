using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class RevisionTests
{
    [Fact]
    public void Hash_ignores_property_order_and_tracks_behavioral_payload()
    {
        var first = Document("""{"z":1,"nested":{"b":true,"a":"x"}}""");
        var reordered = Document("""{"nested":{"a":"x","b":true},"z":1}""");
        Assert.Equal(RevisionHasher.Hash(first), RevisionHasher.Hash(reordered));

        var prompt = Document("""{"prompt":"changed","threshold":0.7,"choiceQuestions":["a","b"]}""");
        var threshold = Document("""{"prompt":"same","threshold":0.8,"choiceQuestions":["a","b"]}""");
        var choices = Document("""{"prompt":"same","threshold":0.7,"choiceQuestions":["a","c"]}""");
        var baseline = Document("""{"prompt":"same","threshold":0.7,"choiceQuestions":["a","b"]}""");
        Assert.NotEqual(RevisionHasher.Hash(baseline), RevisionHasher.Hash(prompt));
        Assert.NotEqual(RevisionHasher.Hash(baseline), RevisionHasher.Hash(threshold));
        Assert.NotEqual(RevisionHasher.Hash(baseline), RevisionHasher.Hash(choices));
    }

    [Fact]
    public void Hash_preserves_array_order_and_number_lexical_form()
    {
        Assert.NotEqual(RevisionHasher.Hash(Document("""{"items":[1,2]}""")), RevisionHasher.Hash(Document("""{"items":[2,1]}""")));
        Assert.NotEqual(RevisionHasher.Hash(Document("""{"value":1}""")), RevisionHasher.Hash(Document("""{"value":1.0}""")));
    }

    [Fact]
    public void Hash_rejects_duplicate_properties_unsupported_version_and_invalid_document()
    {
        Assert.Throws<ArgumentException>(() => RevisionHasher.Hash(Document("""{"x":1,"x":2}""")));
        Assert.Throws<ArgumentOutOfRangeException>(() => RevisionHasher.Hash(new RevisionDocument(2, "config", Json("{}"))));
        Assert.Throws<ArgumentException>(() => RevisionHasher.Hash(new RevisionDocument(1, "config", default)));
        Assert.Throws<ArgumentException>(() => RevisionHasher.Hash(new RevisionDocument(1, "", Json("{}"))));
    }

    [Fact]
    public void Hash_configuration_profile_tracks_model_parameters_threshold_criteria_and_summary()
    {
        var baseline = FullProfile();
        var baselineHash = RevisionHasher.Hash(baseline);
        Assert.NotEqual(baselineHash, RevisionHasher.Hash(FullProfile(model: "jev-1.14.0")));
        Assert.NotEqual(baselineHash, RevisionHasher.Hash(FullProfile(summaryParameters: """{"reasoningEffort":"high","maxOutputTokens":4096}""")));
        Assert.NotEqual(baselineHash, RevisionHasher.Hash(FullProfile(threshold: 0.6)));
        Assert.NotEqual(baselineHash, RevisionHasher.Hash(FullProfile(criterion: "Social chat")));
        Assert.NotEqual(baselineHash, RevisionHasher.Hash(FullProfile(summaryPrompt: """{"text":"new summary prompt"}""")));
    }

    [Fact]
    public void Full_profile_hash_round_trips_through_system_text_json()
    {
        var profile = FullProfile();
        var deserialized = JsonSerializer.Deserialize<ConfigurationProfile>(JsonSerializer.Serialize(profile));

        Assert.NotNull(deserialized);
        Assert.Equal(RevisionHasher.Hash(profile), RevisionHasher.Hash(deserialized));
    }

    [Fact]
    public void Manifest_round_trips_through_system_text_json_and_remains_valid()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var manifest = new ExperimentManifest(scope, Ref(scope), Ref(scope), Ref(scope), Ref(scope), Ref(scope),
            [Ref(scope)], 2, 7, CandidateOrder.Interleaved, new(20, 10000, 10000, 5m), new(1, 3, null));
        var deserialized = JsonSerializer.Deserialize<ExperimentManifest>(JsonSerializer.Serialize(manifest));

        Assert.NotNull(deserialized);
        Assert.Empty(ManifestValidator.Validate(deserialized));
        Assert.Single(deserialized.Candidates);
    }

    [Fact]
    public void Revision_and_profile_snapshot_json_before_source_documents_are_disposed()
    {
        RevisionDocument revision;
        ConfigurationProfile profile;
        using (var revisionSource = JsonDocument.Parse("""{"answer":"stable"}"""))
            using (var parametersSource = JsonDocument.Parse("""{"reasoningEffort":"high"}"""))
        using (var schemaSource = JsonDocument.Parse("""{"contractVersion":1}"""))
        using (var promptSource = JsonDocument.Parse("""{"text":"hello"}"""))
        {
            revision = new RevisionDocument(1, "configuration", revisionSource.RootElement);
            profile = new ConfigurationProfile(1, "completion", "openai", "model", "endpoint",
                "provider-model", DateTimeOffset.Parse("2026-09-26T00:00:00Z"), parametersSource.RootElement,
                null, [], new RevisionDocument(1, "schema", schemaSource.RootElement),
                new RevisionDocument(1, "prompt", promptSource.RootElement), null);
        }

        Assert.Equal(64, RevisionHasher.Hash(revision).Length);
        Assert.Empty(ProfileValidator.Validate(profile));
    }

    private static RevisionDocument Document(string payload) => new(1, "configuration", Json(payload));
    private static ConfigurationProfile FullProfile(string model = "jev-1.13.0", double threshold = 0.4,
        string criterion = "Needs help", string? summaryParameters = null, string? summaryPrompt = null) => new(
        1, "typed-with-summary", "jev", model, "jev-endpoint", model,
        DateTimeOffset.Parse("2026-09-26T00:00:00Z"), Json("{}"), threshold,
        [new ChoiceQuestionProfile("intent", "Choose intent.", new Dictionary<string, string>
            { ["support"] = criterion, ["social"] = "Casual conversation" })],
        Doc("schema", """{"contractVersion":1,"type":"object"}"""),
        Doc("prompt", """{"text":"classify"}"""),
        Summary(summaryParameters, summaryPrompt));

    private static SummaryModelProfile Summary(string? parameters = null, string? prompt = null, JsonElement? summaryParameters = null) => new(
        1, "openai", "gpt-summary", "gpt-summary-pinned", "openai-endpoint",
        DateTimeOffset.Parse("2026-09-26T00:00:00Z"), summaryParameters ?? Json(parameters ?? """{"reasoningEffort":"medium","maxOutputTokens":2048}"""),
        Doc("schema", """{"contractVersion":1,"type":"object"}"""), Doc("prompt", prompt ?? """{"text":"summarize"}"""));

    private static ScopedRevisionRef Ref(EvaluationScope scope) => new(scope, new RevisionRef(Guid.NewGuid(), new string('a', 64)));
    private static RevisionDocument Doc(string kind, string json) => new(1, kind, Json(json));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
