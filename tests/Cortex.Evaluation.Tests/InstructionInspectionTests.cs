using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Cortex.Evaluation.Tools;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class InstructionInspectionTests
{
    [Fact]
    public void Offline_tool_describes_effective_instruction_without_execution()
    {
        var project = Project();
        var request = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            operation = "instruction.describe",
            payload = new { project, stateId = "participation-inactive-v1", entryKind = "group", nodeId = "group-generation" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = OfflineEvaluationTools.Invoke(request);
        Assert.True(response.Success, response.ErrorCode);
        Assert.False(response.Persisted);
        Assert.Equal("test-revision", response.Result!.Value.GetProperty("projectRevision").GetString());
        Assert.Contains(project.Document.GetProperty("project").GetProperty("personalityCore").GetString()!,
            response.Result.Value.GetProperty("system").GetString());
    }

    [Fact]
    public void Generation_snapshot_contains_real_personality_and_state_composition()
    {
        var project = Project();
        var source = project.Document.GetProperty("project");
        var state = source.GetProperty("states")[0];
        var result = FlowInstructionInspection.Describe(project, state.GetProperty("id").GetString()!,
            SessionEntryKind.Group, "group-generation");
        Assert.Contains(source.GetProperty("personalityCore").GetString()!, result.System);
        Assert.Contains(state.GetProperty("participationGuidance").GetString()!, result.System);
        Assert.Equal(project.ProjectRevision, result.ProjectRevision);
        Assert.Contains("mensagens", result.InputNames);
    }

    [Fact]
    public void Situation_snapshot_does_not_add_disabled_context()
    {
        var project = Project();
        var source = project.Document.GetProperty("project");
        var state = source.GetProperty("states")[0];
        var node = state.GetProperty("graph").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "group-situation");
        var result = FlowInstructionInspection.Describe(project, state.GetProperty("id").GetString()!,
            SessionEntryKind.Group, "group-situation");
        Assert.Equal(node.GetProperty("prompt").GetString(), result.System);
        Assert.True(result.ResponseSchema!.Value.GetProperty("properties").TryGetProperty("envolvimento", out _));
    }

    [Theory]
    [InlineData("missing", "group-situation")]
    [InlineData("participation-inactive-v1", "missing")]
    [InlineData("participation-inactive-v1", "group-entry")]
    [InlineData("participation-inactive-v1", "dm-situation")]
    public void Missing_or_non_instruction_node_cannot_be_exported(string state, string node) =>
        Assert.Throws<InvalidDataException>(() => FlowInstructionInspection.Describe(Project(), state, SessionEntryKind.Group, node));

    private static SessionFlowProject Project() => new("offline-project", 1, "test-revision",
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluation-project.json"))).RootElement.Clone());
}
