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
            profile = new ConfigurationProfile(1, "completion", "luna", "model", "endpoint",
                "provider-model", DateTimeOffset.Parse("2026-09-26T00:00:00Z"), parametersSource.RootElement,
                null, [], new RevisionDocument(1, "schema", schemaSource.RootElement),
                new RevisionDocument(1, "prompt", promptSource.RootElement), null);
        }

        Assert.Equal(64, RevisionHasher.Hash(revision).Length);
        Assert.Empty(ProfileValidator.Validate(profile));
    }

    private static RevisionDocument Document(string payload) => new(1, "configuration", Json(payload));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
