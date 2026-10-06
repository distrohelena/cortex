using Cortex.Core.Flows;
using static Cortex.Video.Editing.EditingContractValidator;

namespace Cortex.Video.Editing;

internal static class EditingTimelineValidation
{
    internal static void Validate(ResolvedEditPlanDocument document, EditingValidationContext? context, List<FlowIssue> issues)
    {
        if (document.Status != "resolved" || document.Timebase != "seconds") Add(issues, "edit.timeline_status", "/status", "Use resolved status and seconds for a complete timeline.");
        var media = ValidateMedia(document.Media, context, issues);
        Ids(document.Timeline.Select(item => item?.BeatId), "/timeline", issues);
        if (context is not null && !document.Timeline.Select(item => item.BeatId).SequenceEqual(context.Script.Beats.Select(beat => beat.Id))) Add(issues, "edit.beat_order", "/timeline", "A final timeline must include every required beat in authored order.");
        if (context?.Plan is { } plan && (plan.Status != PlanStatus.Complete || plan.UnresolvedRequirements.Count > 0 || plan.Beats.Any(beat => Pending(beat.Status) || Pending(beat.Visual.Status) || Pending(beat.Audio.Status)))) Add(issues, "edit.plan_incomplete", "/timeline", "The current plan still has unresolved requirements.");
        if (context?.Tasks is { } tasks && tasks.RecordingTasks.Any(task => task.Status != TaskStatus.Resolved || task.ResolvedBy is null)) Add(issues, "edit.pending_recording", "/recording_task_resolutions", "Required recordings must be resolved in the current task snapshot.");
        double cursor = 0;
        for (var i = 0; i < document.Timeline.Count; i++)
        {
            var item = document.Timeline[i]; if (item is null) continue; var path = $"/timeline/{i}";
            if (!Same(item.Start, cursor)) Add(issues, "edit.timeline_gap", path + "/start", "Sequence items must be contiguous from zero, without hidden gaps or overlaps.");
            if (!double.IsFinite(item.Start) || !double.IsFinite(item.End) || item.Start < 0 || item.End <= item.Start) Add(issues, "edit.interval_invalid", path, "Sequence intervals must be finite and positive.");
            var duration = item.End - item.Start; cursor = item.End;
            Binding(item.Audio, true, duration, media, path + "/audio", issues); Binding(item.Video, false, duration, media, path + "/video", issues);
            if (item.Enter != "cut" || item.Exit != "cut") Add(issues, "edit.transition", path, "This contract version supports cut transitions.");
            var beat = context?.Script.Beats.FirstOrDefault(beat => beat.Id == item.BeatId);
            if (beat?.Audio.Type == AudioKind.None && (item.Audio.Kind != BindingKind.None || !Same(duration, beat.Visual.DurationSec ?? double.NaN))) Add(issues, "edit.timeline_duration", path, "Intentional silence and authored display duration must be preserved.");
            if (beat?.Visual.Type is VisualKind.Speaker or VisualKind.SpeakerWithOverlay &&
                (item.Video.Kind != BindingKind.Clip || item.Audio.Kind != BindingKind.Clip || item.Video.MediaId != item.Audio.MediaId || !Same(item.Video.SourceIn ?? double.NaN, item.Audio.SourceIn ?? double.NaN) || !Same(item.Video.SourceOut ?? double.NaN, item.Audio.SourceOut ?? double.NaN))) Add(issues, "edit.av_sync", path, "Linked speaker audio and video must use the same source intervals.");
            var decision = context?.Plan?.Beats.FirstOrDefault(beat => beat.BeatId == item.BeatId);
            var candidate = decision?.Candidates.FirstOrDefault(candidate => candidate.Id == decision.SelectedCandidateId);
            if (candidate is not null && item.Audio.Kind == BindingKind.Clip && (candidate.MediaId != item.Audio.MediaId || !Same(candidate.SourceIn, item.Audio.SourceIn ?? double.NaN) || !Same(candidate.SourceOut, item.Audio.SourceOut ?? double.NaN))) Add(issues, "edit.selection_changed", path + "/audio", "The timeline differs from the pinned selected candidate.");
            foreach (var overlay in item.Overlays ?? [])
                if (overlay.Trigger is { } trigger && (trigger.Resolved is null || trigger.Resolved.TimingGranularity != TimingGranularity.Word || !double.IsFinite(trigger.Resolved.OffsetSec) || trigger.Resolved.OffsetSec < 0 || trigger.Resolved.OffsetSec >= duration)) Add(issues, "edit.pending_alignment", path + "/overlays", "Spoken triggers need real in-beat word alignment before rendering.");
        }
        if (!Same(cursor, document.DurationSec) || cursor <= 0) Add(issues, "edit.timeline_duration", "/duration_sec", "Duration must equal the complete derived sequence, without implicit padding.");
        if (document.TargetDurationSec is { } target && !Same(target, cursor) && !(document.Notices?.Any(notice => notice.Code is "below_target_duration" or "above_target_duration") ?? false)) Add(issues, "edit.target_deviation", "/notices", "Report target-duration deviation rather than padding or truncating implicitly.");
        Ids(document.RecordingTaskResolutions.Select(task => task.RecordingTask), "/recording_task_resolutions", issues);
        if (context?.Tasks is { } currentTasks)
            foreach (var task in currentTasks.RecordingTasks)
                {
                    var resolution = document.RecordingTaskResolutions.FirstOrDefault(value => value.RecordingTask == task.Id);
                    if (resolution is null || task.ResolvedBy is null || resolution.Status != TaskStatus.Resolved || resolution.ResolvedBy.MediaId != task.ResolvedBy.MediaId || !Same(resolution.ResolvedBy.SourceIn, task.ResolvedBy.SourceIn) || !Same(resolution.ResolvedBy.SourceOut, task.ResolvedBy.SourceOut)) Add(issues, "edit.pending_recording", "/recording_task_resolutions", "Carry every accepted recording resolution from the current task snapshot.");
                }
    }

    private static void Binding(MediaBinding binding, bool audio, double duration, IReadOnlyDictionary<string, MediaSnapshot> media, string path, List<FlowIssue> issues)
    {
        if (binding.Kind == BindingKind.Clip)
        {
            if (binding.MediaId is null || binding.SourceIn is null || binding.SourceOut is null) Add(issues, "edit.binding_fields", path, "A moving clip needs concrete media and source in/out.");
            else
            {
                Interval(binding.MediaId, binding.SourceIn.Value, binding.SourceOut.Value, media, path, issues);
                if (!Same(binding.SourceOut.Value - binding.SourceIn.Value, duration)) Add(issues, "edit.timeline_duration", path, "Clip source duration must match its sequence duration.");
            }
            if (binding.Recipe is not null) Add(issues, "edit.binding_fields", path, "A clip is not a procedural recipe.");
        }
        else
        {
            if (binding.SourceIn is not null || binding.SourceOut is not null) Add(issues, "edit.binding_fields", path, "Stills/procedural visuals/silence do not have moving source clocks.");
            if (binding.Kind == BindingKind.Image)
            { if (audio || binding.MediaId is null || !media.TryGetValue(binding.MediaId, out var image) || image.Type != MediaKind.Image || binding.Recipe is not null) Add(issues, "edit.binding_fields", path, "An image binding must reference an image snapshot."); }
            else if (binding.Kind == BindingKind.None)
            { if (!audio || binding.MediaId is not null || binding.Recipe is not null || binding.Focus is not null) Add(issues, "edit.binding_fields", path, "None is intentional audio silence without media."); }
            else if (audio || binding.MediaId is not null || binding.Recipe?.ValueKind != System.Text.Json.JsonValueKind.Object) Add(issues, "edit.binding_fields", path, "A procedural visual requires an explicit recipe, not fabricated media.");
        }
    }
}
