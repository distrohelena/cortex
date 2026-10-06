using Cortex.Core.Flows;
using static Cortex.Video.Editing.EditingContractValidator;

namespace Cortex.Video.Editing;

internal static class EditingReferenceValidation
{
    internal static void Validate(GeneratedEditingDocument document, EditingValidationContext? context, List<FlowIssue> issues)
    {
        Text(document.ProjectId, "/project_id", issues); Text(document.ResolutionId, "/resolution_id", issues);
        Check(document.ScriptRef, context?.ScriptReference, context, "/script_ref", true, issues);
        if (context is null) { Add(issues, "edit.context_required", "/script_ref", "Supply the authored script and input fingerprints to validate generated documents."); return; }
        if (document.ProjectId != context.Script.Project.Id || document.ScriptRef.Revision != context.Script.Revision) Add(issues, "edit.reference_stale", "/script_ref", "The project or authored revision differs from this resolution.");
        if (document is ResolvedEditPlanDocument resolved)
        {
            Check(resolved.EditPlanRef, context.PlanReference, context, "/edit_plan_ref", false, issues);
            Check(resolved.RecordingTasksRef, context.TasksReference, context, "/recording_tasks_ref", false, issues);
            if (context.Plan is null || context.Tasks is null || context.PlanReference is null || context.TasksReference is null) Add(issues, "edit.context_required", "/timeline", "A complete timeline requires its current plan and recording-task inputs.");
        }
        foreach (var input in new GeneratedEditingDocument?[] { context.Plan, context.Tasks })
            if (input is not null && (input.ResolutionId != document.ResolutionId || input.ProjectId != document.ProjectId || input.ScriptRef.Sha256 != document.ScriptRef.Sha256)) Add(issues, "edit.resolution_mismatch", "/resolution_id", "Do not mix project/input snapshots from different resolution passes.");
    }

    private static void Check(EditingDocumentReference reference, EditingDocumentReference? expected, EditingValidationContext? context, string path, bool script, List<FlowIssue> issues)
    {
        LocalPath(reference.Path, path + "/path", issues);
        if (!Hash(reference.Sha256)) Add(issues, "edit.hash", path + "/sha256", "Use a SHA-256 hex digest.");
        if (script && reference.Revision is not > 0) Add(issues, "edit.revision", path + "/revision", "The script revision is required.");
        if (!script) Text(reference.ResolutionId, path + "/resolution_id", issues);
        if (expected is null) return;
        if (!string.Equals(reference.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase) || reference.Revision != expected.Revision || reference.ResolutionId != expected.ResolutionId) Add(issues, "edit.reference_stale", path, "The referenced input bytes or identity changed; derive the result again.");
        if (context?.DocumentPath is { } documentPath)
        {
            try
            {
                var actual = Path.GetFullPath(reference.Path, Path.GetDirectoryName(Path.GetFullPath(documentPath))!);
                var wanted = Path.GetFullPath(expected.Path);
                if (!string.Equals(actual, wanted, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) Add(issues, "edit.reference_stale", path + "/path", "The reference points to a different input file.");
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { Add(issues, "edit.path_invalid", path + "/path", "The local reference path is invalid."); }
        }
    }
}
