using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Filomena.Evaluation.Tools;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class InstructionProfilePreparationTests
{
    [Fact]
    public void Offline_tool_returns_prepared_profile_and_hash_without_persisting()
    {
        var request = JsonSerializer.Serialize(new
        {
            contractVersion = 1, operation = "profile.prepare",
            payload = new { instruction = Instruction(), settings = Settings("openai") }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = OfflineEvaluationTools.Invoke(request);
        Assert.True(response.Success, response.ErrorCode);
        Assert.False(response.Persisted);
        var result = response.Result!.Value;
        Assert.Equal("Instrução composta", result.GetProperty("profile").GetProperty("prompt").GetProperty("payload").GetProperty("system").GetString());
        Assert.Equal(64, result.GetProperty("sha256").GetString()!.Length);
        Assert.False(result.GetProperty("executionAuthorized").GetBoolean());
    }

    [Fact]
    public void Actual_project_steps_prepare_for_each_state_without_starting_runtime()
    {
        var document = Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluation-project.json")));
        var project = new SessionFlowProject("offline", 1, "pinned-test-revision", document);
        var prepared = 0;
        foreach (var state in document.GetProperty("project").GetProperty("states").EnumerateArray())
        {
            foreach (var node in new[] { "group-situation", "group-presence", "group-generation" })
            {
                var instruction = FlowInstructionInspection.Describe(project, state.GetProperty("id").GetString()!, SessionEntryKind.Group, node);
                Assert.Empty(ProfileValidator.Validate(InstructionProfilePreparation.Bind(instruction, Settings("openai"))));
                prepared++;
                if (node == "group-generation") continue;
                Assert.Empty(ProfileValidator.Validate(InstructionProfilePreparation.Bind(instruction,
                    Settings("jev", node == "group-situation" ? Summary() : null))));
                prepared++;
            }
        }
        Assert.Equal(15, prepared);
    }

    [Fact]
    public void Luna_profile_preserves_effective_instruction_schema_and_explicit_settings()
    {
        var settings = Settings("openai");
        var before = RevisionHasher.Hash(settings.ToRevisionDocument());
        var instruction = Instruction();
        var profile = InstructionProfilePreparation.Bind(instruction, settings);
        Assert.Equal("Instrução composta", profile.Prompt.Payload.GetProperty("system").GetString());
        Assert.True(JsonElement.DeepEquals(instruction.ResponseSchema!.Value, profile.Schema.Payload.GetProperty("responseSchema")));
        Assert.Equal("high", profile.Parameters.GetProperty("reasoningEffort").GetString());
        Assert.Equal(before, RevisionHasher.Hash(settings.ToRevisionDocument()));
        Assert.NotEqual(before, RevisionHasher.Hash(profile.ToRevisionDocument()));
    }

    [Fact]
    public void Jev_requires_explicit_summary_model_for_free_summary_field() =>
        Assert.Throws<InvalidDataException>(() => InstructionProfilePreparation.Bind(Instruction(), Settings("jev")));

    [Fact]
    public void Jev_summary_recipe_preserves_full_schema_and_derives_exact_summary_contract()
    {
        var instruction = Instruction();
        var result = InstructionProfilePreparation.Bind(instruction, Settings("jev", Summary()));
        Assert.Equal("typed-with-summary", result.Recipe);
        Assert.Equal(2, result.Schema.Payload.GetProperty("responseSchema").GetProperty("properties").EnumerateObject().Count());
        var properties = result.SummaryProfile!.Schema.Payload.GetProperty("responseSchema").GetProperty("properties");
        Assert.Equal("resumo", Assert.Single(properties.EnumerateObject()).Name);
        Assert.Equal("Resumo curto", properties.GetProperty("resumo").GetProperty("description").GetString());
        Assert.Equal("Resumo explícito", result.SummaryProfile.Prompt.Payload.GetProperty("system").GetString());
    }

    [Theory]
    [InlineData("jev")]
    [InlineData("laya")]
    public void Typed_model_cannot_be_substituted_for_free_generation(string provider)
    {
        var instruction = Instruction() with { ResponseSchema = Json("""
            {"type":"object","additionalProperties":false,"properties":{"texto":{"type":"string"}},"required":["texto"]}
            """) };
        Assert.Throws<InvalidDataException>(() => InstructionProfilePreparation.Bind(instruction, Settings(provider, Summary())));
    }

    [Fact]
    public void Unsupported_score_scale_is_not_coerced_into_a_boolean()
    {
        var instruction = Instruction() with { ResponseSchema = Json("""
            {"type":"object","additionalProperties":false,"properties":{"score":{"type":"number"}},"required":["score"]}
            """) };
        Assert.Throws<InvalidDataException>(() => InstructionProfilePreparation.Bind(instruction, Settings("jev")));
    }

    [Fact]
    public void Project_revision_is_part_of_profile_provenance()
    {
        var first = InstructionProfilePreparation.Bind(Instruction(), Settings("openai"));
        var second = InstructionProfilePreparation.Bind(Instruction() with { ProjectRevision = "R2" }, Settings("openai"));
        Assert.NotEqual(RevisionHasher.Hash(first.ToRevisionDocument()), RevisionHasher.Hash(second.ToRevisionDocument()));
    }

    private static FlowInstructionSnapshot Instruction() => new("R1", "state", "step", "Instrução composta", Json("""
        {"type":"object","additionalProperties":false,"properties":{"acao":{"type":"string","enum":["responder","calar"]},"resumo":{"type":"string","description":"Resumo curto"}},"required":["acao","resumo"]}
        """), ["mensagens"], Json("{}"));
    private static ConfigurationProfile Settings(string provider, SummaryModelProfile? summary = null) => new(
        1, summary is null ? "completion" : "typed-with-summary", provider,
        provider == "openai" ? "gpt-6-luna" : provider == "laya" ? "multilingual" : "jev-1.13.0",
        provider + "-host", provider == "openai" ? "gpt-6-luna" : provider == "laya" ? "multilingual" : "jev-1.13.0",
        DateTimeOffset.UnixEpoch, Json(provider == "openai" ? """{"reasoningEffort":"high","maxOutputTokens":512}""" : "{}"),
        provider == "openai" ? null : 0.5, [], new(1, "schema", Json("""{"contractVersion":1,"responseSchema":null}""")),
        new(1, "prompt", Json("""{"system":"placeholder"}""")), summary);
    private static SummaryModelProfile Summary() => new(1, "openai", "gpt-6-luna", "gpt-6-luna", "openai-host", DateTimeOffset.UnixEpoch,
        Json("""{"reasoningEffort":"high","maxOutputTokens":256}"""), new(1, "schema", Json("""{"contractVersion":1,"responseSchema":null}""")),
        new(1, "prompt", Json("""{"system":"Resumo explícito"}""")));
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
