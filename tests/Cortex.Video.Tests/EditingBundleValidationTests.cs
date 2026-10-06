using System.Security.Cryptography;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class EditingBundleValidationTests
{
    [Fact]
    public void EditedInputsAreStaleEvenWithoutRevisionChanges()
    {
        var context = Context("resolved_edit_plan.json");
        Assert.Contains(EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture("resolved_edit_plan.json")),
            context with { PlanReference = context.PlanReference! with { Sha256 = new string('1', 64) } }), issue => issue.Code == "edit.reference_stale");
        Assert.Contains(EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture("edit_plan.json")),
            Context("edit_plan.json") with { ScriptReference = context.ScriptReference! with { Sha256 = new string('2', 64) } }), issue => issue.Code == "edit.reference_stale");
    }

    [Fact]
    public void PendingTasksFromAnotherPassCannotFillAResolvedTimeline()
    {
        var context = Context("resolved_edit_plan.json") with { Tasks = (RecordingTasksDocument)EditingJson.Parse(EditingSerializationTests.Fixture("recording_tasks.json")) };
        Assert.Contains(EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture("resolved_edit_plan.json")), context), issue => issue.Code == "edit.resolution_mismatch");
        Assert.Contains(EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture("resolved_edit_plan.json")), context), issue => issue.Code == "edit.pending_recording");
    }

    [Fact]
    public void AnnotationsCannotOverrideSuppliedMediaFingerprints()
    {
        var context = Context("resolved_edit_plan.json") with { MediaHashes = new Dictionary<string, string> { ["take_03"] = new string('3', 64) } };
        Assert.Contains(EditingContractValidator.Validate(EditingJson.Parse(EditingSerializationTests.Fixture("resolved_edit_plan.json")), context), issue => issue.Code == "edit.media_stale");
    }

    internal static EditingValidationContext Context(string name)
    {
        var root = Path.Combine(EditingSerializationTests.Repository(), "docs/examples/cortex-edit-v1");
        var after = name.StartsWith("after-pickups/") || name == "resolved_edit_plan.json";
        var prefix = after ? "after-pickups/" : "";
        EditingDocumentReference Reference(string file, string? resolution = null) => new()
        {
            Path = Path.Combine(root, file), Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file)))),
            Revision = resolution is null ? 1 : null, ResolutionId = resolution
        };
        return new()
        {
            DocumentPath = Path.Combine(root, name), Script = (EditScriptDocument)EditingJson.Parse(EditingSerializationTests.Fixture("script.json")),
            ScriptReference = Reference("script.json"),
            Plan = (EditPlanDocument)EditingJson.Parse(EditingSerializationTests.Fixture(prefix + "edit_plan.json")),
            Tasks = (RecordingTasksDocument)EditingJson.Parse(EditingSerializationTests.Fixture(prefix + "recording_tasks.json")),
            PlanReference = Reference(prefix + "edit_plan.json", after ? "example_after_pickups" : "example_before_pickups"),
            TasksReference = Reference(prefix + "recording_tasks.json", after ? "example_after_pickups" : "example_before_pickups")
        };
    }
}
