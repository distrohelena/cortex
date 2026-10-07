using System.Text.Json;
using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaContextCompilerTests
{
    private readonly PersonaContextCompiler compiler = new();
    private static ValidatedPersonaRevision Revision() => new PersonaRevisionCodec().Encode(PersonaFixtures.Bundle());

    [Fact]
    public void SameTopicDifferentRelationshipSelectsDifferentPatterns()
    {
        var revision = Revision();
        var close = compiler.Compile(revision, PersonaFixtures.Situation(), 4);
        var stranger = compiler.Compile(revision, PersonaFixtures.Situation(relationship: "stranger"), 4);
        Assert.Equal(["intimate_provocation"], close.Selection.SelectedPatternIds);
        Assert.DoesNotContain("intimate_provocation", stranger.Selection.SelectedPatternIds);
        Assert.Equal("condition_mismatch", Assert.Single(stranger.Selection.SkippedPatterns).Reason);
        Assert.Empty(compiler.Select(revision, PersonaFixtures.Situation(audience: "public"), 4).SelectedPatternIds);
    }

    [Fact]
    public void UnknownConditionIsNotMatch()
    {
        var result = compiler.Select(Revision(), PersonaFixtures.Situation(relationship: null), 4);
        Assert.Empty(result.SelectedPatternIds);
        Assert.Equal("condition_unknown", Assert.Single(result.SkippedPatterns).Reason);
    }

    [Theory]
    [InlineData("overwhelmed", "exception_matched")]
    [InlineData(null, "exception_uncertain")]
    public void ExceptionOrUncertainExceptionPreventsSelection(string? state, string expectedReason)
    {
        var result = compiler.Select(Revision(), PersonaFixtures.Situation(state: state), 4);
        Assert.Empty(result.SelectedPatternIds);
        Assert.Equal(expectedReason, Assert.Single(result.SkippedPatterns).Reason);
    }

    [Fact]
    public void CounterEvidenceAndNeutralityRemainVisible()
    {
        var revision = Revision();
        var result = compiler.Compile(revision, PersonaFixtures.Situation(), 4);
        var pattern = Assert.Single(result.Payload.GetProperty("behavioralTendencies").EnumerateArray());
        Assert.Equal("tendency", pattern.GetProperty("modality").GetString());
        Assert.Equal("evidence-a", Assert.Single(pattern.GetProperty("supportingEvidenceIds").EnumerateArray()).GetString());
        Assert.Equal("evidence-b", Assert.Single(pattern.GetProperty("counterEvidenceIds").EnumerateArray()).GetString());
        Assert.Equal(["episode-a"], result.Selection.SupportingOriginKeys);
        Assert.Equal(["episode-b"], result.Selection.CounterOriginKeys);
        var neutral = compiler.Compile(revision, PersonaFixtures.Situation(act: "neutral_update"), 4);
        Assert.Empty(neutral.Selection.SelectedPatternIds);
        Assert.Equal(0, neutral.Payload.GetProperty("behavioralTendencies").GetArrayLength());
        Assert.Equal("Mira, personagem fictícia desta fixture.", neutral.Payload.GetProperty("identity").GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("project")]
    [InlineData("world")]
    [InlineData("character")]
    public void CrossScopeRejectedBeforeProjection(string dimension)
    {
        var scope = dimension switch
        {
            "project" => PersonaFixtures.Scope with { ProjectId = "project-b" },
            "world" => PersonaFixtures.Scope with { WorldId = "world-b" },
            _ => PersonaFixtures.Scope with { CharacterId = "character-b" }
        };
        Assert.Throws<InvalidDataException>(() => compiler.Compile(Revision(), PersonaFixtures.Situation() with { Scope = scope }, 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidHostLimitIsRejected(int maxPatterns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => compiler.Select(Revision(), PersonaFixtures.Situation(), maxPatterns));
    }

    [Fact]
    public void HostLimitIsDeterministicAndOnlySelectedSpecPatternsAreEligible()
    {
        var b = PersonaFixtures.Bundle();
        var extra = b.Patterns[0] with { PatternId = "a_first" };
        var hidden = b.Patterns[0] with { PatternId = "not_in_spec" };
        var revision = new PersonaRevisionCodec().Encode(b with
        {
            Patterns = [.. b.Patterns, extra, hidden],
            Spec = b.Spec with { SelectedPatternIds = ["intimate_provocation", "a_first"] }
        });
        var result = compiler.Select(revision, PersonaFixtures.Situation(), 1);
        Assert.Equal(["a_first"], result.SelectedPatternIds);
        Assert.Contains(result.SkippedPatterns, p => p.PatternId == "intimate_provocation" && p.Reason == "host_limit");
        Assert.DoesNotContain("not_in_spec", result.SelectedPatternIds);
    }

    [Fact]
    public void ProjectionKeepsSourceAsDataAndPassageProvenance()
    {
        var projection = compiler.Compile(Revision(), PersonaFixtures.Situation(), 4);
        var evidence = projection.Payload.GetProperty("evidence").EnumerateArray().First(e => e.GetProperty("evidenceId").GetString() == "evidence-a");
        Assert.Equal("evidence_data", evidence.GetProperty("role").GetString());
        Assert.Equal("episode-a", evidence.GetProperty("originKey").GetString());
        Assert.Equal("represented-mira", evidence.GetProperty("subjectRef").GetString());
        Assert.Contains("Mira: você pode mudar o caminho", evidence.GetProperty("passage").GetString());
        Assert.False(projection.Payload.TryGetProperty("memory", out _));
        Assert.False(projection.Payload.TryGetProperty("provider", out _));
        Assert.False(projection.Payload.TryGetProperty("credentials", out _));
    }

    [Fact]
    public void ProjectionDoesNotChangeWhenSituationInputCollectionsChange()
    {
        var alternatives = new List<string> { "Talvez só queira ser ouvida." };
        var projection = compiler.Compile(Revision(), PersonaFixtures.Situation() with { AlternativeReadings = alternatives }, 4);
        var text = projection.Payload.GetRawText();
        alternatives.Clear();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(text, projection.Payload.GetRawText());
        Assert.Equal(1, doc.RootElement.GetProperty("situation").GetProperty("alternativeReadings").GetArrayLength());
    }

    [Fact]
    public void UnknownLabelDoesNotBecomeInferredRelationship()
    {
        Assert.Empty(compiler.Select(Revision(), PersonaFixtures.Situation(relationship: "not_in_fixture_vocabulary"), 4).SelectedPatternIds);
    }

    [Fact]
    public void KnownSituationValueRequiresOrigin()
    {
        var situation = PersonaFixtures.Situation() with { RelationshipKind = new("close", "") };
        Assert.Throws<InvalidDataException>(() => compiler.Select(Revision(), situation, 4));
    }

    [Fact]
    public void ReproducedSourcesDoNotMultiplyIndependentSupport()
    {
        var b = PersonaFixtures.Bundle();
        var source1 = b.Sources[0] with { SourceId = "copy-1" };
        var source2 = b.Sources[0] with { SourceId = "copy-2" };
        var revision = new PersonaRevisionCodec().Encode(b with
        {
            Sources = [.. b.Sources, source1, source2],
            Evidence = [.. b.Evidence,
                b.Evidence[0] with { EvidenceId = "evidence-copy-1", SourceId = source1.SourceId },
                b.Evidence[0] with { EvidenceId = "evidence-copy-2", SourceId = source2.SourceId }],
            Patterns = [b.Patterns[0] with { SupportingEvidenceIds = ["evidence-a", "evidence-copy-1", "evidence-copy-2"] }]
        });
        var projection = compiler.Compile(revision, PersonaFixtures.Situation(), 4);
        Assert.Equal(["episode-a"], projection.Selection.SupportingOriginKeys);
        Assert.Equal(3, projection.Payload.GetProperty("behavioralTendencies")[0].GetProperty("supportingEvidenceIds").GetArrayLength());
    }
}
