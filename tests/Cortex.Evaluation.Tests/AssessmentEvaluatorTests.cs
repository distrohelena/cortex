using System.Text.Json;
using Cortex.Evaluation.Comparison;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class AssessmentEvaluatorTests
{
    [Fact]
    public void Equivalent_allowed_classifications_both_pass()
    {
        var rule = Rule("involvement", "envolvimento", true, "\"nenhum\"", "\"indireto\"");
        foreach (var output in new[] { "{\"envolvimento\":\"nenhum\"}", "{\"envolvimento\":\"indireto\"}" })
        {
            var result = AssessmentEvaluator.Evaluate(output, [rule], []);
            Assert.Equal("pass", result.Outcome);
            Assert.False(Assert.Single(result.Criteria).CriticalFailure);
        }
    }

    [Fact]
    public void Consistent_wrong_answer_fails_instead_of_counting_agreement()
    {
        var result = AssessmentEvaluator.Evaluate("{\"acao\":\"responder\"}", [Rule("action", "acao", true, "\"calar\"")], []);
        Assert.Equal("fail", result.Outcome);
        Assert.True(Assert.Single(result.Criteria).CriticalFailure);
        Assert.Equal("unexpected_value", result.Criteria[0].Diagnostic);
    }

    [Fact]
    public void Boolean_is_not_coerced_from_a_string()
    {
        var result = AssessmentEvaluator.Evaluate("{\"continuar_acompanhando\":\"false\"}",
            [Rule("following", "continuar_acompanhando", false, "false")], []);
        Assert.Equal("fail", result.Outcome);
        Assert.False(Assert.Single(result.Criteria).CriticalFailure);
    }

    [Fact]
    public void Missing_field_is_failure_not_an_acceptable_silence()
    {
        var result = AssessmentEvaluator.Evaluate("{}", [Rule("action", "acao", true, "\"calar\"")], []);
        Assert.Equal("fail", result.Outcome);
        Assert.Equal("missing_field", Assert.Single(result.Criteria).Diagnostic);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"acao\":\"responder\",\"acao\":\"calar\"}")]
    [InlineData("{\"acao\":\"calar\",\"extra\":{\"x\":1,\"x\":2}}")]
    public void Invalid_or_ambiguous_output_fails_contract(string output)
    {
        var result = AssessmentEvaluator.Evaluate(output, [Rule("action", "acao", false, "\"calar\"")], []);
        Assert.Equal("fail", result.Outcome);
        Assert.Equal("invalid_output", Assert.Single(result.Criteria).Diagnostic);
    }

    [Fact]
    public void Voice_and_summary_without_review_remain_unscored()
    {
        var result = AssessmentEvaluator.Evaluate("{\"acao\":\"calar\",\"resumo\":\"Qualquer resumo\"}",
            [Rule("action", "acao", true, "\"calar\"")], ["voice", "summary_fidelity"]);
        Assert.Equal("unscored", result.Outcome);
        Assert.Equal(new[] { "pass", "unscored", "unscored" }, result.Criteria.Select(c => c.Outcome));
    }

    [Fact]
    public void No_criteria_cannot_certify_a_response() =>
        Assert.Equal("unscored", AssessmentEvaluator.Evaluate("{}", [], []).Outcome);

    [Fact]
    public void Plain_text_keeps_all_manual_review_requirements()
    {
        var result = AssessmentEvaluator.Evaluate("Oi, tudo bem?", [], ["voice", "context"]);
        Assert.Equal("unscored", result.Outcome);
        Assert.Equal(new[] { "voice", "context" }, result.Criteria.Select(c => c.Criterion));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("42")]
    public void Unsupported_grading_operator_is_rejected_not_approximated(string accepted) =>
        Assert.Throws<InvalidDataException>(() => AssessmentEvaluator.Evaluate("{}", [Rule("criterion", "field", false, accepted)], []));

    [Fact]
    public void Duplicate_criterion_id_is_rejected() => Assert.Throws<InvalidDataException>(() =>
        AssessmentEvaluator.Evaluate("{}", [Rule("same", "field", false, "true")], ["same"]));

    private static FieldCriterion Rule(string id, string field, bool critical, params string[] accepted) =>
        new(id, field, accepted.Select(value => JsonDocument.Parse(value).RootElement.Clone()).ToArray(), critical);
}
