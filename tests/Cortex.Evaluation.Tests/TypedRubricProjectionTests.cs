using System.Text.Json;
using Cortex.Evaluation.Comparison;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class TypedRubricProjectionTests
{
    private static JsonElement Dimensions => Json("""
        {"involvement":{"valueType":"enum","currentField":"envolvimento","values":["nenhum","indireto","direto"]},
         "action":{"valueType":"enum","currentField":"acao","values":["calar","responder"]},
         "following":{"valueType":"boolean","currentField":"continuar_acompanhando"}}
        """);

    [Fact]
    public void Situation_does_not_claim_to_evaluate_a_downstream_action()
    {
        var result = TypedRubricProjection.Project(Dimensions,
            Json("""{"involvement":["nenhum","indireto"],"action":["calar"]}"""), ["envolvimento"], ["involvement"]);
        var criterion = Assert.Single(result.Criteria);
        Assert.Equal("envolvimento", criterion.Field);
        Assert.True(criterion.Critical);
        Assert.Equal(new[] { "action" }, result.NotEvaluatedDimensions);
        Assert.Equal("pass", AssessmentEvaluator.Evaluate("""{"envolvimento":"indireto"}""", result.Criteria, []).Outcome);
    }

    [Fact]
    public void Boolean_expectation_preserves_type()
    {
        var result = TypedRubricProjection.Project(Dimensions, Json("""{"following":[false]}"""), ["continuar_acompanhando"], []);
        Assert.Equal(JsonValueKind.False, Assert.Single(Assert.Single(result.Criteria).AcceptedValues).ValueKind);
    }

    [Theory]
    [InlineData("{\"invented\":[true]}")]
    [InlineData("{\"involvement\":[\"invented\"]}")]
    [InlineData("{\"following\":[\"false\"]}")]
    [InlineData("{\"action\":[]}")]
    public void Invalid_expectations_are_not_silently_ignored(string expected) =>
        Assert.Throws<InvalidDataException>(() => TypedRubricProjection.Project(Dimensions, Json(expected), ["envolvimento"], []));

    [Fact]
    public void Whole_development_corpus_maps_without_grading_voice_as_typed()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "conversation-cases.json")));
        var dimensions = corpus.RootElement.GetProperty("evaluationSchema").GetProperty("typedSystem1").GetProperty("eligibleDimensions");
        var typedCases = 0;
        var voiceCases = 0;
        foreach (var entry in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected = entry.TryGetProperty("typedSystem1Expectations", out var value) ? value : Json("{}");
            var projection = TypedRubricProjection.Project(dimensions, expected, ["envolvimento", "ambiente"], []);
            if (entry.GetProperty("targetKind").GetString() == "full_completion_only")
            {
                Assert.Empty(projection.Criteria);
                Assert.Equal("unscored", AssessmentEvaluator.Evaluate("{}", projection.Criteria, []).Outcome);
                voiceCases++;
            }
            else
            {
                Assert.NotEmpty(projection.Criteria);
                Assert.Contains("participationAction", projection.NotEvaluatedDimensions);
                Assert.DoesNotContain(projection.Criteria, c => c.Field == "acao");
                typedCases++;
            }
        }
        Assert.Equal(20, typedCases);
        Assert.Equal(5, voiceCases);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
