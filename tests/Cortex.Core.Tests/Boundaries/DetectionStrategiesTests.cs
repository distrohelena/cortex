using Xunit;

namespace Cortex.Core.Tests.Boundaries;

public sealed class DetectionStrategiesTests
{
    [Fact]
    public void System_time_golden_retains_short_segment_and_external_target_with_exact_offsets()
    {
        const string candidate = "Duas horas? Esse sistema sabe desperdiçar o tempo dos outros.";
        var segments = DetectionStrategies.Split(candidate);
        Assert.Equal(new[]
        {
            new DetectionStrategies.Segment(0, 11, "Duas horas?"),
            new DetectionStrategies.Segment(12, 49, "Esse sistema sabe desperdiçar o tempo dos outros.")
        }, segments);
        foreach (var segment in segments) Assert.Equal(segment.Text, candidate.Substring(segment.Start, segment.Length));
    }

    [Fact]
    public void Segments_are_exact_original_spans_without_rewriting_or_lost_words()
    {
        const string candidate = "  É um procedimento de rotina, não uma crise. Vamos focar nisso.  ";
        var segments = DetectionStrategies.Split(candidate);
        Assert.Equal(2, segments.Count);
        Assert.Equal("É um procedimento de rotina, não uma crise.", segments[0].Text);
        Assert.Equal("Vamos focar nisso.", segments[1].Text);
        foreach (var s in segments) Assert.Equal(s.Text, candidate.Substring(s.Start, s.Length));
        Assert.Equal(candidate.Where(c => !char.IsWhiteSpace(c)), string.Concat(segments.Select(s => s.Text)).Where(c => !char.IsWhiteSpace(c)));
        Assert.Single(DetectionStrategies.Split("Seja proativa e transparente, sem drama."));
        Assert.Single(DetectionStrategies.Split("A versão é 1.23."));
        Assert.Empty(DetectionStrategies.Split("  "));
        const string unicode = "🙂 Primeira!\r\nSegunda sem ponto";
        var unicodeSegments = DetectionStrategies.Split(unicode);
        Assert.Equal(2, unicodeSegments.Count);
        foreach (var s in unicodeSegments) Assert.Equal(s.Text, unicode.Substring(s.Start, s.Length));
    }

    [Fact]
    public void Complement_is_strict_and_any_yes_or_ambiguity_blocks()
    {
        Assert.True(DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":"nao","minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao"}"""));
        foreach (var field in new[] { "deprecia_experiencia_protegida", "minimiza_reacao_ou_dificuldade", "expoe_limite" })
            foreach (var value in new[] { "sim", "ambiguo" })
            {
                var fields = new Dictionary<string, string> { ["deprecia_experiencia_protegida"] = "nao", ["minimiza_reacao_ou_dificuldade"] = "nao", ["expoe_limite"] = "nao" };
                fields[field] = value;
                Assert.False(DetectionStrategies.ComplementAllows(System.Text.Json.JsonSerializer.Serialize(fields)));
            }
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("{}"));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("not JSON"));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":false,"minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao"}"""));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":"talvez","minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao"}"""));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":null,"minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao"}"""));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":"nao","deprecia_experiencia_protegida":"nao","minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao"}"""));
        Assert.ThrowsAny<Exception>(() => DetectionStrategies.ComplementAllows("""{"deprecia_experiencia_protegida":"nao","minimiza_reacao_ou_dificuldade":"nao","expoe_limite":"nao","extra":"nao"}"""));
    }
}
