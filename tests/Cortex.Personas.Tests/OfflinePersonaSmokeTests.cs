using System.Text.Json;
using Xunit;

namespace Cortex.Personas.Tests;

public sealed class OfflinePersonaSmokeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("close-private", "frustration", "close", "private", "calm", "intimate_provocation")]
    [InlineData("stranger-private", "frustration", "stranger", "private", "calm", null)]
    [InlineData("close-public", "frustration", "close", "public", "calm", null)]
    [InlineData("overwhelmed", "frustration", "close", "private", "overwhelmed", null)]
    [InlineData("validation-not-assumed", "validation_request", "close", "private", "calm", null)]
    [InlineData("technical-close", "technical_question", "close", "private", "calm", "technical_help")]
    [InlineData("technical-stranger-public", "technical_question", "stranger", "public", "calm", "technical_help")]
    [InlineData("neutral", "neutral_update", "close", "private", "calm", null)]
    [InlineData("own-error", "correction", "close", "private", "calm", "admit_error")]
    [InlineData("unknown-relationship", "frustration", null, "private", "calm", null)]
    [InlineData("unknown-state", "frustration", "close", "private", null, null)]
    [InlineData("support-requested", "support_request", "close", "private", "overwhelmed", "simple_support")]
    public async Task SmokeProducesAuditableProjectionWithoutExternalEffects(string caseId, string act,
        string? relation, string? audience, string? state, string? expectedPattern)
    {
        var codec = new PersonaRevisionCodec();
        var encoded = codec.Encode(PersonaFixtures.SmokeBundle());
        var reopened = codec.Decode(encoded.Bytes, encoded.ArtifactSha256);
        var situation = PersonaFixtures.Situation(act, relation, audience, state) with { SituationId = caseId };
        var projection = new PersonaContextCompiler().Compile(reopened, situation, 4);
        var expected = expectedPattern is null ? Array.Empty<string>() : new[] { expectedPattern };
        Assert.Equal(expected, projection.Selection.SelectedPatternIds);
        Assert.Equal("R1", projection.RevisionId);
        Assert.Equal(encoded.ArtifactSha256, projection.ArtifactSha256);
        Assert.Equal(caseId, projection.SituationId);
        var fixture = new PersonaInferenceFixture(projection);
        await fixture.Bound.CompleteAsync(fixture.Request, TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.Adapter.Calls);
        Assert.Equal("offline-test", Assert.Single(fixture.Adapter.Requests).Provider);
        Assert.Equal("Completed", Assert.Single(fixture.Observations).Stage.ToString());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            caseId,
            projection.RevisionId,
            projection.ArtifactSha256,
            selected = projection.Selection.SelectedPatternIds,
            skipped = projection.Selection.SkippedPatterns,
            outcome = "contract_pass",
            personaFidelity = "not_evaluated"
        }));
    }
}
