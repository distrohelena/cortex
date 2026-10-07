using Xunit;
namespace Cortex.Personas.Tests;

public sealed class PersonaRevisionCodecTests
{
    private readonly PersonaRevisionCodec codec = new();
    [Fact]
    public void RoundTripPreservesObservationInterpretationAndUnknownConfidence()
    {
        var encoded = codec.Encode(PersonaFixtures.Bundle());
        var decoded = codec.Decode(encoded.Bytes, encoded.ArtifactSha256);
        var evidence = decoded.Bundle.Evidence[0];
        Assert.Equal("Questionou a forma de execução e ofereceu apoio depois.", evidence.ObservedBehavior);
        Assert.Equal("Pode valorizar autonomia sem abandonar o apoio.", evidence.Interpretation.Text);
        Assert.NotEqual(evidence.ObservedBehavior, evidence.Interpretation.Text);
        Assert.Null(evidence.Confidence.Transcription);
        Assert.Null(evidence.Confidence.Attribution);
        Assert.Null(evidence.Confidence.Interpretation);
        Assert.Null(evidence.OccurredAt);
        Assert.Equal(encoded.ArtifactSha256, decoded.ArtifactSha256);
    }
    [Theory]
    [InlineData("project")]
    [InlineData("world")]
    [InlineData("character")]
    public void ForeignScopeOrMissingEvidenceIsRejected(string dimension)
    {
        var bundle = PersonaFixtures.Bundle();
        var foreign = dimension switch
        {
            "project" => PersonaFixtures.Scope with { ProjectId = "project-b" },
            "world" => PersonaFixtures.Scope with { WorldId = "other-world" },
            _ => PersonaFixtures.Scope with { CharacterId = "other-character" }
        };
        Assert.Throws<InvalidDataException>(() => codec.Encode(bundle with
        { Evidence = [bundle.Evidence[0] with { Scope = foreign }, bundle.Evidence[1]] }));
    }
    [Theory]
    [InlineData("duplicate_source")]
    [InlineData("duplicate_evidence")]
    [InlineData("missing_source")]
    [InlineData("missing_support")]
    [InlineData("missing_counter")]
    [InlineData("missing_pattern")]
    public void DanglingOrDuplicateIdsAreRejected(string defect)
    {
        var b = PersonaFixtures.Bundle();
        var changed = defect switch
        {
            "duplicate_source" => b with { Sources = [b.Sources[0], b.Sources[0]] },
            "duplicate_evidence" => b with { Evidence = [b.Evidence[0], b.Evidence[0]] },
            "missing_source" => b with { Sources = [] },
            "missing_support" => b with { Patterns = [b.Patterns[0] with { SupportingEvidenceIds = ["absent"] }] },
            "missing_counter" => b with { Patterns = [b.Patterns[0] with { CounterEvidenceIds = ["absent"] }] },
            _ => b with { Spec = b.Spec with { SelectedPatternIds = ["absent"] } }
        };
        Assert.Throws<InvalidDataException>(() => codec.Encode(changed));
    }
    [Fact]
    public void WrongSourceHashIsRejected()
    {
        var b = PersonaFixtures.Bundle();
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with
        { Sources = [b.Sources[0] with { Text = "Changed source." }, b.Sources[1]] }));
    }
    [Theory]
    [InlineData(1, 3, TextOffsetUnit.Utf16)]
    [InlineData(1, 2, TextOffsetUnit.UnicodeScalar)]
    public void ExplicitOffsetUnitPreservesEmoji(int start, int end, TextOffsetUnit unit)
    {
        Assert.Equal("😀", new TextLocation(start, end, unit).Extract("A😀B"));
    }
    [Theory]
    [InlineData(1, 2, TextOffsetUnit.Utf16)]
    [InlineData(-1, 1, TextOffsetUnit.Utf16)]
    [InlineData(0, 8, TextOffsetUnit.UnicodeScalar)]
    [InlineData(3, 2, TextOffsetUnit.Utf16)]
    [InlineData(0, 1, (TextOffsetUnit)99)]
    public void InvalidTextLocationIsRejected(int start, int end, TextOffsetUnit unit)
    {
        Assert.Throws<InvalidDataException>(() => new TextLocation(start, end, unit).Extract("A😀B"));
    }
    [Fact]
    public void SameOriginRemainsOneOrigin()
    {
        var b = PersonaFixtures.Bundle();
        var encoded = codec.Encode(b with
        { Sources = [.. b.Sources, b.Sources[0] with { SourceId = "copy-1" }, b.Sources[0] with { SourceId = "copy-2" }] });
        var copies = codec.Decode(encoded.Bytes, encoded.ArtifactSha256).Bundle.Sources
            .Where(s => s.OriginKey == "episode-a").ToArray();
        Assert.Equal(3, copies.Length);
        Assert.Single(copies.Select(s => s.OriginKey).Distinct());
        Assert.Equal(3, copies.Select(s => s.SourceId).Distinct().Count());
    }
    [Fact]
    public void EncodeCopiesMutableCollections()
    {
        var b = PersonaFixtures.Bundle();
        var sources = b.Sources.ToList();
        var encoded = codec.Encode(b with { Sources = sources });
        sources.Clear();
        Assert.Equal(2, encoded.Bundle.Sources.Count);
        if (encoded.Bundle.Sources is IList<PersonaSource> exposed) exposed.Clear();
        Assert.Equal(2, encoded.Bundle.Sources.Count);
        var bytes = encoded.Bytes.ToArray();
        bytes[0] = 0;
        Assert.Equal((byte)'{', encoded.Bytes.Span[0]);
    }
}
