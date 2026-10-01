using System.Text.Json.Nodes;
using System.Text.Json;
using Xunit;

namespace Cortex.Core.Tests.Assertions;

public sealed class AssertionExtractionTests
{
    [Fact]
    public void Model_inputs_exclude_ground_truth_and_non_input_metadata()
    {
        using var corpus = JsonDocument.Parse("""
            {"contexts":{"personal":"Contexto sintético."},"subjects":[{"id":"person_0","description":"Interlocutora"}],"sourceId":"private-metadata","boundary":"not-for-model"}
            """);
        using var item = JsonDocument.Parse("""
            {"candidate":"😀 Olá","context":"personal","expectedHuman":"secret-gold-label","critical":true}
            """);
        var inputs = AssertionExtractionLive.Inputs(corpus.RootElement, item.RootElement);
        Assert.Equal(new[] { "candidate", "candidate_length_utf16", "recent_context", "subjects" }, inputs.EnumerateObject().Select(p => p.Name));
        Assert.Equal(6, inputs.GetProperty("candidate_length_utf16").GetInt32());
        Assert.DoesNotContain("secret-gold-label", inputs.GetRawText());
        Assert.DoesNotContain("private-metadata", inputs.GetRawText());
        Assert.DoesNotContain("not-for-model", inputs.GetRawText());
    }

    [Fact]
    public void Nested_schema_exposes_only_provided_subjects_and_closed_special_values()
    {
        var schema = AssertionExtraction.Schema(["person_7"]);
        var item = schema.GetProperty("properties").GetProperty("assertions").GetProperty("items");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(6, item.GetProperty("required").GetArrayLength());
        Assert.Equal(new[] { "person_7", "generic", "unknown", "ambiguous" },
            item.GetProperty("properties").GetProperty("subject").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    private static JsonObject Output() => JsonNode.Parse("""
        {"assertions":[{"evidence_span":{"start":3,"end":6,"text":"Olá"},"subject":"person_0","proposition":"person_0 cumprimenta","expression_mode":"explicit","commitment":"asserted","grounding":"unsupported_inference"}]}
        """)!.AsObject();

    [Fact]
    public void Accepts_exact_utf16_offsets_after_surrogate_pair()
        => Assert.Null(AssertionExtraction.Validate(Output().ToJsonString(), "😀 Olá", ["person_0"]));

    [Fact]
    public void Empty_assertions_are_valid()
        => Assert.Null(AssertionExtraction.Validate("{\"assertions\":[]}", "Vamos resolver.", ["person_0"]));

    [Theory]
    [InlineData("start", -1)]
    [InlineData("start", 2)]
    [InlineData("end", 7)]
    [InlineData("end", 3)]
    public void Rejects_invalid_or_inexact_offsets(string field, int value)
    {
        var data = Output(); data["assertions"]![0]!["evidence_span"]![field] = value;
        Assert.Equal("invalid_span", AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
    }

    [Fact]
    public void Rejects_paraphrased_evidence()
    {
        var data = Output(); data["assertions"]![0]!["evidence_span"]!["text"] = "Oi";
        Assert.Equal("invalid_span", AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
    }

    [Theory]
    [InlineData("subject", "invented_person")]
    [InlineData("expression_mode", "guess")]
    [InlineData("commitment", "fact")]
    [InlineData("grounding", "candidate_said_so")]
    [InlineData("proposition", " ")]
    public void Rejects_invalid_values(string field, string value)
    {
        var data = Output(); data["assertions"]![0]![field] = value;
        Assert.NotNull(AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
    }

    [Theory]
    [InlineData("{\"assertions\":null}")]
    [InlineData("{\"assertions\":[],\"rationale\":\"x\"}")]
    [InlineData("{\"assertions\":[],\"assertions\":[]}")]
    [InlineData("not json")]
    public void Rejects_non_strict_shape(string raw)
        => Assert.NotNull(AssertionExtraction.Validate(raw, "test", ["person_0"]));

    [Fact]
    public void Rejects_missing_extra_and_overlong_fields()
    {
        var data = Output(); data["assertions"]![0]!.AsObject().Remove("commitment");
        Assert.NotNull(AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
        data = Output(); data["assertions"]![0]!["confidence"] = 1;
        Assert.NotNull(AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
        data = Output(); data["assertions"]![0]!["proposition"] = new string('x', 401);
        Assert.NotNull(AssertionExtraction.Validate(data.ToJsonString(), "😀 Olá", ["person_0"]));
    }
}
