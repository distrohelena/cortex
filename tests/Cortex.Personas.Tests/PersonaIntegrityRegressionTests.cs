using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaIntegrityRegressionTests
{
    private readonly PersonaRevisionCodec codec = new();

    [Theory]
    [InlineData("version")]
    [InlineData("scope")]
    public void DuplicateJsonPropertiesNeverBecomeValidatedProof(string dimension)
    {
        var text = Encoding.UTF8.GetString(codec.Encode(PersonaFixtures.Bundle()).Bytes.Span);
        text = dimension == "version"
            ? text.Replace("\"contractVersion\":1", "\"contractVersion\":2,\"contractVersion\":1", StringComparison.Ordinal)
            : text.Replace("\"projectId\":\"project-a\"", "\"projectId\":\"foreign\",\"projectId\":\"project-a\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => codec.Decode(Encoding.UTF8.GetBytes(text), PersonaFixtures.Hash(text)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("spec")]
    [InlineData("sources_null")]
    [InlineData("unknown_member")]
    public void MissingOrUnknownSchemaFieldsAreRejected(string defect)
    {
        var body = JsonNode.Parse(codec.Encode(PersonaFixtures.Bundle()).Bytes.Span)!.AsObject();
        if (defect == "version") body.Remove("contractVersion");
        else if (defect == "spec") body.Remove("spec");
        else if (defect == "sources_null") body["sources"] = null;
        else body["deliveryEnabled"] = true;
        var text = body.ToJsonString();
        var error = Assert.Throws<InvalidDataException>(() => codec.Decode(Encoding.UTF8.GetBytes(text), PersonaFixtures.Hash(text)));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("source-a", error.Message);
    }

    [Fact]
    public void EncodingNeverSilentlyReplacesMalformedSourceText()
    {
        var b = PersonaFixtures.Bundle();
        var badText = "A" + '\uD800' + "B";
        var source = b.Sources[0] with { Text = badText, Sha256 = PersonaFixtures.Hash(badText) };
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with
        {
            Sources = [source, b.Sources[1]],
            Evidence = [b.Evidence[0] with { Location = new(0, 3, TextOffsetUnit.Utf16) }, b.Evidence[1]]
        }));
    }

    [Theory]
    [InlineData(TextOffsetUnit.Utf16)]
    [InlineData(TextOffsetUnit.UnicodeScalar)]
    public void StandaloneLocationRejectsMalformedUnicode(TextOffsetUnit unit)
    {
        Assert.Throws<InvalidDataException>(() => new TextLocation(0, 1, unit).Extract("" + '\uD800'));
    }

    [Fact]
    public void ObservedBehaviorCannotUseEmptyPassage()
    {
        var b = PersonaFixtures.Bundle();
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with
        { Evidence = [b.Evidence[0] with { Location = new(0, 0, TextOffsetUnit.Utf16) }, b.Evidence[1]] }));
    }

    [Fact]
    public void GeneratedDraftIsNotAnObservedBehavioralExperience()
    {
        var b = PersonaFixtures.Bundle();
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with
        { Sources = [b.Sources[0] with { SourceKind = PersonaSourceKind.GeneratedDraft }, b.Sources[1]] }));
    }

    [Fact]
    public void BehavioralSubjectMustHaveAnExplicitParticipantRole()
    {
        var b = PersonaFixtures.Bundle();
        Assert.Throws<InvalidDataException>(() => codec.Encode(b with
        { Evidence = [b.Evidence[0] with { SubjectRef = "unrepresented-subject" }, b.Evidence[1]] }));
    }

    [Fact]
    public void ArtifactMemoryExposureCannotMutateValidatedSnapshot()
    {
        var encoded = codec.Encode(PersonaFixtures.Bundle());
        Assert.True(MemoryMarshal.TryGetArray(encoded.Bytes, out var exposed));
        exposed.Array![exposed.Offset] = 0;
        Assert.Equal("R1", encoded.Bundle.Spec.RevisionId);
        Assert.Equal((byte)'{', encoded.Bytes.Span[0]);
        var bytes = encoded.Bytes.ToArray();
        var decoded = codec.Decode(bytes, encoded.ArtifactSha256);
        bytes[0] = 0;
        Assert.Equal("R1", decoded.Bundle.Spec.RevisionId);
    }

    [Fact]
    public void RepeatedEncodingIsDeterministicWithoutImplicitTime()
    {
        Assert.Equal(codec.Encode(PersonaFixtures.Bundle()).ArtifactSha256,
            codec.Encode(PersonaFixtures.Bundle()).ArtifactSha256);
    }
}
