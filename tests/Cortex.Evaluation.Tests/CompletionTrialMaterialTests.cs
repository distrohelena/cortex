using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class CompletionTrialMaterialTests
{
    [Fact]
    public void Stored_revisions_reconstruct_exact_profile_input_and_target()
    {
        var data = Setup();
        var material = CompletionTrialMaterial.FromStoredDocuments(data.Access, data.Claim,
            data.TargetRef, data.Target, data.Profile, data.Input);
        Assert.Equal("participation", material.Input.NodeId);
        Assert.Equal(data.Claim.Case.Revision, material.Input.InputRevision);
        Assert.Equal(data.Claim.Candidate.Revision.Sha256, RevisionHasher.Hash(material.Profile.ToRevisionDocument()));
        Assert.True(JsonElement.DeepEquals(data.Input.Payload, material.Input.Input.Payload));
    }

    [Theory]
    [InlineData("target-hash")]
    [InlineData("profile-hash")]
    [InlineData("input-hash")]
    [InlineData("target-kind")]
    [InlineData("target-version")]
    [InlineData("target-node")]
    [InlineData("input-kind")]
    [InlineData("profile-kind")]
    public void Invalid_or_substituted_material_is_rejected(string mutation)
    {
        var d = Setup();
        var target = d.Target;
        var profile = d.Profile;
        var input = d.Input;
        if (mutation.StartsWith("target")) target = new(mutation == "target-version" ? 2 : 1,
            mutation == "target-kind" ? "rubric" : "completion-target",
            JsonSerializer.SerializeToElement(new { nodeId = mutation == "target-node" ? "" : "other" }));
        if (mutation.StartsWith("profile")) profile = new(1,
            mutation == "profile-kind" ? "rubric" : "configuration-profile", JsonSerializer.SerializeToElement(new { }));
        if (mutation.StartsWith("input")) input = new(1,
            mutation == "input-kind" ? "rubric" : "completion-input", JsonSerializer.SerializeToElement(new { changed = true }));
        // Schema failures are tested with matching hashes, separately from substitution.
        var targetRef = mutation is "target-kind" or "target-node" ? Pin(d.Access.Scope, target) : d.TargetRef;
        var claim = d.Claim;
        if (mutation == "profile-kind") claim = claim with { Candidate = Pin(d.Access.Scope, profile) };
        if (mutation == "input-kind") claim = claim with { Case = Pin(d.Access.Scope, input) };
        Assert.Throws<InvalidDataException>(() => CompletionTrialMaterial.FromStoredDocuments(
            d.Access, claim, targetRef, target, profile, input));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("target")]
    [InlineData("candidate")]
    [InlineData("case")]
    public void Unauthorized_material_is_rejected(string mutation)
    {
        var d = Setup();
        var foreign = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var access = mutation == "role" ? EvaluationAccess.FromHost("test", d.Access.Scope, EvaluationRole.Proposer) : d.Access;
        var target = mutation == "target" ? d.TargetRef with { Scope = foreign } : d.TargetRef;
        var claim = mutation switch
        {
            "candidate" => d.Claim with { Candidate = d.Claim.Candidate with { Scope = foreign } },
            "case" => d.Claim with { Case = d.Claim.Case with { Scope = foreign } },
            _ => d.Claim
        };
        Assert.Throws<UnauthorizedAccessException>(() => CompletionTrialMaterial.FromStoredDocuments(
            access, claim, target, d.Target, d.Profile, d.Input));
    }

    private static (EvaluationAccess Access, TrialClaim Claim, ScopedRevisionRef TargetRef,
        RevisionDocument Target, RevisionDocument Profile, RevisionDocument Input) Setup()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var target = new RevisionDocument(1, "completion-target", JsonSerializer.SerializeToElement(new { nodeId = "participation" }));
        var profile = new ConfigurationProfile(1, "completion", "openai", "gpt-6-luna", "luna-host",
            "gpt-6-luna", DateTimeOffset.UnixEpoch,
            JsonSerializer.SerializeToElement(new { reasoningEffort = "high", maxOutputTokens = 256 }), null, [],
            new(1, "schema", JsonSerializer.SerializeToElement(new { contractVersion = 1, responseSchema = (object?)null })),
            new(1, "prompt", JsonSerializer.SerializeToElement(new { system = "Classifique a participação." })), null).ToRevisionDocument();
        var input = new RevisionDocument(1, "completion-input", JsonSerializer.SerializeToElement(new { message = "Oi" }));
        return (EvaluationAccess.FromHost("test", scope, EvaluationRole.Evaluator),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, Pin(scope, profile), Pin(scope, input), 0), Pin(scope, target), target, profile, input);
    }

    private static ScopedRevisionRef Pin(EvaluationScope scope, RevisionDocument document) =>
        new(scope, new(Guid.NewGuid(), RevisionHasher.Hash(document)));
}
