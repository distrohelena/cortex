using Filomena.Core.Boundaries;
using Filomena.Core.Memory;
using Xunit;

namespace Filomena.Core.Tests.Boundaries;

public sealed class BoundaryPolicyEvaluatorTests
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
    internal static Boundary Boundary()
    {
        const string ns = "whatsapp-dm-5511944558218-s-whatsapp-net";
        const string text = "Não brinque sobre trabalho.";
        return new(new MemoryItem("source", ns, text, MemoryCategory.Boundary,
            [MemoryIdentity.Person(ns, "whatsapp:5511944558218@s.whatsapp.net", "Helena")], "exact"), "hash", BoundaryAdmission.Analyze(text));
    }

    [Theory]
    [InlineData("presente", "experiencia_do_sujeito", "minimizacao", "reacao_emocional", false, false, "protected_personal_depreciation")]
    [InlineData("presente", "experiencia_do_sujeito", "cuidado", "tempo_gasto", false, true, "no_prohibited_act_identified")]
    [InlineData("presente", "problema_ou_sistema", "discordancia_tecnica", "nenhuma", false, true, "no_prohibited_act_identified")]
    [InlineData("presente", "sujeito", "provocacao", "nenhuma", false, false, "protected_personal_depreciation")]
    [InlineData("nenhuma", "outro", "neutro", "nenhuma", false, true, "outside_boundary_scope")]
    [InlineData("ambigua", "outro", "neutro", "nenhuma", false, false, "ambiguous_relevance")]
    [InlineData("presente", "ambiguo", "neutro", "nenhuma", false, false, "ambiguous_semantics")]
    [InlineData("presente", "sujeito", "neutro", "nenhuma", true, false, "boundary_disclosure")]
    [InlineData("presente", "experiencia_do_sujeito", "minimizacao", "nenhuma", false, false, "invalid_semantic_features")]
    [InlineData("presente", "problema_ou_sistema", "minimizacao", "reacao_emocional", false, false, "invalid_semantic_features")]
    [InlineData("nenhuma", "sujeito", "neutro", "tempo_gasto", false, false, "invalid_semantic_features")]
    public void Literal_policy_cases(string relevance, string target, string act, string dimension, bool disclosure, bool allow, string diagnostic)
    {
        var result = BoundaryPolicyEvaluator.Evaluate(Boundary(), new(relevance, target, act, dimension, disclosure), Now);
        Assert.Equal(allow, result.Allow);
        Assert.Equal(diagnostic, result.DiagnosticCode);
    }

    [Fact]
    public void Exhaustive_enum_space_never_allows_disclosure_ambiguity_or_protected_depreciation()
    {
        var count = 0;
        var boundary = Boundary();
        foreach (var relevance in new[] { "nenhuma", "presente", "ambigua" })
            foreach (var target in new[] { "sujeito", "experiencia_do_sujeito", "problema_ou_sistema", "outra_pessoa", "outro", "ambiguo" })
                foreach (var act in new[] { "neutro", "conselho", "informacao_factual", "discordancia_tecnica", "cuidado", "provocacao", "ridicularizacao", "desqualificacao", "minimizacao", "ambiguo" })
                    foreach (var dimension in new[] { "nenhuma", "competencia", "esforco", "tempo_gasto", "dificuldade", "forma_de_lidar", "reacao_emocional", "experiencia_geral", "ambigua" })
                        foreach (var disclosure in new[] { false, true })
                        {
                            var result = BoundaryPolicyEvaluator.Evaluate(boundary, new(relevance, target, act, dimension, disclosure), Now);
                            count++;
                            if (disclosure || relevance == "ambigua" || relevance == "presente" &&
                                (target == "ambiguo" || act == "ambiguo" || dimension == "ambigua" ||
                                 target is "sujeito" or "experiencia_do_sujeito" && act is "provocacao" or "ridicularizacao" or "desqualificacao" or "minimizacao"))
                                Assert.False(result.Allow, $"Unsafe allow: {relevance}/{target}/{act}/{dimension}/{disclosure}");
                            Assert.Equal(result, BoundaryPolicyEvaluator.Evaluate(boundary, new(relevance, target, act, dimension, disclosure), Now));
                        }
        Assert.Equal(3240, count);
    }

    [Fact]
    public void Missing_or_invalid_features_and_boundary_fail_closed()
    {
        Assert.Equal("invalid_boundary", BoundaryPolicyEvaluator.Evaluate(null, null, Now).DiagnosticCode);
        Assert.Equal("invalid_semantic_features", BoundaryPolicyEvaluator.Evaluate(Boundary(), null, Now).DiagnosticCode);
        Assert.False(BoundaryPolicyEvaluator.Evaluate(Boundary(), new("inventada", "outro", "neutro", "nenhuma", false), Now).Allow);
    }
}
