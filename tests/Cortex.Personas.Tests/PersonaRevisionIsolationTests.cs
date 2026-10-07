using System.Text.Json;
using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaRevisionIsolationTests
{
    private readonly PersonaRevisionCodec codec = new();
    private readonly PersonaContextCompiler compiler = new();

    [Fact]
    public void NewRevisionDoesNotRewriteExistingProjection()
    {
        var b = PersonaFixtures.Bundle();
        var r1 = codec.Encode(b);
        var old = compiler.Compile(r1, PersonaFixtures.Situation(), 4);
        var oldText = old.Payload.GetRawText();
        var r2 = codec.Encode(b with
        {
            Spec = b.Spec with
            {
                RevisionId = "R2",
                ParentArtifactSha256 = r1.ArtifactSha256,
                VoiceGuidance = [new("Voz tranquila nesta revisão.", PersonaAssertionBasis.Authorial, [], "fixture-author")]
            }
        });
        var current = compiler.Compile(r2, PersonaFixtures.Situation(), 4);
        Assert.Equal("R1", old.RevisionId);
        Assert.Equal(oldText, old.Payload.GetRawText());
        Assert.Equal(r1.ArtifactSha256, old.ArtifactSha256);
        Assert.Equal("R2", current.RevisionId);
        Assert.NotEqual(old.ArtifactSha256, current.ArtifactSha256);
        Assert.Contains("tranquila", current.Payload.GetProperty("voiceGuidance")[0].GetProperty("text").GetString());
        Assert.Equal(r1.ArtifactSha256, r2.Bundle.Spec.ParentArtifactSha256);
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("hash")]
    [InlineData("version")]
    public void CorruptedOrUnsupportedArtifactNeverLoads(string defect)
    {
        var encoded = codec.Encode(PersonaFixtures.Bundle());
        var bytes = encoded.Bytes.ToArray();
        if (defect == "byte") bytes[0] = 0;
        if (defect == "version")
        {
            var text = System.Text.Encoding.UTF8.GetString(bytes).Replace("\"contractVersion\":1", "\"contractVersion\":2", StringComparison.Ordinal);
            bytes = System.Text.Encoding.UTF8.GetBytes(text);
            Assert.Throws<InvalidDataException>(() => codec.Decode(bytes, PersonaFixtures.Hash(text)));
            return;
        }
        var hash = defect == "hash" ? new string('0', 64) : encoded.ArtifactSha256;
        Assert.Throws<InvalidDataException>(() => codec.Decode(bytes, hash));
    }

    [Fact]
    public void EvidenceRevisionPreservesOriginal()
    {
        var b = PersonaFixtures.Bundle();
        var corrected = b.Evidence[0] with
        {
            EvidenceId = "evidence-a-revised",
            SupersedesEvidenceId = "evidence-a",
            Interpretation = b.Evidence[0].Interpretation with
            { Text = "A leitura foi revista: ofereceu apoio sem confrontar a pessoa.", ReviewStatus = "author_corrected" }
        };
        var encoded = codec.Encode(b with
        {
            Evidence = [.. b.Evidence, corrected],
            Patterns = [b.Patterns[0] with { SupportingEvidenceIds = ["evidence-a-revised"] }]
        });
        var roundTrip = codec.Decode(encoded.Bytes, encoded.ArtifactSha256).Bundle;
        var original = roundTrip.Evidence.Single(e => e.EvidenceId == "evidence-a");
        var revised = roundTrip.Evidence.Single(e => e.EvidenceId == "evidence-a-revised");
        Assert.Equal("Pode valorizar autonomia sem abandonar o apoio.", original.Interpretation.Text);
        Assert.Contains("leitura foi revista", revised.Interpretation.Text);
        Assert.Equal("evidence-a", revised.SupersedesEvidenceId);
        Assert.Equal(original.ObservedBehavior, revised.ObservedBehavior);
        Assert.Equal(3, roundTrip.Evidence.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("cycle")]
    public void InvalidSupersessionIsRejected(string defect)
    {
        var b = PersonaFixtures.Bundle();
        var first = b.Evidence[0] with { SupersedesEvidenceId = defect == "missing" ? "absent" : defect == "self" ? "evidence-a" : "evidence-b" };
        var second = defect == "cycle" ? b.Evidence[1] with { SupersedesEvidenceId = "evidence-a" } : b.Evidence[1];
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with { Evidence = [first, second] }));
    }

    [Fact]
    public void ReopeningWithFreshCodecReconstructsSameProjection()
    {
        var encoded = codec.Encode(PersonaFixtures.Bundle());
        var first = compiler.Compile(encoded, PersonaFixtures.Situation(), 4);
        var reopened = new PersonaRevisionCodec().Decode(encoded.Bytes, encoded.ArtifactSha256);
        var second = new PersonaContextCompiler().Compile(reopened, PersonaFixtures.Situation(), 4);
        Assert.Equal(first.Payload.GetRawText(), second.Payload.GetRawText());
        Assert.Equal(first.ArtifactSha256, second.ArtifactSha256);
        Assert.Equal(first.Selection.SelectedPatternIds, second.Selection.SelectedPatternIds);
    }

    [Fact]
    public void SameRevisionLabelCannotHideChangedContent()
    {
        var b = PersonaFixtures.Bundle();
        var first = codec.Encode(b);
        var changed = codec.Encode(b with { Spec = b.Spec with { IdentityDescription = "Descrição revista com identidade estável." } });
        Assert.Equal(first.Bundle.Spec.RevisionId, changed.Bundle.Spec.RevisionId);
        Assert.NotEqual(first.ArtifactSha256, changed.ArtifactSha256);
    }

    [Fact]
    public void DisposedParsingDocumentDoesNotInvalidateProjection()
    {
        var projection = compiler.Compile(codec.Encode(PersonaFixtures.Bundle()), PersonaFixtures.Situation(), 4);
        using (var document = JsonDocument.Parse(projection.Payload.GetRawText()))
            Assert.Equal("situation-a", document.RootElement.GetProperty("situation").GetProperty("situationId").GetString());
        Assert.Equal("situation-a", projection.Payload.GetProperty("situation").GetProperty("situationId").GetString());
    }
}
