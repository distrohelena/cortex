using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void Validate_rejects_missing_refs_and_cross_project_refs()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var crossProject = Manifest(scope) with { Target = Ref(new(scope.WorkspaceId, Guid.NewGuid()), Guid.NewGuid()) };
        Assert.Contains(ManifestValidator.Validate(crossProject), x => x.Code == "revision.scope_mismatch" && x.Path == "target");
        Assert.Contains(ManifestValidator.Validate(Manifest(scope) with { Dataset = null! }),
            x => x.Code == "revision.missing" && x.Path == "dataset");
    }

    [Fact]
    public void Manifest_snapshots_candidate_collection_at_construction()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var candidates = new List<ScopedRevisionRef> { Ref(scope, Guid.NewGuid()) };
        var manifest = Manifest(scope, candidates);

        candidates.Clear();

        Assert.Single(manifest.Candidates);
    }

    [Fact]
    public void Validate_rejects_invalid_scope_and_hash()
    {
        var scope = new EvaluationScope(Guid.Empty, Guid.NewGuid());
        var manifest = Manifest(scope) with { Baseline = Ref(scope, Guid.NewGuid(), "not-a-sha256") };
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "scope.invalid" && x.Path == "scope");
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "revision.hash_invalid" && x.Path == "baseline");
    }

    [Fact]
    public void Profile_validation_rejects_unsupported_parameter_and_invalid_schema()
    {
        var profile = Profile("luna", "completion", """{"temperature":0.2,"unsupported_magic":true}""", null, [],
            """{"contractVersion":0}""", null);
        var issues = ProfileValidator.Validate(profile);
        Assert.Contains(issues, x => x.Code == "profile.parameter_unsupported" && x.Path == "parameters.unsupported_magic");
        Assert.Contains(issues, x => x.Code == "schema.contract_version" && x.Path == "schema.contractVersion");
        Assert.Contains(issues, x => x.Code == "profile.parameter_unsupported" && x.Path == "parameters.temperature");
    }

    [Fact]
    public void Validation_rejects_invalid_threshold_repetitions_budgets_and_stop_rules()
    {
        Assert.Contains(ProfileValidator.Validate(Profile("jev", "typed-with-summary", "{}", 1, [],
            """{"contractVersion":1}""", Doc("summary-profile", """{"prompt":"summary"}"""))),
            x => x.Code == "profile.threshold_invalid");
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var manifest = Manifest(scope) with { Repetitions = 0, Budgets = new(0, 0, 0, 0), StopRules = new(3, 2, null) };
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "manifest.repetitions_invalid");
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "budget.invalid");
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "stop_rules.invalid");
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    public void Jev_threshold_excludes_both_endpoints(double threshold)
    {
        var question = new ChoiceQuestionProfile("intent", "Select intent.",
            new Dictionary<string, string> { ["support"] = "Support", ["social"] = "Social" });
        var issues = ProfileValidator.Validate(Profile("jev", "typed-with-summary", "{}", threshold, [question],
            """{"contractVersion":1}""", Doc("summary-profile", """{"prompt":"summary"}""")));

        Assert.Contains(issues, x => x.Code == "profile.threshold_invalid");
    }

    [Fact]
    public void Completion_allows_absent_threshold_and_typed_recipe_requires_pinned_summary_profile()
    {
        var completion = Profile("luna", "completion", "{}", null, [], """{"contractVersion":1}""", null);
        Assert.Empty(ProfileValidator.Validate(completion));

        var typed = Profile("luna", "typed-with-summary", """{"reasoningEffort":"high","maxOutputTokens":4096}""",
            null, [], """{"contractVersion":1}""", null);
        Assert.Contains(ProfileValidator.Validate(typed), x => x.Code == "profile.summary_profile_required");
    }

    [Fact]
    public void Choice_questions_preserve_field_instructions_and_each_keyed_criterion()
    {
        var question = new ChoiceQuestionProfile("intent", "Choose the best intent.",
            new Dictionary<string, string> { ["support"] = "Needs help", ["social"] = "Casual conversation" });
        var profile = Profile("jev", "typed-with-summary", "{}", 0.5, [question], """{"contractVersion":1}""",
            Doc("summary-profile", """{"model":"summary-v2","prompt":"summarize"}"""));

        Assert.Empty(ProfileValidator.Validate(profile));
        Assert.Equal("Choose the best intent.", profile.ChoiceQuestions[0].Instructions);
        Assert.Equal("Needs help", profile.ChoiceQuestions[0].Criteria["support"]);
        Assert.Equal("Casual conversation", profile.ChoiceQuestions[0].Criteria["social"]);
    }

    [Fact]
    public void Provider_descriptors_accept_only_their_frozen_parameter_sets()
    {
        Assert.Empty(ProfileValidator.Validate(Profile("luna", "completion",
            """{"reasoningEffort":"high","maxOutputTokens":4096}""", null, [],
            """{"contractVersion":1}""", null)));
        Assert.Empty(ProfileValidator.Validate(Profile("lm-studio", "completion",
            """{"generationTemperature":0.7,"generationMaxTokens":8192,"requestTimeoutSeconds":180}""", null, [],
            """{"contractVersion":1}""", null)));
        Assert.Contains(ProfileValidator.Validate(Profile("unknown", "completion", "{}", null, [],
            """{"contractVersion":1}""", null)), x => x.Code == "profile.provider_unsupported");
    }

    private static ExperimentManifest Manifest(EvaluationScope scope, IReadOnlyList<ScopedRevisionRef>? candidates = null) => new(scope,
        Ref(scope, Guid.NewGuid()), Ref(scope, Guid.NewGuid()), Ref(scope, Guid.NewGuid()),
        Ref(scope, Guid.NewGuid()), Ref(scope, Guid.NewGuid()), candidates ?? [Ref(scope, Guid.NewGuid())],
        2, 42, CandidateOrder.Interleaved, new(20, 10000, 10000, 5m), new(1, 5, null));
    private static ConfigurationProfile Profile(string provider, string recipe, string parameters, double? threshold,
        IReadOnlyList<ChoiceQuestionProfile> questions, string schema, RevisionDocument? summary) => new(
        1, recipe, provider, "model-alias", "endpoint-alias", "provider-model-id",
        DateTimeOffset.Parse("2026-09-26T00:00:00Z"), Json(parameters), threshold, questions,
        new RevisionDocument(1, "schema", Json(schema)), new RevisionDocument(1, "prompt", Json("""{"text":"hello"}""")), summary);
    private static RevisionDocument Doc(string kind, string payload) => new(1, kind, Json(payload));
    private static ScopedRevisionRef Ref(EvaluationScope scope, Guid id, string? hash = null) =>
        new(scope, new RevisionRef(id, hash ?? new string('a', 64)));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
