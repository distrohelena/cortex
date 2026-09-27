using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Xunit;
using Filomena.Evaluation.Tools;

namespace Filomena.Evaluation.Tests;

public sealed class SyntheticStepCasePreparationTests
{
    [Fact]
    public void Offline_tool_prepares_input_and_provenance_without_authorizing_execution()
    {
        var request = JsonSerializer.Serialize(new
        {
            contractVersion = 1, operation = "case.prepare.synthetic",
            payload = new { caseId = "case", context = Context(), selfActorRef = "filomena",
                instruction = Instruction(["mensagens"]), supplementalFixtures = Json("{}") }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = OfflineEvaluationTools.Invoke(request);
        Assert.True(response.Success, response.ErrorCode);
        Assert.False(response.Persisted);
        Assert.False(response.Result!.Value.GetProperty("executionAuthorized").GetBoolean());
        Assert.Equal("completion-input", response.Result.Value.GetProperty("prepared").GetProperty("input").GetProperty("kind").GetString());
    }

    [Fact]
    public void All_25_cases_prepare_for_actual_situation_step_without_rubric_leakage()
    {
        var project = new SessionFlowProject("offline", 1, "R1", Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluation-project.json"))));
        var state = project.Document.GetProperty("project").GetProperty("states")[0].GetProperty("id").GetString()!;
        var instruction = FlowInstructionInspection.Describe(project, state, SessionEntryKind.Group, "group-situation");
        var corpus = Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "conversation-cases.json")));
        var count = 0;
        foreach (var item in corpus.GetProperty("cases").EnumerateArray())
        {
            var prepared = SyntheticStepCasePreparation.Prepare(item.GetProperty("id").GetString()!,
                item.GetProperty("context"), "filomena", instruction, Json("{}"));
            Assert.Equal(instruction.InputNames.Order(), prepared.Input.Payload.EnumerateObject().Select(p => p.Name).Order());
            Assert.False(prepared.Input.Payload.TryGetProperty("expected", out _));
            Assert.Equal("synthetic_fixture", prepared.Provenance.Payload.GetProperty("sourceKind").GetString());
            Assert.Equal(RevisionHasher.Hash(prepared.Input), prepared.Provenance.Payload.GetProperty("inputSha256").GetString());
            count++;
        }
        Assert.Equal(25, count);
    }

    [Fact]
    public void Supplemental_step_values_are_explicitly_labelled_as_fixtures_not_real_upstream_outputs()
    {
        var result = SyntheticStepCasePreparation.Prepare("case", Context(), "filomena",
            Instruction(["mensagens", "envolvimento"]), Json("""{"envolvimento":"direto"}"""));
        Assert.Equal("direto", result.Input.Payload.GetProperty("envolvimento").GetString());
        Assert.Equal("envolvimento", Assert.Single(result.Provenance.Payload.GetProperty("supplementalFixtureFields").EnumerateArray()).GetString());
        Assert.False(result.Provenance.Payload.GetProperty("runtimeReplay").GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"envolvimento\":\"direto\",\"expected\":true}")]
    [InlineData("{\"envolvimento\":\"direto\",\"mensagens\":[]}")]
    [InlineData("{\"envolvimento\":\"direto\",\"envolvimento\":\"nenhum\"}")]
    public void Missing_extra_overridden_or_duplicate_fields_fail(string supplemental)
    {
        Assert.Throws<InvalidDataException>(() => SyntheticStepCasePreparation.Prepare("case", Context(), "filomena",
            Instruction(["mensagens", "envolvimento"]), Json(supplemental)));
    }

    private static FlowInstructionSnapshot Instruction(string[] fields) =>
        new("R1", "state", "step", "Instruction", null, fields, Json("{}"));
    private static JsonElement Context() => Json("""
        {"conversation":"group","history":[],"currentBatch":[{"sender":{"ref":"helena","name":"Helena"},"text":"Filomena, oi"}]}
        """);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
