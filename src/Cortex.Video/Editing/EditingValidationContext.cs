namespace Cortex.Video.Editing;

public sealed record EditingValidationContext
{
    public string? DocumentPath { get; init; }
    public required EditScriptDocument Script { get; init; }
    public EditingDocumentReference? ScriptReference { get; init; }
    public EditPlanDocument? Plan { get; init; }
    public RecordingTasksDocument? Tasks { get; init; }
    public IReadOnlyDictionary<string, string>? MediaHashes { get; init; }
}
