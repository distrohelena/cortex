using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video.Editing;

public static partial class EditingFlowTypes
{
    private static readonly FlowType ResolutionText = new StringType();
    private static readonly FlowType ResolutionNumber = new NumberType();
    private static readonly FlowType OptionalScore = new NullableType(new NumberType());
    private static ObjectType Shape(params (string Name, FlowType Type, bool Required)[] fields) => new(fields.ToDictionary(f => f.Name, f => new FlowField(f.Type, f.Required)));
    private static readonly FlowType Profile = ((ObjectType)((ObjectType)Script.Fields["project"].Type).Fields["recording_defaults"].Type);
    private static readonly FlowType Visual = ((ObjectType)((ArrayType)Script.Fields["beats"].Type).ElementType).Fields["visual"].Type;
    private static readonly FlowType Requirement = Shape(("beat_id", ResolutionText, true), ("code", ResolutionText, true), ("status", ResolutionText, true), ("message", ResolutionText, true));
    private static readonly FlowType TaskMatch = Shape(("mode", new EnumType(["exact", "semantic", "intent"]), true), ("requested_text", ResolutionText, false), ("requested_meaning", ResolutionText, false));
    private static readonly FlowType CandidateMatch = Shape(("mode", new EnumType(["exact", "semantic", "intent"]), true), ("method", ResolutionText, true), ("basis", ResolutionText, true),
        ("requested_text", new NullableType(new StringType()), true), ("requested_meaning", new NullableType(new StringType()), true),
        ("semantic_score", OptionalScore, true), ("take_score", OptionalScore, true), ("evaluated_metrics", Shape(("semantic_match", ResolutionNumber, true), ("brevity", ResolutionNumber, false)), true),
        ("unavailable_metrics", new ArrayType(new StringType()), true), ("unavailable_preferences", new ArrayType(new StringType()), true), ("ranking_basis", ResolutionText, true), ("transcript_sha256", ResolutionText, true), ("speaker_basis", ResolutionText, true));
    private static readonly FlowType Candidate = Shape(("id", ResolutionText, true), ("media_id", ResolutionText, true), ("source_in", ResolutionNumber, true), ("source_out", ResolutionNumber, true), ("transcript", ResolutionText, true),
        ("speaker", ResolutionText, false), ("timing_granularity", new EnumType(["segment"]), true), ("match", CandidateMatch, true), ("rank", ResolutionNumber, true), ("reason", ResolutionText, true));
    private static readonly FlowType Media = Shape(("id", ResolutionText, true), ("source_id", ResolutionText, true), ("path", ResolutionText, true), ("sha256", ResolutionText, true), ("type", new EnumType(["video"]), true),
        ("width", ResolutionNumber, true), ("height", ResolutionNumber, true), ("duration_sec", ResolutionNumber, true), ("fps", ResolutionText, true), ("speaker", ResolutionText, false));
    private static readonly FlowType AudioDecisionType = Shape(("status", ResolutionText, true), ("recording_task_id", ResolutionText, false), ("search", Shape(("status", ResolutionText, true), ("outcome", ResolutionText, true), ("method", ResolutionText, true)), false));
    private static readonly FlowType Beat = Shape(("beat_id", ResolutionText, true), ("status", ResolutionText, true), ("audio", AudioDecisionType, true),
        ("visual", Shape(("status", ResolutionText, true), ("intent", Visual, true), ("media_id", ResolutionText, false), ("requirements", new ArrayType(Requirement), true)), true),
        ("candidates", new ArrayType(Candidate), true), ("selected_candidate_id", ResolutionText, false), ("selection", Shape(("by", ResolutionText, true), ("review_status", new EnumType(["pending"]), true)), false));
    public static ObjectType Plan { get; } = Shape(("schema", new EnumType([EditingDocumentKinds.Plan]), true), ("project_id", ResolutionText, true), ("script_ref", ScriptReference, true), ("resolution_id", ResolutionText, true),
        ("status", new EnumType(["incomplete"]), true), ("media", new ArrayType(Media), true), ("beats", new ArrayType(Beat), true), ("unresolved_requirements", new ArrayType(Requirement), true));
    public static ObjectType Tasks { get; } = Shape(("schema", new EnumType([EditingDocumentKinds.Tasks]), true), ("project_id", ResolutionText, true), ("script_ref", ScriptReference, true), ("resolution_id", ResolutionText, true),
        ("recording_tasks", new ArrayType(Shape(("id", ResolutionText, true), ("beat_id", ResolutionText, true), ("reason", new EnumType(["no_suitable_clip_found", "script_requires_exact_wording", "script_requests_recording"]), true),
            ("script", ResolutionText, false), ("brief", ResolutionText, false), ("match", TaskMatch, true), ("target_duration_sec", ResolutionNumber, false), ("recording", Profile, true), ("status", new EnumType(["needs_recording"]), true))), true));
}
