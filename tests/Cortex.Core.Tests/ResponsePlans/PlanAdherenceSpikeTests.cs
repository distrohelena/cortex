using System.Text.Json;
using Xunit;

namespace Cortex.Core.Tests.ResponsePlans;

public sealed class PlanAdherenceSpikeTests
{
    [Fact]
    public void All_no_is_adherent()
        => Assert.Equal("adherent", PlanAdherenceSpike.Classify("""
            {"introduziu_semantica_nova":"nao","contradisse_plano":"nao","omitiu_conteudo_essencial":"nao","mudou_target":"nao"}
            """));

    [Theory]
    [InlineData("introduziu_semantica_nova")]
    [InlineData("contradisse_plano")]
    [InlineData("omitiu_conteudo_essencial")]
    [InlineData("mudou_target")]
    public void Each_deviation_prevents_adherence(string field)
    {
        var values = new Dictionary<string, string> { ["introduziu_semantica_nova"] = "nao", ["contradisse_plano"] = "nao", ["omitiu_conteudo_essencial"] = "nao", ["mudou_target"] = "nao" };
        values[field] = "sim";
        Assert.Equal("divergent", PlanAdherenceSpike.Classify(JsonSerializer.Serialize(values)));
        values[field] = "ambiguo";
        Assert.Equal("ambiguous", PlanAdherenceSpike.Classify(JsonSerializer.Serialize(values)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("invalid")]
    [InlineData("{\"introduziu_semantica_nova\":true}")]
    public void Invalid_is_not_adherent(string raw) => Assert.Equal("invalid_output", PlanAdherenceSpike.Classify(raw));
}
