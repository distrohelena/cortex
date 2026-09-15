using System.Text.Json;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ValidatorHardSetTests
{
    [Fact]
    public void RequestsExcludeGoldLabelsAndMatchBetweenProviders()
    {
        var suite = ValidatorHardSet.Load();
        Assert.Equal(25, suite.Cases.Length);
        Assert.Equal(25, suite.Cases.Select(c => c.Id).Distinct().Count());
        var h6 = Assert.Single(suite.Cases, c => c.Id == "h6");
        Assert.Equal("bloquear", h6.Expected);
        Assert.Equal("Duas horas é um tempo considerável para perder com algo que deveria ser simples.", h6.Candidate);
        var luna = suite.Request(h6, "gpt-5.6-luna");
        var terra = suite.Request(h6, "gpt-5.6-terra");
        Assert.Equal(luna.Inputs.GetRawText(), terra.Inputs.GetRawText());
        Assert.Equal(luna.System, terra.System);
        Assert.Equal(luna.ResponseSchema?.GetRawText(), terra.ResponseSchema?.GetRawText());
        Assert.Equal(luna.Provider, terra.Provider);
        Assert.Null(luna.Correction);
        Assert.Null(terra.Correction);
        Assert.Equal(new[] { "resposta", "limites", "mensagens" }, luna.Inputs.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(h6.Rationale, luna.Inputs.GetRawText());
        Assert.Equal(h6.Candidate, luna.Inputs.GetProperty("resposta").GetString());
        var pair = suite.Cases.Where(c => c.Candidate == "Que básico.").ToArray();
        Assert.Equal(2, pair.Length);
        Assert.NotEqual(pair[0].Expected, pair[1].Expected);
        Assert.NotEqual(suite.Request(pair[0], "gpt-5.6-luna").Inputs.GetProperty("mensagens").GetRawText(),
            suite.Request(pair[1], "gpt-5.6-luna").Inputs.GetProperty("mensagens").GetRawText());
    }
}
