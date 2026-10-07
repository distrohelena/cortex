using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaCandidateCodecTests
{
    private readonly PersonaCandidateCodec codec = new();
    private static ValidatedPersonaExtractionRequest Request() =>
        new PersonaCandidateRequestAdmission().Admit(PersonaCandidateFixtures.Request());

    [Fact]
    public void ValidCandidateRetainsHostProvenance()
    {
        var request = Request();
        var artifact = codec.AdmitResponse(request, PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var b = artifact.Batch;
        Assert.Equal("persona_evidence_candidates", b.ArtifactKind);
        Assert.Equal("pending_review", b.ReviewStatus);
        Assert.Equal(1, b.ContractVersion);
        Assert.Equal(request.RequestSha256, b.RequestSha256);
        Assert.Equal("synthetic-episode-1", b.Request.Source.OriginKey);
        Assert.Equal("289df48f19fdf9e95dcc1e3fcbed663d47afdc27c51104dd49f786863b2f5f53", b.Request.Source.Sha256);
        Assert.Equal("actors-lab", b.Request.Scope.ProjectId);
        var c = Assert.Single(b.Candidates);
        Assert.Equal("candidate:request-1:1", c.CandidateId);
        Assert.Equal("person:mira", c.SubjectRef);
        Assert.Equal("speaker", c.Role);
        Assert.Equal(new(0, 23, TextOffsetUnit.Utf16), c.Location);
        Assert.Equal("Mira: I got that wrong.", c.Quotation);
        Assert.Equal("Admits her own error", c.ObservedBehavior);
        Assert.Equal("May accept correction without deflecting", c.Interpretation);
        Assert.Equal(["May be specific to this topic"], c.Alternatives);
        Assert.Null(c.Confidence.Interpretation);
    }

    [Fact]
    public void ValidEmptyIsNotFailure() => Assert.Empty(codec.AdmitResponse(Request(),
        "{\"contractVersion\":1,\"candidates\":[]}").Batch.Candidates);

    public static IEnumerable<object[]> InvalidResponses()
    {
        yield return ["not json"];
        yield return ["null"];
        yield return ["{}"];
        yield return ["{\"contractVersion\":1,\"candidates\":null}"];
        yield return ["{\"contractVersion\":2,\"candidates\":[]}"];
        yield return ["{\"contractVersion\":\"1\",\"candidates\":[]}"];
        yield return ["{\"contractVersion\":1,\"candidates\":[],\"revision\":\"approved\"}"];
        yield return ["{\"contractVersion\":1,\"contractVersion\":1,\"candidates\":[]}"];
        yield return ["{\"contractVersion\":1,\"candidates\":[null]}"];
        foreach (var property in new[] { "passageId", "quotation", "observedContext", "observedBehavior", "interpretation", "alternatives", "confidence" })
        {
            var node = ResponseNode();
            node["candidates"]![0]!.AsObject().Remove(property);
            yield return [node.ToJsonString()];
            node = ResponseNode();
            node["candidates"]![0]![property] = null;
            yield return [node.ToJsonString()];
        }
        foreach (var property in new[] { "passageId", "quotation", "observedContext", "observedBehavior", "interpretation" })
        {
            var node = ResponseNode();
            node["candidates"]![0]![property] = " ";
            yield return [node.ToJsonString()];
        }
        var unknown = ResponseNode();
        unknown["candidates"]![0]!["subjectRef"] = "person:other";
        yield return [unknown.ToJsonString()];
        var passage = ResponseNode();
        passage["candidates"]![0]!["passageId"] = "unknown";
        yield return [passage.ToJsonString()];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft("Mira: I got that wrong!"))];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft() with { Alternatives = [""] })];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft(), PersonaCandidateFixtures.Draft())];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()).Replace("\"transcription\":null,", "", StringComparison.Ordinal)];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()).Replace("\"transcription\":null", "\"transcription\":null,\"transcription\":null", StringComparison.Ordinal)];
        yield return [PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()).Replace("\"interpretation\":\"May accept correction without deflecting\"", "\"interpretation\":\"\\uD800\"", StringComparison.Ordinal)];
    }

    [Theory, MemberData(nameof(InvalidResponses))]
    public void RejectsInvalidResponse(string json) =>
        Assert.Throws<InvalidDataException>(() => codec.AdmitResponse(Request(), json));

    [Fact]
    public void NoNormalizationOrQuoteRepair()
    {
        var r = new PersonaCandidateRequestAdmission().Admit(PersonaCandidateFixtures.Request("café"));
        Assert.Throws<InvalidDataException>(() => codec.AdmitResponse(r,
            PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft("cafe\u0301"))));
    }

    [Fact]
    public void DistinctInterpretationsRemainSeparateHypotheses()
    {
        var draft = PersonaCandidateFixtures.Draft();
        var b = codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(draft,
            draft with { Interpretation = "Could be politeness rather than a stable trait" })).Batch;
        Assert.Equal(2, b.Candidates.Count);
        Assert.Equal("candidate:request-1:2", b.Candidates[1].CandidateId);
    }

    [Theory]
    [InlineData(32768, true)]
    [InlineData(32769, false)]
    public void ResponseByteBoundary(int bytes, bool accepted)
    {
        const string json = "{\"contractVersion\":1,\"candidates\":[]}";
        var padded = json + new string(' ', bytes - json.Length);
        if (accepted) Assert.Empty(codec.AdmitResponse(Request(), padded).Batch.Candidates);
        else Assert.Throws<InvalidDataException>(() => codec.AdmitResponse(Request(), padded));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void CandidateCountBoundary(int count, bool accepted)
    {
        var drafts = Enumerable.Range(0, count).Select(i => PersonaCandidateFixtures.Draft() with
        { Interpretation = $"Reading {i}" }).ToArray();
        if (accepted) Assert.Equal(count, codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(drafts)).Batch.Candidates.Count);
        else Assert.Throws<InvalidDataException>(() => codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(drafts)));
    }

    [Theory]
    [InlineData("observedContext")]
    [InlineData("observedBehavior")]
    [InlineData("interpretation")]
    [InlineData("alternative")]
    [InlineData("transcription")]
    [InlineData("attribution")]
    [InlineData("confidenceInterpretation")]
    public void TextFieldBoundary(string field)
    {
        foreach (var length in new[] { 2048, 2049 })
        {
            var node = ResponseNode();
            var c = node["candidates"]![0]!;
            var value = new string('a', length);
            if (field == "alternative") c["alternatives"]![0] = value;
            else if (field is "transcription" or "attribution" or "confidenceInterpretation")
                c["confidence"]![field == "confidenceInterpretation" ? "interpretation" : field] = value;
            else c[field] = value;
            if (length == 2048) Assert.Single(codec.AdmitResponse(Request(), node.ToJsonString()).Batch.Candidates);
            else Assert.Throws<InvalidDataException>(() => codec.AdmitResponse(Request(), node.ToJsonString()));
        }
    }

    [Fact]
    public void InvalidItemRejectsWholeBatch() => Assert.Throws<InvalidDataException>(() =>
        codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft(),
            PersonaCandidateFixtures.Draft() with { PassageId = "foreign" })));

    [Fact]
    public void InvalidRawUnicodeIsRejectedBeforeJsonEncoding() => Assert.Throws<InvalidDataException>(() =>
        codec.AdmitResponse(Request(), "{\"contractVersion\":1,\"candidates\":[]}\ud800"));

    [Fact]
    public void ArtifactReopensAgainstExactRequest()
    {
        var r = Request();
        var artifact = codec.AdmitResponse(r, PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var reopened = codec.Decode(artifact.Bytes, artifact.ArtifactSha256, r);
        Assert.Equal(artifact.ArtifactSha256, reopened.ArtifactSha256);
        Assert.Equal("Admits her own error", reopened.Batch.Candidates[0].ObservedBehavior);
        Assert.Throws<InvalidDataException>(() => codec.Decode(artifact.Bytes, new string('0', 64), r));
    }

    [Fact]
    public void ReopeningDoesNotConfuseArtifactEscapingWithReturnedResponseBytes()
    {
        var drafts = Enumerable.Range(0, 4).Select(i => PersonaCandidateFixtures.Draft() with
        { ObservedContext = new string('é', 2048), Interpretation = $"Reading {i}" }).ToArray();
        var json = JsonSerializer.Serialize(new PersonaCandidateResponse(1, drafts),
            new JsonSerializerOptions(PersonaCandidateFixtures.Json) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.True(Encoding.UTF8.GetByteCount(json) < 32768);
        var r = Request();
        var a = codec.AdmitResponse(r, json);
        Assert.True(a.Bytes.Length > 32768);
        Assert.Equal(4, codec.Decode(a.Bytes, a.ArtifactSha256, r).Batch.Candidates.Count);
    }

    [Theory]
    [InlineData("artifactKind")]
    [InlineData("contractVersion")]
    [InlineData("reviewStatus")]
    [InlineData("requestSha256")]
    [InlineData("candidateId")]
    [InlineData("subjectRef")]
    [InlineData("role")]
    [InlineData("location")]
    [InlineData("quotation")]
    [InlineData("extra")]
    [InlineData("embeddedRequest")]
    public void ArtifactTamperingIsRejectedEvenWithRecomputedHash(string field)
    {
        var r = Request();
        var a = codec.AdmitResponse(r, PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var node = JsonNode.Parse(a.Bytes.Span)!;
        switch (field)
        {
            case "contractVersion": node[field] = 2; break;
            case "artifactKind": case "reviewStatus": case "requestSha256": node[field] = "fabricated"; break;
            case "extra": node["approval"] = true; break;
            case "embeddedRequest": node["request"]!["source"]!["originKey"] = "forged-origin"; break;
            case "location": node["candidates"]![0]![field]!["end"] = 1; break;
            default: node["candidates"]![0]![field] = "fabricated"; break;
        }
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        Assert.Throws<InvalidDataException>(() => codec.Decode(bytes, Hash(bytes), r));
    }

    public static IEnumerable<object[]> DifferentRequests()
    {
        var r = PersonaCandidateFixtures.Request();
        yield return [r with { Scope = r.Scope with { ProjectId = "other" } }];
        yield return [r with { Scope = r.Scope with { WorldId = "other" } }];
        yield return [r with { Scope = r.Scope with { CharacterId = "other" } }];
        yield return [r with { Source = r.Source with { SourceId = "other" } }];
        yield return [r with { Source = r.Source with { OriginKey = "other" } }];
        yield return [r with { Source = r.Source with { RightsNote = "other" } }];
        yield return [r with { Source = r.Source with { SourceKind = PersonaSourceKind.DocumentExcerpt } }];
        yield return [r with { Source = r.Source with { Text = "Mira: I got that WRONG.", Sha256 = PersonaCandidateFixtures.Hash("Mira: I got that WRONG.") } }];
        yield return [r with { RequestId = "other" }];
        yield return [r with { RunId = "other" }];
        yield return [r with { ConfigurationRevision = "other" }];
        yield return [r with { PromptRevision = "other" }];
        yield return [r with { Method = r.Method with { Version = "other" } }];
        yield return [r with { Method = r.Method with { AuthorRef = "other" } }];
        yield return [r with { Method = r.Method with { Provider = "other" } }];
        yield return [r with { Method = r.Method with { Model = "other" } }];
        yield return [r with { Method = r.Method with { ConfigurationRevision = "other" } }];
        yield return [r with { Limits = r.Limits with { MaxCandidates = 7 } }];
        yield return [r with { Passages = [r.Passages[0] with { Role = "other" }] }];
        yield return [r with { Passages = [r.Passages[0] with { PassageId = "other" }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(1, 22, TextOffsetUnit.Utf16) }] }];
        yield return [r with { SubjectRef = "person:other", Passages = [r.Passages[0] with { SubjectRef = "person:other" }] }];
    }

    [Theory, MemberData(nameof(DifferentRequests))]
    public void DifferentRequestBindingIsRejected(PersonaExtractionRequest other)
    {
        var a = codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var expected = new PersonaCandidateRequestAdmission().Admit(other);
        Assert.Throws<InvalidDataException>(() => codec.Decode(a.Bytes, a.ArtifactSha256, expected));
    }

    [Fact]
    public void CandidateSnapshotsAndBytesAreIndependent()
    {
        var r = Request();
        var a = codec.AdmitResponse(r, PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var original = a.Bytes;
        Assert.True(MemoryMarshal.TryGetArray(a.Bytes, out var mutable));
        mutable.Array![mutable.Offset] = 0;
        var snapshot = a.Batch;
        ((IList<string>)snapshot.Candidates[0].Alternatives)[0] = "forged";
        ((IList<PersonaEvidenceCandidate>)snapshot.Candidates)[0] = snapshot.Candidates[0] with { SubjectRef = "forged" };
        Assert.Equal("person:mira", a.Batch.Candidates[0].SubjectRef);
        Assert.Equal("May be specific to this topic", a.Batch.Candidates[0].Alternatives[0]);
        Assert.Single(codec.Decode(original, a.ArtifactSha256, r).Batch.Candidates);
    }

    [Fact]
    public void CandidateArtifactIsNotPersonaRevision()
    {
        var a = codec.AdmitResponse(Request(), PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        Assert.Throws<InvalidDataException>(() => new PersonaRevisionCodec().Decode(a.Bytes, a.ArtifactSha256));
    }

    [Theory]
    [InlineData("duplicateRoot")]
    [InlineData("duplicateItem")]
    [InlineData("missingRoot")]
    [InlineData("nullCandidates")]
    [InlineData("nullItem")]
    [InlineData("extraItem")]
    [InlineData("invalidRequestEnum")]
    [InlineData("duplicateConfidence")]
    public void ArtifactSchemaIsStrict(string attack)
    {
        var r = Request();
        var artifact = codec.AdmitResponse(r, PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()));
        var json = Encoding.UTF8.GetString(artifact.Bytes.Span);
        var node = JsonNode.Parse(json)!;
        switch (attack)
        {
            case "duplicateRoot": json = json.Replace("\"contractVersion\":1", "\"contractVersion\":1,\"contractVersion\":1", StringComparison.Ordinal); break;
            case "duplicateItem": json = json.Replace("\"passageId\":\"p1\"", "\"passageId\":\"p1\",\"passageId\":\"p1\"", StringComparison.Ordinal); break;
            case "duplicateConfidence": json = json.Replace("\"transcription\":null", "\"transcription\":null,\"transcription\":null", StringComparison.Ordinal); break;
            case "missingRoot": node.AsObject().Remove("reviewStatus"); json = node.ToJsonString(); break;
            case "nullCandidates": node["candidates"] = null; json = node.ToJsonString(); break;
            case "nullItem": node["candidates"]![0] = null; json = node.ToJsonString(); break;
            case "extraItem": node["candidates"]![0]!["approval"] = true; json = node.ToJsonString(); break;
            case "invalidRequestEnum": node["request"]!["source"]!["sourceKind"] = 0; json = node.ToJsonString(); break;
        }
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.Throws<InvalidDataException>(() => codec.Decode(bytes, Hash(bytes), r));
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void QuotationFieldLimitIsEnforcedBeforeAndAfterExtraction(int length, bool accepted)
    {
        var text = new string('a', length);
        var raw = PersonaCandidateFixtures.Request(text);
        if (!accepted)
        {
            Assert.Throws<InvalidDataException>(() => new PersonaCandidateRequestAdmission().Admit(raw));
            return;
        }
        var r = new PersonaCandidateRequestAdmission().Admit(raw);
        Assert.Equal(text, codec.AdmitResponse(r,
            PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft(text))).Batch.Candidates[0].Quotation);
    }

    private static JsonNode ResponseNode() => JsonNode.Parse(
        PersonaCandidateFixtures.Response(PersonaCandidateFixtures.Draft()))!;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
