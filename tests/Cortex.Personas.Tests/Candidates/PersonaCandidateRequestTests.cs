using Xunit;

namespace Cortex.Personas.Tests;

public sealed class PersonaCandidateRequestTests
{
    private readonly PersonaCandidateRequestAdmission admission = new();

    [Fact]
    public void AdmitsExactSourceAndTargetPassages()
    {
        var admitted = admission.Admit(PersonaCandidateFixtures.Request());
        var request = admitted.Snapshot;
        Assert.Equal("Mira: I got that wrong.", request.Source.Text);
        Assert.Equal("person:mira", request.SubjectRef);
        Assert.Equal("synthetic-episode-1", request.Source.OriginKey);
        Assert.Equal(PersonaSourceKind.FictionalFixture, request.Source.SourceKind);
        Assert.Equal("speaker", request.Passages[0].Role);
        Assert.Equal("Mira: I got that wrong.", request.Passages[0].Location.Extract(request.Source.Text));
        Assert.Equal(64, admitted.RequestSha256.Length);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        var r = PersonaCandidateFixtures.Request();
        yield return [r with { RequestId = "" }];
        yield return [r with { RunId = " " }];
        yield return [r with { ConfigurationRevision = "" }];
        yield return [r with { PromptRevision = "" }];
        yield return [r with { SubjectRef = "" }];
        yield return [r with { Scope = r.Scope with { ProjectId = "" } }];
        yield return [r with { Scope = r.Scope with { WorldId = "" } }];
        yield return [r with { Scope = r.Scope with { CharacterId = "" } }];
        yield return [r with { Method = r.Method with { Kind = "" } }];
        yield return [r with { Method = r.Method with { Version = "" } }];
        yield return [r with { Method = r.Method with { AuthorRef = "" } }];
        yield return [r with { Source = r.Source with { Sha256 = new string('0', 64) } }];
        yield return [r with { Source = r.Source with { Sha256 = "invalid" } }];
        yield return [r with { Source = r.Source with { SourceKind = PersonaSourceKind.GeneratedDraft } }];
        yield return [r with { Source = r.Source with { SourceKind = (PersonaSourceKind)999 } }];
        yield return [r with { Source = r.Source with { OriginKey = "" } }];
        yield return [r with { Source = r.Source with { SourceId = "" } }];
        yield return [r with { Passages = [r.Passages[0] with { SubjectRef = "person:other" }] }];
        yield return [r with { Passages = [r.Passages[0] with { Role = "" }] }];
        yield return [r with { Passages = [r.Passages[0] with { PassageId = "" }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(1, 1, TextOffsetUnit.Utf16) }] }];
        yield return [r with { Passages = [r.Passages[0], r.Passages[0]] }];
        yield return [r with { Passages = [r.Passages[0], r.Passages[0] with { PassageId = "p2" }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(-1, 2, TextOffsetUnit.Utf16) }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(2, 1, TextOffsetUnit.Utf16) }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(0, 1, (TextOffsetUnit)999) }] }];
        yield return [r with { Passages = [r.Passages[0] with { Location = new(0, 999, TextOffsetUnit.Utf16) }] }];
        yield return [r with { SubjectRef = "\ud800" }];
        yield return [r with { Source = r.Source with { Text = "\ud800" } }];
        yield return [r with { Scope = null! }];
        yield return [r with { Method = null! }];
        yield return [r with { Source = null! }];
        yield return [r with { Passages = null! }];
        yield return [r with { Passages = [null!] }];
        yield return [r with { Limits = null! }];
    }

    [Theory, MemberData(nameof(InvalidRequests))]
    public void RejectsInvalidRequest(PersonaExtractionRequest request) =>
        Assert.Throws<InvalidDataException>(() => admission.Admit(request));

    [Theory]
    [InlineData(PersonaSourceKind.FictionalFixture)]
    [InlineData(PersonaSourceKind.DocumentExcerpt)]
    [InlineData(PersonaSourceKind.AuthorialDecision)]
    [InlineData(PersonaSourceKind.DeliveredUtterance)]
    public void PreservesSupportedSourceKind(PersonaSourceKind kind)
    {
        var r = PersonaCandidateFixtures.Request();
        Assert.Equal(kind, admission.Admit(r with { Source = r.Source with { SourceKind = kind } }).Snapshot.Source.SourceKind);
    }

    [Fact]
    public void EmptyPassagesAreValidButDoNotGuessAttribution() =>
        Assert.Empty(admission.Admit(PersonaCandidateFixtures.Request() with { Passages = [] }).Snapshot.Passages);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void RejectsNonPositiveLimits(int field, int value)
    {
        var r = PersonaCandidateFixtures.Request();
        var l = r.Limits;
        l = field switch
        {
            0 => l with { MaxSourceBytes = value },
            1 => l with { MaxResponseBytes = value },
            2 => l with { MaxCandidates = value },
            _ => l with { MaxTextCodeUnits = value }
        };
        Assert.Throws<InvalidDataException>(() => admission.Admit(r with { Limits = l }));
    }

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public void SourceByteBoundary(int bytes, bool accepted)
    {
        var r = PersonaCandidateFixtures.Request(new string('a', bytes)) with { Passages = [] };
        if (accepted) Assert.Equal(bytes, admission.Admit(r).Snapshot.Source.Text.Length);
        else Assert.Throws<InvalidDataException>(() => admission.Admit(r));
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void PassageCodeUnitBoundary(int length, bool accepted)
    {
        var r = PersonaCandidateFixtures.Request(new string('a', length));
        if (accepted) Assert.Single(admission.Admit(r).Snapshot.Passages);
        else Assert.Throws<InvalidDataException>(() => admission.Admit(r));
    }

    [Fact]
    public void MultibyteSourceUsesUtf8BytesRatherThanCharacters()
    {
        var r = PersonaCandidateFixtures.Request(new string('é', 8192)) with { Passages = [] };
        admission.Admit(r);
        var tooBig = PersonaCandidateFixtures.Request(new string('é', 8193)) with { Passages = [] };
        Assert.Throws<InvalidDataException>(() => admission.Admit(tooBig));
    }

    [Fact]
    public void MixedOffsetUnitsDetectOverlapAndPreservePairBoundaries()
    {
        var r = PersonaCandidateFixtures.Request("😀ab");
        var p = r.Passages[0];
        var disjoint = r with
        {
            Passages = [p with { Location = new(0, 1, TextOffsetUnit.UnicodeScalar) },
            p with { PassageId = "p2", Location = new(2, 4, TextOffsetUnit.Utf16) }]
        };
        Assert.Equal(2, admission.Admit(disjoint).Snapshot.Passages.Count);
        Assert.Throws<InvalidDataException>(() => admission.Admit(disjoint with
        { Passages = [disjoint.Passages[0], disjoint.Passages[1] with { Location = new(0, 2, TextOffsetUnit.Utf16) }] }));
        Assert.Throws<InvalidDataException>(() => admission.Admit(r with
        { Passages = [p with { Location = new(1, 2, TextOffsetUnit.Utf16) }] }));
    }

    [Fact]
    public void RequestSnapshotCannotBeMutated()
    {
        var r = PersonaCandidateFixtures.Request();
        var passages = r.Passages.ToArray();
        var admitted = admission.Admit(r with { Passages = passages });
        var hash = admitted.RequestSha256;
        passages[0] = passages[0] with { Role = "attacker" };
        ((IList<PersonaAttributedPassage>)admitted.Snapshot.Passages)[0] = passages[0];
        Assert.Equal("speaker", admitted.Snapshot.Passages[0].Role);
        Assert.Equal(hash, admitted.RequestSha256);
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("runId")]
    [InlineData("configurationRevision")]
    [InlineData("promptRevision")]
    [InlineData("subjectRef")]
    [InlineData("projectId")]
    [InlineData("worldId")]
    [InlineData("characterId")]
    [InlineData("sourceId")]
    [InlineData("originKey")]
    [InlineData("rightsNote")]
    [InlineData("kind")]
    [InlineData("version")]
    [InlineData("authorRef")]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("methodConfigurationRevision")]
    [InlineData("passageId")]
    [InlineData("role")]
    public void MetadataCodeUnitLimitIsEnforced(string field)
    {
        foreach (var length in new[] { 2048, 2049 })
        {
            var r = PersonaCandidateFixtures.Request();
            var value = new string('a', length);
            r = field switch
            {
                "requestId" => r with { RequestId = value },
                "runId" => r with { RunId = value },
                "configurationRevision" => r with { ConfigurationRevision = value },
                "promptRevision" => r with { PromptRevision = value },
                "subjectRef" => r with { SubjectRef = value, Passages = [r.Passages[0] with { SubjectRef = value }] },
                "projectId" => r with { Scope = r.Scope with { ProjectId = value } },
                "worldId" => r with { Scope = r.Scope with { WorldId = value } },
                "characterId" => r with { Scope = r.Scope with { CharacterId = value } },
                "sourceId" => r with { Source = r.Source with { SourceId = value } },
                "originKey" => r with { Source = r.Source with { OriginKey = value } },
                "rightsNote" => r with { Source = r.Source with { RightsNote = value } },
                "kind" => r with { Method = r.Method with { Kind = value } },
                "version" => r with { Method = r.Method with { Version = value } },
                "authorRef" => r with { Method = r.Method with { AuthorRef = value } },
                "provider" => r with { Method = r.Method with { Provider = value } },
                "model" => r with { Method = r.Method with { Model = value } },
                "methodConfigurationRevision" => r with { Method = r.Method with { ConfigurationRevision = value } },
                "passageId" => r with { Passages = [r.Passages[0] with { PassageId = value }] },
                _ => r with { Passages = [r.Passages[0] with { Role = value }] }
            };
            if (length == 2048) Assert.Equal(64, admission.Admit(r).RequestSha256.Length);
            else Assert.Throws<InvalidDataException>(() => admission.Admit(r));
        }
    }
}
