using System.Text.Json;
using Cortex.Evaluation.Contracts;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void Validate_rejects_missing_refs_and_cross_project_refs()
    {
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var crossProject = Manifest(scope, target: Ref(new(scope.WorkspaceId, Guid.NewGuid()), Guid.NewGuid()));
        Assert.Contains(ManifestValidator.Validate(crossProject), x => x.Code == "revision.scope_mismatch" && x.Path == "target");
        Assert.Contains(ManifestValidator.Validate(Manifest(scope, missingDataset: true)),
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
        var manifest = Manifest(scope, baseline: Ref(scope, Guid.NewGuid(), "not-a-sha256"), repetitions: 0,
            budgets: new(0, 0, 0, 0), stopRules: new(3, 2, null));
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "scope.invalid" && x.Path == "scope");
        Assert.Contains(ManifestValidator.Validate(manifest), x => x.Code == "revision.hash_invalid" && x.Path == "baseline");
    }

    [Fact]
    public void Profile_validation_rejects_unsupported_parameter_and_invalid_schema()
    {
        var profile = Profile("openai", "completion", """{"temperature":0.2,"unsupported_magic":true}""", null, [],
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
            """{"contractVersion":1}""", Summary())),
            x => x.Code == "profile.threshold_invalid");
        var scope = new EvaluationScope(Guid.NewGuid(), Guid.NewGuid());
        var manifest = Manifest(scope, repetitions: 0, budgets: new(0, 0, 0, 0), stopRules: new(3, 2, null));
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
            """{"contractVersion":1}""", Summary()));

        Assert.Contains(issues, x => x.Code == "profile.threshold_invalid");
    }

    [Fact]
    public void Completion_allows_absent_threshold_and_typed_recipe_requires_pinned_summary_profile()
    {
        var completion = Profile("openai", "completion", "{}", null, [], """{"contractVersion":1}""", null);
        Assert.Empty(ProfileValidator.Validate(completion));

        var typed = Profile("jev", "typed-with-summary", "{}",
            null, [], """{"contractVersion":1}""", null);
        Assert.Contains(ProfileValidator.Validate(typed), x => x.Code == "profile.summary_profile_required");
    }

    [Theory]
    [InlineData("jev", "jev-1.13.0")]
    [InlineData("laya", "typed-decisions")]
    [InlineData("openjev", "minicpm5-2b")]
    public void System_one_completion_profiles_can_be_typed_only_without_summary(string provider, string model)
    {
        var question = new ChoiceQuestionProfile("intent", "Choose intent.",
            new Dictionary<string, string> { ["yes"] = "Yes", ["no"] = "No" });
        var profile = Profile(provider, "completion", "{}", 0.5, [question],
            """{"contractVersion":1}""", null, model);

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Theory]
    [InlineData("openjev", 20, true)]
    [InlineData("openjev", 21, false)]
    [InlineData("jev", 21, true)]
    [InlineData("laya", 21, true)]
    public void Openjev_profile_rejects_more_than_twenty_options_before_any_trial(string provider, int count, bool valid)
    {
        var values = Enumerable.Range(0, count).Select(i => $"v{i}").ToArray();
        var schema = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            responseSchema = new
            {
                type = "object", additionalProperties = false, required = new[] { "acao" },
                properties = new { acao = new { type = "string", @enum = values } }
            }
        });
        var model = provider switch { "jev" => "jev-1.13.0", "laya" => "typed-decisions", _ => "minicpm5-2b" };
        var bySchema = ProfileValidator.Validate(Profile(provider, "completion", "{}", 0.5, [], schema, null, model));
        var byQuestion = ProfileValidator.Validate(Profile(provider, "completion", "{}", 0.5,
            [new ChoiceQuestionProfile("acao", "Choose.", values.ToDictionary(v => v, v => v))], """{"contractVersion":1}""", null, model));

        if (valid)
        {
            Assert.Empty(bySchema);
            Assert.Empty(byQuestion);
        }
        else
        {
            Assert.Contains(bySchema, x => x.Code == "profile.options_exceed_provider_limit" && x.Path == "schema.responseSchema.properties.acao");
            Assert.Contains(byQuestion, x => x.Code == "profile.choice_question_invalid" && x.Path == "choiceQuestions[0]");
        }
    }

    [Theory]
    [InlineData("{}", null)]
    [InlineData("""{"optionOrder":"fixed"}""", null)]
    [InlineData("""{"optionOrder":"rotations"}""", null)]
    [InlineData("""{"optionOrder":"Rotations"}""", "profile.parameter_invalid")]
    [InlineData("""{"optionOrder":"average"}""", "profile.parameter_invalid")]
    [InlineData("""{"optionOrder":1}""", "profile.parameter_invalid")]
    [InlineData("""{"optionOrder":null}""", "profile.parameter_invalid")]
    public void Openjev_option_order_is_optional_and_limited_to_fixed_or_rotations(string parameters, string? code)
    {
        var issues = ProfileValidator.Validate(Profile("openjev", "completion", parameters, 0.5, [],
            """{"contractVersion":1}""", null, "minicpm5-2b"));
        if (code is null) Assert.Empty(issues);
        else Assert.Equal([new ValidationIssue(code, "parameters.optionOrder")], issues);
    }

    [Theory]
    [InlineData("jev", "jev-1.13.0")]
    [InlineData("laya", "typed-decisions")]
    public void Option_order_stays_unsupported_for_other_typed_providers(string provider, string model)
    {
        Assert.Contains(ProfileValidator.Validate(Profile(provider, "completion", """{"optionOrder":"rotations"}""", 0.5, [],
            """{"contractVersion":1}""", null, model)), x => x.Code == "profile.parameter_unsupported" && x.Path == "parameters.optionOrder");
    }

    [Fact]
    public void Summary_profile_rejects_missing_identity_and_invalid_summary_content()
    {
        var summary = new SummaryModelProfile(1, "openai", "gpt-summary", "", "endpoint-alias",
            DateTimeOffset.Parse("2026-09-26T00:00:00Z"), Json("{}"),
            Doc("schema", """{"contractVersion":1}"""), Doc("prompt", """{"text":"summary"}"""));
        var issues = ProfileValidator.Validate(Profile("jev", "typed-with-summary", "{}", 0.5, [],
            """{"contractVersion":1}""", summary));
        Assert.Contains(issues, x => x.Code == "profile.field_invalid" && x.Path == "summaryProfile.resolvedModelId");
    }

    [Fact]
    public void Choice_questions_preserve_field_instructions_and_each_keyed_criterion()
    {
        var question = new ChoiceQuestionProfile("intent", "Choose the best intent.",
            new Dictionary<string, string> { ["support"] = "Needs help", ["social"] = "Casual conversation" });
        var profile = Profile("jev", "typed-with-summary", "{}", 0.5, [question], """{"contractVersion":1}""",
            Summary());

        Assert.Empty(ProfileValidator.Validate(profile));
        Assert.Equal("Choose the best intent.", profile.ChoiceQuestions[0].Instructions);
        Assert.Equal("Needs help", profile.ChoiceQuestions[0].Criteria["support"]);
        Assert.Equal("Casual conversation", profile.ChoiceQuestions[0].Criteria["social"]);
    }

    [Fact]
    public void Provider_descriptors_accept_only_their_frozen_parameter_sets()
    {
        Assert.Empty(ProfileValidator.Validate(Profile("openai", "completion",
            """{"reasoningEffort":"high","maxOutputTokens":4096}""", null, [],
            """{"contractVersion":1}""", null)));
        Assert.Empty(ProfileValidator.Validate(Profile("lmstudio", "completion",
            """{"generationTemperature":0.7,"generationMaxTokens":8192,"requestTimeoutSeconds":180}""", null, [],
            """{"contractVersion":1}""", null)));
        Assert.Empty(ProfileValidator.Validate(Profile("laya", "typed-with-summary", "{}", 0.5,
            [new ChoiceQuestionProfile("field", "instructions", new Dictionary<string, string> { ["yes"] = "Yes", ["no"] = "No" })],
            """{"contractVersion":1}""", Summary(), "typed-decisions")));
        Assert.Contains(ProfileValidator.Validate(Profile("laya", "typed-with-summary", "{}", 0.5, [],
            """{"contractVersion":1}""", Summary(), "auto")), x => x.Code == "profile.model_unsupported");
        Assert.Contains(ProfileValidator.Validate(Profile("unknown", "completion", "{}", null, [],
            """{"contractVersion":1}""", null)), x => x.Code == "profile.provider_unsupported");
    }

    [Fact]
    public void Openai_profile_accepts_advertised_none_and_max_efforts_but_rejects_minimal()
    {
        ConfigurationProfile OpenAiProfile(string effort) => Profile("openai", "completion",
            JsonSerializer.Serialize(new { reasoningEffort = effort }), null, [],
            """{"contractVersion":1}""", null, "gpt-6-luna");

        Assert.Empty(ProfileValidator.Validate(OpenAiProfile("none")));
        Assert.Empty(ProfileValidator.Validate(OpenAiProfile("max")));
        Assert.Contains(ProfileValidator.Validate(OpenAiProfile("minimal")),
            x => x.Code == "profile.parameter_invalid" && x.Path == "parameters.reasoningEffort");
    }

    private static ExperimentManifest Manifest(EvaluationScope scope, IReadOnlyList<ScopedRevisionRef>? candidates = null,
        ScopedRevisionRef? target = null, ScopedRevisionRef? baseline = null, bool missingDataset = false,
        int repetitions = 2, ExperimentBudgets? budgets = null, ExperimentStopRules? stopRules = null) => new(scope,
        target ?? Ref(scope), baseline ?? Ref(scope), missingDataset ? null! : Ref(scope), Ref(scope), Ref(scope),
        candidates ?? [Ref(scope, Guid.NewGuid())], repetitions, 42, CandidateOrder.Interleaved,
        budgets ?? new(20, 10000, 10000, 5m), stopRules ?? new(1, 5, null));
    private static ConfigurationProfile Profile(string provider, string recipe, string parameters, double? threshold,
        IReadOnlyList<ChoiceQuestionProfile> questions, string schema, SummaryModelProfile? summary, string model = "jev-1.13.0") => new(
        1, recipe, provider, model, "endpoint-alias", "provider-model-id",
        DateTimeOffset.Parse("2026-09-26T00:00:00Z"), Json(parameters), threshold, questions,
        new RevisionDocument(1, "schema", Json(schema)), new RevisionDocument(1, "prompt", Json("""{"text":"hello"}""")), summary);
    private static SummaryModelProfile Summary() => new(1, "openai", "gpt-summary", "gpt-summary-pinned", "endpoint-summary",
        DateTimeOffset.Parse("2026-09-26T00:00:00Z"), Json("{}"), Doc("schema", """{"contractVersion":1}"""),
        Doc("prompt", """{"text":"summary"}"""));
    private static RevisionDocument Doc(string kind, string payload) => new(1, kind, Json(payload));
    private static ScopedRevisionRef Ref(EvaluationScope scope) => Ref(scope, Guid.NewGuid());
    private static ScopedRevisionRef Ref(EvaluationScope scope, Guid id, string? hash = null) =>
        new(scope, new RevisionRef(id, hash ?? new string('a', 64)));
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
