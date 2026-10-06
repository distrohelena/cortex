using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cortex.Video.Editing;

/// <summary>Resolves editorial speech against one explicitly bound, transcribed video.</summary>
public sealed class ClipResolver(IClipMatcher matcher)
{
    public async Task<ClipResolution> ResolveAsync(LoadedEditScript loaded, MediaSnapshot media, TranscriptResult transcript, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var script = loaded.Document;
        EditingContractValidator.ThrowIfInvalid(script);
        if (script.Beats.Count > 32) throw new InvalidDataException("Resolve Clips supports at most 32 beats in this increment.");
        var source = script.Sources.SingleOrDefault(s => s.Id == media.SourceId) ?? throw new InvalidDataException("Select a source declared in the script.");
        if (source.Type != SourceKind.VideoFile || media.Type != MediaKind.Video || !SamePath(loaded.ResolveLocalPath(source.Path), media.Path) || source.Speaker != media.Speaker)
            throw new InvalidDataException("The connected video must match the selected video_file path and declared speaker.");
        var context = new EditingValidationContext { Script = script, ScriptReference = loaded.Reference };
        var mediaIssues = new List<Cortex.Core.Flows.FlowIssue>();
        EditingContractValidator.ValidateMedia([media], context, mediaIssues);
        if (mediaIssues.Count > 0) throw new InvalidDataException(string.Join("; ", mediaIssues.Select(i => i.Message)));
        if (transcript.Segments is null || transcript.Segments.Any(s => s is null || s.End > media.DurationSec) || transcript.Segments.Sum(s => (long)(s.Text?.Length ?? 0)) > 65536)
            throw new InvalidDataException("The transcript must fit this video and contain at most 65536 characters.");
        var candidates = TakeCandidates.Build(transcript);
        var decisions = new List<BeatDecision>(); var tasks = new List<RecordingTask>(); var requirements = new List<JsonElement>();
        var resolution = "resolution-" + Guid.NewGuid().ToString("N");
        var transcriptHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(transcript, VideoNodes.JsonOptions)));
        foreach (var beat in script.Beats)
        {
            token.ThrowIfCancellationRequested();
            var audio = beat.Audio; var found = new List<ClipCandidate>(); string? selected = null; RecordingTask? task = null;
            AudioDecision audioDecision;
            if (audio.Type == AudioKind.None) audioDecision = new() { Status = DecisionStatus.IntentionalNone };
            else if (audio.Type == AudioKind.Record)
            {
                task = RecordTask(beat, null); tasks.Add(task);
                audioDecision = new() { Status = DecisionStatus.NeedsRecording, RecordingTaskId = task.Id };
            }
            else
            {
                var search = audio.Search!;
                if ((search.SourceId is not null && search.SourceId != source.Id) || search.Speaker != source.Speaker ||
                    (search.SourceId is null && script.Sources.Any(s => s.Id != source.Id && s.Type != SourceKind.Image && (s.Speaker is null || s.Speaker == search.Speaker))))
                {
                    audioDecision = new() { Status = DecisionStatus.NeedsMedia, Search = new("not_completed", "source_not_searched", "single_video_scope") };
                    requirements.Add(Requirement(beat.Id, "source_not_searched", DecisionStatus.NeedsMedia, "Connect the requested source; this increment searches one declared video_file only."));
                }
                else
                {
                    var query = search.Text ?? search.Meaning!;
                    if (query.Length > 2000 || string.IsNullOrWhiteSpace(EditingContractValidator.Normalize(query))) throw new InvalidDataException("Use a speech query of 1-2000 characters containing words.");
                    IReadOnlyList<ClipMatchAssessment> assessed = search.Match == MatchMode.Exact
                        ? candidates.Select(c => new ClipMatchAssessment(c.Id, (" " + EditingContractValidator.Normalize(c.Text) + " ").Contains(" " + EditingContractValidator.Normalize(query) + " ", StringComparison.Ordinal) ? ClipMatchOutcome.Match : ClipMatchOutcome.NoMatch, 1, "normalized_exact_words")).ToArray()
                        : candidates.Count == 0 ? [] : await matcher.MatchAsync(search, candidates, token);
                    if (assessed.Count != candidates.Count || assessed.Select(a => a.CandidateId).Distinct(StringComparer.Ordinal).Count() != candidates.Count || assessed.Any(a => !candidates.Any(c => c.Id == a.CandidateId) || !Enum.IsDefined(a.Outcome) || !double.IsFinite(a.Score) || a.Score < 0 || a.Score > 1 || string.IsNullOrWhiteSpace(a.Method)))
                        throw new InvalidDataException("The matcher must return one valid assessment for every candidate.");
                    var matches = assessed.Where(a => a.Outcome == ClipMatchOutcome.Match).Select(a => (Assessment: a, Clip: candidates.Single(c => c.Id == a.CandidateId))).ToArray();
                    foreach (var pair in matches.Where(p => audio.Selection?.MaxDurationSec is not { } maximum || p.Clip.Source.End - p.Clip.Source.Start <= maximum))
                        found.Add(new() { Id = pair.Clip.Id, MediaId = media.Id, SourceIn = pair.Clip.Source.Start, SourceOut = pair.Clip.Source.End, Transcript = pair.Clip.Text, Speaker = source.Speaker, TimingGranularity = TimingGranularity.Segment, Rank = 1,
                            Match = Evidence(search, audio.Selection, pair.Assessment, pair.Clip, transcriptHash), Reason = "Compatible speech in whole transcript segments; audio and visual quality require human review." });
                    found = found.OrderByDescending(c => c.Match.GetProperty("take_score").ValueKind == JsonValueKind.Number ? c.Match.GetProperty("take_score").GetDouble() : assessed.Single(a => a.CandidateId == c.Id).Score)
                        .ThenBy(c => c.SourceOut - c.SourceIn).ThenBy(c => c.SourceIn).Select((c, i) => c with { Rank = i + 1 }).ToList();
                    if (found.Count > 0)
                    {
                        selected = found[0].Id;
                        audioDecision = new() { Status = DecisionStatus.Matched, Search = new("completed", "suitable_clip_found", found[0].Match.GetProperty("method").GetString()!) };
                    }
                    else if (matches.Length > 0)
                    {
                        audioDecision = new() { Status = DecisionStatus.NeedsAlignment, Search = new("completed", "matching_speech_requires_alignment", "segment_timing") };
                        requirements.Add(Requirement(beat.Id, "clip_duration_alignment", DecisionStatus.NeedsAlignment, "Matching speech exists but its whole segments exceed max_duration_sec. No shorter timecodes were invented."));
                    }
                    else if (assessed.Any(a => a.Outcome == ClipMatchOutcome.Uncertain))
                    {
                        audioDecision = new() { Status = DecisionStatus.NeedsReview, Search = new("completed", "inconclusive", "transcript_only") };
                        requirements.Add(Requirement(beat.Id, "inconclusive_match", DecisionStatus.NeedsReview, "The evaluator could not confidently accept or reject the available speech."));
                    }
                    else
                    {
                        if (audio.Fallback is not null) { task = RecordTask(beat, search); tasks.Add(task); }
                        audioDecision = new() { Status = task is null ? DecisionStatus.NeedsMedia : DecisionStatus.NeedsRecording, RecordingTaskId = task?.Id, Search = new("completed", "no_suitable_clip_found", search.Match == MatchMode.Exact ? "normalized_exact_words" : "transcript_only") };
                        if (task is null) requirements.Add(Requirement(beat.Id, "no_matching_clip", DecisionStatus.NeedsMedia, "No suitable clip was found and the author supplied no recording fallback."));
                    }
                }
            }
            var visualRequirements = new List<JsonElement>();
            var speakerVisual = beat.Visual.Type is VisualKind.Speaker or VisualKind.SpeakerWithOverlay;
            var visualStatus = speakerVisual && selected is not null && (beat.Visual.Speaker is null || beat.Visual.Speaker == media.Speaker) ? DecisionStatus.NeedsReview : DecisionStatus.NeedsMedia;
            if (visualStatus == DecisionStatus.NeedsReview) visualRequirements.Add(Requirement(beat.Id, "selection_review", visualStatus, "Review the proposed segment, delivery, framing and media quality before accepting it."));
            else visualRequirements.Add(Requirement(beat.Id, "visual_not_resolved", visualStatus, "This node resolves speech only; the requested visual remains pending."));
            if (beat.Visual.Focus is not null) visualRequirements.Add(Requirement(beat.Id, "focus_not_resolved", DecisionStatus.NeedsReview, "The authored focus/crop request has not been analysed."));
            var overlays = (beat.Overlays ?? []).Concat(beat.Overlay is null ? [] : [beat.Overlay]).ToArray();
            if (overlays.Any(o => o.Trigger is not null))
            {
                visualStatus = DecisionStatus.NeedsAlignment;
                visualRequirements.Add(Requirement(beat.Id, "overlay_word_alignment", visualStatus, "Spoken overlay triggers require real word alignment; segment times cannot locate individual words."));
            }
            if (overlays.Length > 0) visualRequirements.Add(Requirement(beat.Id, "overlay_rendering_pending", DecisionStatus.NeedsReview, "Overlays are preserved as intent and have not been rendered."));
            requirements.AddRange(visualRequirements);
            var visual = new VisualDecision { Status = visualStatus, Intent = beat.Visual, MediaId = speakerVisual && selected is not null ? media.Id : null, Requirements = visualRequirements };
            var status = audioDecision.Status is not (DecisionStatus.Matched or DecisionStatus.IntentionalNone) ? audioDecision.Status : visualStatus;
            decisions.Add(new() { BeatId = beat.Id, Status = status, Audio = audioDecision, Visual = visual, Candidates = found, SelectedCandidateId = selected, Selection = selected is null ? null : new("resolver", ReviewStatus.Pending) });
        }
        token.ThrowIfCancellationRequested();
        var plan = new EditPlanDocument { Schema = EditingDocumentKinds.Plan, ProjectId = script.Project.Id, ScriptRef = loaded.Reference, ResolutionId = resolution, Status = PlanStatus.Incomplete, Media = [media], Beats = decisions, UnresolvedRequirements = requirements };
        var taskDocument = new RecordingTasksDocument { Schema = EditingDocumentKinds.Tasks, ProjectId = script.Project.Id, ScriptRef = loaded.Reference, ResolutionId = resolution, RecordingTasks = tasks };
        context = context with { Plan = plan, Tasks = taskDocument, MediaHashes = new Dictionary<string, string> { [media.Id] = media.Sha256 } };
        EditingContractValidator.ThrowIfInvalid(plan, context); EditingContractValidator.ThrowIfInvalid(taskDocument, context);
        return new(plan, taskDocument);

        RecordingTask RecordTask(EditBeat beat, ClipSearch? search)
        {
            var audio = beat.Audio; var fallback = audio.Fallback;
            var wording = search is null ? audio.Script : fallback?.Script;
            var brief = search is null ? audio.Brief : fallback?.Brief ?? (wording is null ? search.Text ?? search.Meaning : null);
            var exact = search?.Match == MatchMode.Exact || search is null && audio.RequireExactWording == true;
            if (exact && search is null && string.IsNullOrWhiteSpace(wording)) throw new InvalidDataException("An exact recording needs an authored script.");
            var profile = search is null ? audio.Recording : fallback?.Recording;
            var defaults = script.Project.RecordingDefaults;
            var identity = JsonSerializer.Serialize(new[] { script.Project.Id, beat.Id });
            return new() { Id = "record-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24], BeatId = beat.Id,
                Reason = search is not null ? RecordingReason.NoSuitableClipFound : exact ? RecordingReason.ScriptRequiresExactWording : RecordingReason.ScriptRequestsRecording,
                Script = wording, Brief = brief, TargetDurationSec = search is null ? audio.TargetDurationSec : fallback?.TargetDurationSec,
                Match = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["mode"] = exact ? "exact" : search is null ? "intent" : Mode(search.Match), [exact ? "requested_text" : "requested_meaning"] = search?.Text ?? search?.Meaning ?? wording ?? brief }),
                Recording = new() { Speaker = profile?.Speaker ?? search?.Speaker ?? defaults?.Speaker ?? beat.Visual.Speaker, Framing = profile?.Framing ?? defaults?.Framing, Orientation = profile?.Orientation ?? defaults?.Orientation, Delivery = profile?.Delivery ?? defaults?.Delivery }, Status = TaskStatus.NeedsRecording };
        }
    }

    private static JsonElement Evidence(ClipSearch search, ClipSelection? selection, ClipMatchAssessment assessment, TakeCandidate candidate, string transcriptHash)
    {
        var metrics = new Dictionary<string, double> { ["semantic_match"] = assessment.Score };
        if (selection?.MaxDurationSec is { } limit) metrics["brevity"] = Math.Max(0, 1 - (candidate.Source.End - candidate.Source.Start) / limit);
        var unavailable = EditingContractValidator.Metrics.Where(m => !metrics.ContainsKey(m)).Order(StringComparer.Ordinal).ToArray();
        var preferences = selection?.Prefer ?? [];
        double? total = selection?.Score is { } weights && weights.All(w => w.Value == 0 || metrics.ContainsKey(w.Key)) ? weights.Sum(w => w.Value == 0 ? 0 : w.Value * metrics[w.Key]) : null;
        return JsonSerializer.SerializeToElement(new { mode = Mode(search.Match), method = assessment.Method, basis = "transcript_only", requested_text = search.Text, requested_meaning = search.Meaning,
            semantic_score = search.Match == MatchMode.Exact ? (double?)null : assessment.Score, take_score = total, evaluated_metrics = metrics, unavailable_metrics = unavailable, unavailable_preferences = preferences,
            ranking_basis = total is null ? "text_match_then_shorter_segment_then_source_order" : "authored_weights_then_shorter_segment_then_source_order", transcript_sha256 = transcriptHash, speaker_basis = "source_declaration" });
    }
    private static string Mode(MatchMode mode) => JsonNamingPolicy.SnakeCaseLower.ConvertName(mode.ToString());
    private static JsonElement Requirement(string beat, string code, DecisionStatus status, string message) => JsonSerializer.SerializeToElement(new { beat_id = beat, code, status = JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString()), message });
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
