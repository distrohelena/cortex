using System.Text.Json;
using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class PersonalAttributionSpikeTests
{
    [Fact]
    public void Negative_personal_attribution_blocks_without_requiring_invention()
    {
        var fields = PersonalAttributionSpike.Schema("A").Fields.Keys.ToDictionary(k => k, _ => "nao");
        fields["atribui_estado_ou_traco_ao_sujeito"] = "sim";
        fields["atribuicao_e_negativa_ou_desqualificante"] = "sim";
        fields["relacionada_ao_episodio_protegido"] = "sim";
        Assert.Equal("Block", PersonalAttributionSpike.Evaluate("A", JsonSerializer.SerializeToElement(fields)));
        fields["atribuicao_e_negativa_ou_desqualificante"] = "nao";
        Assert.Equal("Allow", PersonalAttributionSpike.Evaluate("A", JsonSerializer.SerializeToElement(fields)));
        fields["atribuicao_e_negativa_ou_desqualificante"] = "ambiguo";
        Assert.Equal("Block", PersonalAttributionSpike.Evaluate("A", JsonSerializer.SerializeToElement(fields)));
    }

    [Fact]
    public void Verifier_mapping_does_not_treat_ambiguity_as_allow()
    {
        Assert.Equal("Block", PersonalAttributionSpike.Evaluate("C", JsonSerializer.SerializeToElement(new { violacao_encontrada = "sim", tipo = "atribuicao_negativa" })));
        Assert.Equal("Allow", PersonalAttributionSpike.Evaluate("C", JsonSerializer.SerializeToElement(new { violacao_encontrada = "nao", tipo = "nenhuma" })));
        Assert.Equal("Block", PersonalAttributionSpike.Evaluate("C", JsonSerializer.SerializeToElement(new { violacao_encontrada = "ambiguo", tipo = "ambiguo" })));
        Assert.Throws<InvalidDataException>(() => PersonalAttributionSpike.Evaluate("C", JsonSerializer.SerializeToElement(new { violacao_encontrada = "nao", tipo = "minimizacao" })));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"violacao_encontrada\":\"nao\",\"tipo\":\"nenhuma\",\"extra\":\"sim\"}")]
    [InlineData("{\"violacao_encontrada\":\"nao\",\"tipo\":\"nenhuma\",\"tipo\":\"nenhuma\"}")]
    [InlineData("{\"violacao_encontrada\":false,\"tipo\":\"nenhuma\"}")]
    public void Invalid_features_never_become_approval(string raw)
    {
        using var json = JsonDocument.Parse(raw);
        Assert.ThrowsAny<Exception>(() => PersonalAttributionSpike.Evaluate("C", json.RootElement));
    }
}
