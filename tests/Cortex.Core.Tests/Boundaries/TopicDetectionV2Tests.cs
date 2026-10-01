using System.Text.Json;
using Cortex.Core.Boundaries;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class TopicDetectionV2Tests
{
    public static Dictionary<string, string> Features() => new()
    {
        ["relevante_ao_topico"] = "sim",
        ["expoe_limite"] = "nao",
        ["provoca_ou_ridiculariza_sujeito"] = "nao",
        ["julga_negativamente_competencia"] = "nao",
        ["julga_negativamente_esforco"] = "nao",
        ["julga_negativamente_tempo_gasto"] = "nao",
        ["minimiza_dificuldade"] = "nao",
        ["critica_forma_de_lidar"] = "nao",
        ["minimiza_reacao_emocional"] = "nao"
    };
    private static BoundaryPolicyDecision Evaluate(Dictionary<string, string> fields) =>
        new NoTeasingTopicDetectionV2().Evaluate(BoundaryPolicyEvaluatorTests.Boundary(), JsonSerializer.SerializeToElement(fields), BoundaryPolicyEvaluatorTests.Now);

    [Fact]
    public void Each_predicate_blocks_independently_and_all_ambiguities_fail_closed()
    {
        Assert.True(Evaluate(Features()).Allow);
        foreach (var key in Features().Keys)
        {
            var fields = Features(); fields[key] = "ambiguo";
            Assert.False(Evaluate(fields).Allow);
            if (key is "relevante_ao_topico" or "expoe_limite") continue;
            fields[key] = "sim";
            Assert.Equal("protected_topic_personal_depreciation", Evaluate(fields).DiagnosticCode);
            fields["relevante_ao_topico"] = "nao";
            Assert.Equal("invalid_semantic_features", Evaluate(fields).DiagnosticCode);
        }
    }

    [Fact]
    public void Disclosure_outside_scope_is_still_blocked_and_unrelated_safe_content_allows()
    {
        var fields = Features(); fields["relevante_ao_topico"] = "nao";
        Assert.Equal("outside_boundary_scope", Evaluate(fields).DiagnosticCode);
        fields["expoe_limite"] = "sim";
        Assert.Equal("boundary_disclosure", Evaluate(fields).DiagnosticCode);
    }

    [Fact]
    public void Multiple_features_produce_deterministic_natural_feedback()
    {
        var fields = Features(); fields["julga_negativamente_tempo_gasto"] = "sim"; fields["minimiza_dificuldade"] = "sim";
        var template = new NoTeasingTopicDetectionV2();
        var value = template.Parse(JsonSerializer.Serialize(fields));
        var decision = Evaluate(fields);
        Assert.False(decision.Allow);
        var feedback = template.BuildRegenerationFeedback(value, decision);
        Assert.Contains("tempo gasto", feedback);
        Assert.Contains("dificuldade", feedback);
        Assert.DoesNotContain("julga_negativamente", feedback);
    }

    [Fact]
    public void Missing_extra_duplicate_wrong_type_and_unknown_value_never_allow()
    {
        var template = new NoTeasingTopicDetectionV2();
        foreach (var field in Features().Keys)
        {
            var missing = Features(); missing.Remove(field);
            Assert.Throws<InvalidDataException>(() => template.Parse(JsonSerializer.Serialize(missing)));
        }
        var extra = Features(); extra["resultado"] = "permitir";
        Assert.Throws<InvalidDataException>(() => template.Parse(JsonSerializer.Serialize(extra)));
        var wrong = Features(); wrong["expoe_limite"] = "false";
        Assert.Throws<InvalidDataException>(() => template.Parse(JsonSerializer.Serialize(wrong)));
        Assert.Throws<InvalidDataException>(() => template.Parse("{\"expoe_limite\":false}"));
        var duplicate = JsonSerializer.Serialize(Features()).Replace("{", "{\"expoe_limite\":\"sim\",");
        Assert.Throws<InvalidDataException>(() => template.Parse(duplicate));
        Assert.Equal("invalid_boundary", template.Evaluate(null, JsonSerializer.SerializeToElement(Features()), DateTimeOffset.UtcNow).DiagnosticCode);
        Assert.False(template.Evaluate(BoundaryPolicyEvaluatorTests.Boundary(), default, DateTimeOffset.UtcNow).Allow);
    }

    [Fact]
    public void Versions_are_exact_and_legacy_schema_is_not_upgraded()
    {
        var legacy = BoundaryDetectionTemplates.Resolve("no_teasing_about_topic:v1", "boundary-semantic-features:v1", "no_teasing_about_topic:evaluator-v1");
        var current = BoundaryDetectionTemplates.Resolve("no_teasing_about_topic:v1", "no_teasing_about_topic:semantic-v2", "no_teasing_about_topic:evaluator-v2");
        Assert.True(legacy.SemanticSchema.ToJsonSchema().GetProperty("properties").TryGetProperty("ato", out _));
        Assert.False(current.SemanticSchema.ToJsonSchema().GetProperty("properties").TryGetProperty("ato", out _));
        Assert.Throws<InvalidDataException>(() => BoundaryDetectionTemplates.Resolve("no_teasing_about_topic:v1", "latest", "latest"));
    }
}
