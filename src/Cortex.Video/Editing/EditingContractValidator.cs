using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cortex.Core.Flows;

namespace Cortex.Video.Editing;

public static class EditingContractValidator
{
    internal static readonly HashSet<string> Metrics = ["semantic_match", "delivery", "audio_quality", "visual_quality", "brevity", "continuity"];

    public static IReadOnlyList<FlowIssue> Validate(EditingDocument document, EditingValidationContext? context = null)
    {
        var issues = new List<FlowIssue>();
        var expected = document switch { EditScriptDocument => EditingDocumentKinds.Script, EditorialDecisionPlanDocument decision => decision.Schema==EditingDocumentKinds.DecisionsV2?EditingDocumentKinds.DecisionsV2:EditingDocumentKinds.Decisions, EditPlanDocument => EditingDocumentKinds.Plan,
            RecordingTasksDocument => EditingDocumentKinds.Tasks, ResolvedEditPlanDocument => EditingDocumentKinds.Resolved, _ => "" };
        if (document.Schema != expected) Add(issues, "edit.schema", "/schema", "The document type and schema identifier must agree.");
        if (document.Metadata is { } metadata && metadata.ValueKind != JsonValueKind.Object) Add(issues, "edit.metadata", "/metadata", "Metadata must be an object.");
        if (document is EditorialDecisionPlanDocument decisions) issues.AddRange(EditorialDecisionValidation.Validate(decisions, context?.Script));
        if (document is EditScriptDocument script) ValidateScript(script, issues);
        if (document is GeneratedEditingDocument generated) EditingReferenceValidation.Validate(generated, context, issues);
        if (document is EditPlanDocument plan) ValidatePlan(plan, context, issues);
        if (document is RecordingTasksDocument tasks) ValidateTasks(tasks, context, issues);
        if (document is ResolvedEditPlanDocument resolved) EditingTimelineValidation.Validate(resolved, context, issues);
        return issues;
    }

    public static void ThrowIfInvalid(EditingDocument document, EditingValidationContext? context = null)
    {
        var issues = Validate(document, context);
        if (issues.Count > 0) throw new InvalidDataException(string.Join("; ", issues.Select(issue => issue.Code + " " + issue.Path + ": " + issue.Message)));
    }

    internal static void Add(List<FlowIssue> issues, string code, string path, string message) => issues.Add(new(code, path, message));
    internal static void Text(string? value, string path, List<FlowIssue> issues)
    { if (string.IsNullOrWhiteSpace(value)) Add(issues, "edit.text_required", path, "A nonempty string is required."); }
    internal static void Positive(double? value, string path, List<FlowIssue> issues)
    { if (value is { } number && (!double.IsFinite(number) || number <= 0)) Add(issues, "edit.number_invalid", path, "The value must be finite and positive."); }
    internal static void LocalPath(string? value, string path, List<FlowIssue> issues)
    { Text(value, path, issues); if (value?.Contains("://", StringComparison.Ordinal) == true || value?.Contains('\0') == true) Add(issues, "edit.path_invalid", path, "Use a local filesystem path."); }
    internal static void Ids(IEnumerable<string?> values, string path, List<FlowIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) { Text(value, path, issues); if (value is not null && !seen.Add(value)) Add(issues, "edit.id_duplicate", path, "IDs must be unique within the collection."); }
    }
    internal static bool Same(double left, double right) => double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) < .000001;
    internal static bool Hash(string? value) => value is not null && Regex.IsMatch(value, "^[a-fA-F0-9]{64}$");
    internal static bool Pending(DecisionStatus value) => value is not (DecisionStatus.Matched or DecisionStatus.IntentionalNone);
    internal static bool Property(JsonElement value, string name, out JsonElement item)
    { item = default; return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out item); }

    private static void ValidateScript(EditScriptDocument script, List<FlowIssue> issues)
    {
        if (script.Revision < 1) Add(issues, "edit.revision", "/revision", "Revision must be a positive integer.");
        Text(script.Project.Id, "/project/id", issues); Text(script.Project.Title, "/project/title", issues);
        Positive(script.Project.TargetDurationSec, "/project/target_duration_sec", issues);
        var ratio = script.Project.AspectRatio.Split(':');
        var valid = ratio.Length == 2 && double.TryParse(ratio[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
            double.TryParse(ratio[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) && width > 0 && height > 0 &&
            script.Project.Format switch { ProjectFormat.Vertical => width < height, ProjectFormat.Horizontal => width > height, ProjectFormat.Square => width == height, _ => false };
        if (!valid) Add(issues, "edit.aspect_mismatch", "/project/aspect_ratio", "Format and positive aspect ratio must agree.");
        Ids(script.Sources.Select(source => source?.Id), "/sources", issues); Ids(script.Beats.Select(beat => beat?.Id), "/beats", issues);
        if (script.Beats.Count == 0) Add(issues, "edit.beat_order", "/beats", "At least one required beat is needed.");
        var sources = script.Sources.Where(source => source is not null).Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < script.Sources.Count; i++)
        {
            var source = script.Sources[i]; if (source is null) continue;
            LocalPath(source.Path, $"/sources/{i}/path", issues);
            if (source.Type == SourceKind.Image && (source.Speaker is not null || source.Transcription is not null)) Add(issues, "edit.source_fields", $"/sources/{i}", "An image is not a speaker transcription source.");
        }
        for (var i = 0; i < script.Beats.Count; i++)
        {
            var beat = script.Beats[i]; if (beat is null) continue;
            var path = $"/beats/{i}"; var audio = beat.Audio; var visual = beat.Visual;
            if (audio.Type == AudioKind.None)
            {
                if (audio.Search is not null || audio.Selection is not null || audio.Fallback is not null || audio.Script is not null || audio.Brief is not null || audio.Recording is not null || audio.TargetDurationSec is not null || audio.RequireExactWording is not null)
                    Add(issues, "edit.audio_fields", path + "/audio", "Intentional silence cannot also request speech.");
                if (visual.DurationSec is null) Add(issues, "edit.duration_required", path + "/visual/duration_sec", "A silent beat needs an authored display duration.");
            }
            else if (audio.Type == AudioKind.FindClip)
            {
                if (audio.Search is null) Add(issues, "edit.search_query", path + "/audio/search", "A clip search is required.");
                else
                {
                    var search = audio.Search; Text(search.Speaker, path + "/audio/search/speaker", issues);
                    if (search.Match == MatchMode.Exact ? string.IsNullOrWhiteSpace(search.Text) || search.Meaning is not null : string.IsNullOrWhiteSpace(search.Meaning) || search.Text is not null)
                        Add(issues, "edit.search_query", path + "/audio/search", "Exact queries require text; semantic/intent queries require meaning, without mixing them.");
                    if (search.SourceId is not null && !sources.Contains(search.SourceId)) Add(issues, "edit.reference_unknown", path + "/audio/search/source_id", "The declared source does not exist.");
                }
                if (audio.Script is not null || audio.Brief is not null || audio.Recording is not null || audio.TargetDurationSec is not null || audio.RequireExactWording is not null)
                    Add(issues, "edit.audio_fields", path + "/audio", "Put recording instructions in the fallback, not the clip search.");
                if (audio.Fallback is { } fallback)
                {
                    if (fallback.Type != AudioKind.Record) Add(issues, "edit.audio_fields", path + "/audio/fallback/type", "The recording fallback must be record.");
                    Positive(fallback.TargetDurationSec, path + "/audio/fallback/target_duration_sec", issues);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(audio.Script) && string.IsNullOrWhiteSpace(audio.Brief)) Add(issues, "edit.recording_brief", path + "/audio", "Recording needs an authored script or brief.");
                if (audio.Search is not null || audio.Selection is not null || audio.Fallback is not null) Add(issues, "edit.audio_fields", path + "/audio", "A direct recording cannot also be a search.");
                if (audio.RequireExactWording == true && string.IsNullOrWhiteSpace(audio.Script)) Add(issues, "edit.recording_brief", path + "/audio/script", "Exact wording needs a script.");
                Positive(audio.TargetDurationSec, path + "/audio/target_duration_sec", issues);
            }
            if (audio.Selection is { } selection)
            {
                if (selection.Strategy != "best_take") Add(issues, "edit.selection_strategy", path + "/audio/selection/strategy", "Use best_take.");
                Positive(selection.MaxDurationSec, path + "/audio/selection/max_duration_sec", issues);
                if (selection.Score is { } score)
                {
                    if (score.Any(pair => !Metrics.Contains(pair.Key) || !double.IsFinite(pair.Value) || pair.Value < 0)) Add(issues, "edit.weights_invalid", path + "/audio/selection/score", "Use known metrics and finite nonnegative weights.");
                    if (!Same(score.Values.Sum(), 1)) Add(issues, "edit.weights_sum", path + "/audio/selection/score", "Weights must sum to 1.");
                }
            }
            if (visual.Type == VisualKind.Asset && (visual.Source is null || !sources.Contains(visual.Source))) Add(issues, "edit.reference_unknown", path + "/visual/source", "The asset source does not exist.");
            if (visual.Type is VisualKind.Speaker or VisualKind.SpeakerWithOverlay) Text(visual.Speaker, path + "/visual/speaker", issues);
            Positive(visual.DurationSec, path + "/visual/duration_sec", issues);
            if (visual.Focus is { } focus)
            {
                if (focus.Type != "crop_zoom" || string.IsNullOrWhiteSpace(focus.TargetText)) Add(issues, "edit.focus_invalid", path + "/visual/focus", "Use crop_zoom with a nonempty text target.");
                if (focus.SourceRectPixels is { } rect && (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || rect.X < 0 || rect.Y < 0 || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || rect.Width <= 0 || rect.Height <= 0)) Add(issues, "edit.focus_invalid", path + "/visual/focus/source_rect_pixels", "Pixel bounds must be finite and positive within their coordinate space.");
            }
            if ((visual.Enter is not null && visual.Enter != "cut") || (visual.Exit is not null && visual.Exit != "cut")) Add(issues, "edit.transition", path + "/visual", "This version supports explicit cut transitions.");
        }
    }

    internal static Dictionary<string, MediaSnapshot> ValidateMedia(IReadOnlyList<MediaSnapshot> media, EditingValidationContext? context, List<FlowIssue> issues)
    {
        Ids(media.Select(item => item?.Id), "/media", issues);
        var result = new Dictionary<string, MediaSnapshot>(StringComparer.Ordinal);
        for (var i = 0; i < media.Count; i++)
        {
            var item = media[i]; if (item is null || string.IsNullOrWhiteSpace(item.Id)) continue;
            result.TryAdd(item.Id, item); var path = $"/media/{i}";
            LocalPath(item.Path, path + "/path", issues);
            if (!Hash(item.Sha256)) Add(issues, "edit.hash", path + "/sha256", "Use a SHA-256 hex digest.");
            if (item.Type!=MediaKind.Audio && (item.Width <= 0 || item.Height <= 0)) Add(issues, "edit.media_dimensions", path, "Measured dimensions must be positive.");
            if (item.Type is MediaKind.Video or MediaKind.Audio)
            {
                if (item.DurationSec is null || item.Type==MediaKind.Video && item.Fps is null) Add(issues, "edit.media_properties", path, "A video needs measured duration and frame rate.");
                Positive(item.DurationSec, path + "/duration_sec", issues);
                if (item.Fps is not null && !Regex.IsMatch(item.Fps, @"^[1-9][0-9]*(/[1-9][0-9]*)?$")) Add(issues, "edit.media_properties", path + "/fps", "Use a positive frame rate.");
            }
            else if (item.DurationSec is not null || item.Fps is not null) Add(issues, "edit.media_properties", path, "Images do not have video source clocks.");
            if (context is not null && !context.Script.Sources.Any(source => source.Id == item.SourceId)) Add(issues, "edit.reference_unknown", path + "/source_id", "The snapshot source is not declared in the script.");
            if (context?.MediaHashes?.TryGetValue(item.Id, out var hash) == true && !string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase)) Add(issues, "edit.media_stale", path + "/sha256", "Media bytes differ from the analysed snapshot.");
        }
        return result;
    }

    internal static void Interval(string mediaId, double start, double end, IReadOnlyDictionary<string, MediaSnapshot> media, string path, List<FlowIssue> issues)
    {
        if (!media.TryGetValue(mediaId, out var source)) { Add(issues, "edit.reference_unknown", path + "/media_id", "The media snapshot does not exist."); return; }
        if (source.Type is not(MediaKind.Video or MediaKind.Audio) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || source.DurationSec is null || end > source.DurationSec)
            Add(issues, "edit.interval_invalid", path, "Moving-media intervals must be finite, positive and inside their measured source.");
    }

    internal static void Match(JsonElement match, string path, ClipSelection? policy, List<FlowIssue> issues)
    {
        if (match.ValueKind != JsonValueKind.Object) { Add(issues, "edit.match_evidence", path, "Match evidence must be an object."); return; }
        if (!Property(match, "mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("exact" or "semantic" or "intent")) Add(issues, "edit.match_evidence", path + "/mode", "Use a known match mode.");
        if (!Property(match, "method", out var method) || method.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(method.GetString())) Add(issues, "edit.match_evidence", path + "/method", "Measured match evidence needs an evaluation method.");
        foreach (var name in new[] { "semantic_score", "take_score" })
            if (Property(match, name, out var score) && score.ValueKind != JsonValueKind.Null && (score.ValueKind != JsonValueKind.Number || !score.TryGetDouble(out var value) || !double.IsFinite(value) || value < 0 || value > 1))
                Add(issues, "edit.score_range", path + "/" + name, "Scores must be finite values from 0 to 1, or an explicit unmeasured null.");
        if (Property(match, "evaluated_metrics", out var measured) && measured.ValueKind == JsonValueKind.Object)
            foreach (var metric in measured.EnumerateObject()) if (metric.Value.ValueKind != JsonValueKind.Number || !metric.Value.TryGetDouble(out var value) || !double.IsFinite(value) || value < 0 || value > 1)
                Add(issues, "edit.score_range", path + "/evaluated_metrics/" + metric.Name, "Metric values must be finite and normalized.");
        if (Property(match, "basis", out var basis) && basis.ValueKind == JsonValueKind.String && basis.GetString() == "transcript_only" && measured.ValueKind == JsonValueKind.Object &&
            measured.EnumerateObject().Any(metric => metric.Name is "delivery" or "audio_quality" or "visual_quality"))
            Add(issues, "edit.score_modality", path + "/evaluated_metrics", "Text evidence cannot measure vocal delivery, audio quality or visual quality.");
        if (policy?.Score is { } weights && Property(match, "take_score", out var total) && total.ValueKind != JsonValueKind.Null)
        {
            if (total.ValueKind != JsonValueKind.Number) return;
            var unavailable = Property(match, "unavailable_metrics", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToHashSet() : [];
            if (weights.Any(pair => pair.Value > 0 && (unavailable.Contains(pair.Key) || !Property(measured, pair.Key, out _)))) Add(issues, "edit.score_unmeasured", path + "/take_score", "Do not supply a full weighted score while positive-weight metrics are unmeasured.");
            else
            {
                var sum = weights.Sum(pair => pair.Value > 0 && Property(measured, pair.Key, out var metric) && metric.TryGetDouble(out var value) ? pair.Value * value : 0);
                if (!total.TryGetDouble(out var score) || !Same(sum, score)) Add(issues, "edit.score_weighted", path + "/take_score", "The full score must use the declared weights without redistribution.");
            }
        }
    }

    private static void ValidatePlan(EditPlanDocument plan, EditingValidationContext? context, List<FlowIssue> issues)
    {
        var media = ValidateMedia(plan.Media, context, issues);
        Ids(plan.Beats.Select(beat => beat?.BeatId), "/beats", issues);
        if (context is not null && !plan.Beats.Select(beat => beat.BeatId).SequenceEqual(context.Script.Beats.Select(beat => beat.Id))) Add(issues, "edit.beat_order", "/beats", "Keep every authored beat in the authored order.");
        for (var i = 0; i < plan.Beats.Count; i++)
        {
            var beat = plan.Beats[i]; if (beat is null) continue; var path = $"/beats/{i}";
            var intent = context?.Script.Beats.FirstOrDefault(item => item.Id == beat.BeatId);
            Ids(beat.Candidates.Select(item => item?.Id), path + "/candidates", issues);
            foreach (var candidate in beat.Candidates)
            {
                if (candidate is null) continue;
                Interval(candidate.MediaId, candidate.SourceIn, candidate.SourceOut, media, path + "/candidates/" + candidate.Id, issues);
                Text(candidate.Transcript, path + "/candidates/" + candidate.Id + "/transcript", issues);
                if (candidate.Rank < 1) Add(issues, "edit.rank", path + "/candidates", "Ranks are positive integers.");
                Match(candidate.Match, path + "/candidates/" + candidate.Id + "/match", intent?.Audio.Selection, issues);
                if (intent?.Audio.Selection?.MaxDurationSec is { } maximum && candidate.SourceOut - candidate.SourceIn > maximum) Add(issues, "edit.clip_too_long", path + "/candidates", "The candidate exceeds the authored maximum duration.");
                if (intent?.Audio.Search?.Speaker is { } speaker && candidate.Speaker != speaker) Add(issues, "edit.speaker_mismatch", path + "/candidates", "The declared speaker does not match the request.");
                if (intent?.Audio.Search is { } search)
                {
                    if (!Property(candidate.Match, "mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() != JsonNamingPolicy.SnakeCaseLower.ConvertName(search.Match.ToString())) Add(issues, "edit.match_mode", path + "/candidates", "Evidence must use the authored matching mode.");
                    if (search.Match == MatchMode.Exact && !(" " + Normalize(candidate.Transcript) + " ").Contains(" " + Normalize(search.Text ?? "") + " ", StringComparison.Ordinal)) Add(issues, "edit.exact_wording", path + "/candidates", "An exact request requires its words in order, not a paraphrase.");
                }
            }
            if (beat.SelectedCandidateId is not null && !beat.Candidates.Any(candidate => candidate.Id == beat.SelectedCandidateId)) Add(issues, "edit.reference_unknown", path + "/selected_candidate_id", "The selected candidate must belong to this beat.");
            if (beat.Visual.Requirements?.Any(requirement => Property(requirement, "status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "needs_alignment") == true && (beat.Status == DecisionStatus.Matched || beat.Visual.Status == DecisionStatus.Matched)) Add(issues, "edit.pending_alignment", path, "An audio match cannot clear required visual alignment.");
            if (beat.Audio.Status == DecisionStatus.NeedsRecording)
            {
                Text(beat.Audio.RecordingTaskId, path + "/audio/recording_task_id", issues);
                if (context?.Tasks is { } tasks && !tasks.RecordingTasks.Any(task => task.Id == beat.Audio.RecordingTaskId && task.BeatId == beat.BeatId)) Add(issues, "edit.reference_unknown", path + "/audio/recording_task_id", "The recording task must exist for the same beat.");
            }
            if (beat.Visual.MediaId is { } image && !media.ContainsKey(image)) Add(issues, "edit.reference_unknown", path + "/visual/media_id", "The visual media snapshot does not exist.");
        }
        if (plan.Status == PlanStatus.Complete && (plan.UnresolvedRequirements.Count > 0 || plan.Beats.Any(beat => Pending(beat.Status) || Pending(beat.Audio.Status) || Pending(beat.Visual.Status)))) Add(issues, "edit.plan_incomplete", "/status", "A complete plan cannot retain unresolved requirements.");
    }

    private static void ValidateTasks(RecordingTasksDocument document, EditingValidationContext? context, List<FlowIssue> issues)
    {
        Ids(document.RecordingTasks.Select(task => task?.Id), "/recording_tasks", issues);
        var media = context?.Plan is { } currentPlan ? ValidateMedia(currentPlan.Media, context, issues) : new Dictionary<string, MediaSnapshot>();
        for (var i = 0; i < document.RecordingTasks.Count; i++)
        {
            var task = document.RecordingTasks[i]; if (task is null) continue; var path = $"/recording_tasks/{i}";
            var beat = context?.Script.Beats.FirstOrDefault(item => item.Id == task.BeatId);
            if (context is not null && beat is null) Add(issues, "edit.reference_unknown", path + "/beat_id", "The authored beat does not exist.");
            if (string.IsNullOrWhiteSpace(task.Script) && string.IsNullOrWhiteSpace(task.Brief)) Add(issues, "edit.recording_brief", path, "Recording needs a script or brief.");
            if (!Property(task.Match, "mode", out var taskMode) || taskMode.ValueKind != JsonValueKind.String ||
                (taskMode.GetString() == "exact" ? !Property(task.Match, "requested_text", out var query) || query.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(query.GetString()) :
                    taskMode.GetString() is not ("semantic" or "intent") || !Property(task.Match, "requested_meaning", out var meaning) || meaning.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(meaning.GetString())))
                Add(issues, "edit.search_query", path + "/match", "Recording tasks need their authored text/meaning matching requirement.");
            Positive(task.TargetDurationSec, path + "/target_duration_sec", issues);
            if (task.Reason == RecordingReason.NoSuitableClipFound && task.Status != TaskStatus.Resolved && context?.Plan is { } plan)
            {
                var observation = plan.Beats.FirstOrDefault(item => item.BeatId == task.BeatId)?.Audio.Search;
                if (observation?.Status != "completed" || observation.Outcome != "no_suitable_clip_found") Add(issues, "edit.search_not_completed", path + "/reason", "Only a successfully completed unsuccessful search can request this recording.");
            }
            if (task.Reason == RecordingReason.ScriptRequiresExactWording && (!Property(task.Match, "mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() != "exact")) Add(issues, "edit.recording_exact", path + "/match", "The exact-wording task must require exact matching.");
            if (task.Status == TaskStatus.Resolved)
            {
                if (task.ResolvedBy is null) Add(issues, "edit.pending_recording", path + "/resolved_by", "A resolved recording needs accepted pickup evidence.");
                else
                {
                    Interval(task.ResolvedBy.MediaId, task.ResolvedBy.SourceIn, task.ResolvedBy.SourceOut, media, path + "/resolved_by", issues);
                    if (task.ResolvedBy.Selection.ReviewStatus != ReviewStatus.Accepted) Add(issues, "edit.pickup_not_accepted", path + "/resolved_by", "Submitting a pickup is not accepting it.");
                    Match(task.ResolvedBy.Match, path + "/resolved_by/match", null, issues);
                    if (media.TryGetValue(task.ResolvedBy.MediaId, out var snapshot) && (snapshot.SourceId != task.ResolvedBy.SourceId || snapshot.Path != task.ResolvedBy.Path)) Add(issues, "edit.reference_unknown", path + "/resolved_by", "Pickup identity must match its current media snapshot.");
                    if (task.Reason == RecordingReason.ScriptRequiresExactWording && Normalize(task.Script ?? "") != Normalize(task.ResolvedBy.Transcript)) Add(issues, "edit.recording_exact", path + "/resolved_by/transcript", "The pickup does not preserve the requested wording.");
                }
            }
        }
    }

    internal static string Normalize(string value) => string.Join(" ", Regex.Matches(value.Normalize().ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(match => match.Value));
}
