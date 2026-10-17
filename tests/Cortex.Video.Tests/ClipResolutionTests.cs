using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class ClipResolutionTests
{
    [Fact]
    public async Task FindsExactAndSemanticSpeechAndCreatesOnlyTheMissingRecording()
    {
        var matcher = new Matcher();
        var result = await new ClipResolver(matcher).ResolveAsync(Script(), Media(), Transcript(), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "opening", "save", "captions" }, result.Plan.Beats.Select(b => b.BeatId));
        Assert.Equal(DecisionStatus.Matched, result.Plan.Beats[0].Audio.Status);
        Assert.Equal(DecisionStatus.Matched, result.Plan.Beats[1].Audio.Status);
        Assert.Equal(DecisionStatus.NeedsRecording, result.Plan.Beats[2].Status);
        var clip = Assert.Single(result.Plan.Beats[1].Candidates);
        Assert.Equal(3, clip.SourceIn); Assert.Equal(5, clip.SourceOut);
        Assert.Equal(TimingGranularity.Segment, clip.TimingGranularity);
        Assert.Equal(ReviewStatus.Pending, result.Plan.Beats[1].Selection!.ReviewStatus);
        var task = Assert.Single(result.Tasks.RecordingTasks);
        Assert.Equal("captions", task.BeatId); Assert.Equal("Enable captions before exporting.", task.Script);
        Assert.Equal("medium_closeup", task.Recording.Framing);
        Assert.Equal("narrator", task.Recording.Speaker);
        Assert.Equal(RecordingReason.NoSuitableClipFound, task.Reason);
        Assert.Equal(result.Plan.ResolutionId, result.Tasks.ResolutionId);
        Assert.Equal(Script().Reference.Sha256, result.Plan.ScriptRef.Sha256);
        var context = new EditingValidationContext { Script = Script().Document, ScriptReference = Script().Reference, Plan = result.Plan, Tasks = result.Tasks };
        Assert.Empty(EditingContractValidator.Validate(result.Plan, context));
        Assert.Empty(EditingContractValidator.Validate(result.Tasks, context));
        FlowDomain.Check(EditingJson.Serialize(result.Plan), EditingFlowTypes.Plan);
        FlowDomain.Check(EditingJson.Serialize(result.Tasks), EditingFlowTypes.Tasks);
        Assert.Equal(2, matcher.Calls); // Exact matching does not contact Jev.
    }

    [Fact]
    public async Task FailedOrUncertainProviderCannotClaimARecordingIsMissing()
    {
        var uncertain = await new ClipResolver(new Matcher { Uncertain = true }).ResolveAsync(Script(), Media(), Transcript(), TestContext.Current.CancellationToken);
        Assert.Empty(uncertain.Tasks.RecordingTasks);
        Assert.Equal(DecisionStatus.NeedsReview, uncertain.Plan.Beats[2].Audio.Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => new ClipResolver(new Matcher { Fail = true }).ResolveAsync(Script(), Media(), Transcript(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ClipResolver(new Matcher()).ResolveAsync(Script(), Media(), Transcript(), new(true)));
    }

    [Fact]
    public async Task WholeSegmentOverDurationLimitNeedsAlignmentRatherThanAnotherRecording()
    {
        var loaded = Script();
        loaded = loaded with { Document = loaded.Document with { Beats = [loaded.Document.Beats[0] with { Audio = loaded.Document.Beats[0].Audio with { Selection = new() { Strategy = "best_take", MaxDurationSec = 1 } } }] } };
        var result = await new ClipResolver(new Matcher()).ResolveAsync(loaded, Media(), Transcript(), TestContext.Current.CancellationToken);
        Assert.Empty(result.Tasks.RecordingTasks);
        Assert.Equal(DecisionStatus.NeedsAlignment, result.Plan.Beats[0].Audio.Status);
        Assert.Empty(result.Plan.Beats[0].Candidates);
    }

    [Fact]
    public async Task ExactModeDoesNotAcceptExtraWordsInsidePhraseAndTasksKeepStableIds()
    {
        var loaded = Script(); loaded = loaded with { Document = loaded.Document with { Beats = [loaded.Document.Beats[0]] } };
        var transcript = new TranscriptResult("", [new(0, 2, "Open all the settings.")]);
        var resolver = new ClipResolver(new Matcher());
        var first = await resolver.ResolveAsync(loaded, Media(), transcript, TestContext.Current.CancellationToken);
        var second = await resolver.ResolveAsync(loaded, Media(), transcript, TestContext.Current.CancellationToken);
        Assert.Equal(Assert.Single(first.Tasks.RecordingTasks).Id, Assert.Single(second.Tasks.RecordingTasks).Id);
        Assert.NotEqual(first.Plan.ResolutionId, second.Plan.ResolutionId);
        Assert.Equal("Open the settings.", first.Tasks.RecordingTasks[0].Brief);
    }

    [Fact]
    public async Task UnsearchedSourcesAndWrongSpeakersAreNotEvidenceOfMissingSpeech()
    {
        var loaded = Script();
        loaded = loaded with { Document = loaded.Document with { Sources = [.. loaded.Document.Sources, new() { Id = "other", Type = SourceKind.VideoFile, Path = "other.mp4", Speaker = "narrator" }] } };
        var result = await new ClipResolver(new Matcher()).ResolveAsync(loaded, Media(), Transcript(), TestContext.Current.CancellationToken);
        Assert.Empty(result.Tasks.RecordingTasks);
        Assert.All(result.Plan.Beats, b => Assert.Equal(DecisionStatus.NeedsMedia, b.Audio.Status));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ClipResolver(new Matcher()).ResolveAsync(Script(), Media() with { Speaker = "someone_else" }, Transcript(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnmeasuredWeightedQualityStaysNullAndOverlayAlignmentStaysPending()
    {
        var loaded = Script(); var beat = loaded.Document.Beats[0];
        loaded = loaded with { Document = loaded.Document with { Beats = [beat with { Audio = beat.Audio with { Selection = new() { Strategy = "best_take", Score = new Dictionary<string, double> { ["semantic_match"] = .5, ["audio_quality"] = .5 }, Prefer = ["eye_contact"] } }, Overlays = [new() { Text = "SETTINGS", Trigger = new() { SpokenPhrase = "settings" } }] }] } };
        var result = await new ClipResolver(new Matcher()).ResolveAsync(loaded, Media(), Transcript(), TestContext.Current.CancellationToken);
        var match = result.Plan.Beats[0].Candidates[0].Match;
        Assert.Equal(JsonValueKind.Null, match.GetProperty("take_score").ValueKind);
        Assert.Contains("audio_quality", match.GetProperty("unavailable_metrics").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(DecisionStatus.NeedsAlignment, result.Plan.Beats[0].Visual.Status);
        Assert.Empty(result.Tasks.RecordingTasks);
    }

    [Fact]
    public async Task ExplicitRecordingPreservesAuthoredBriefAndPerFieldProfileOverrides()
    {
        var loaded = Script();
        loaded = loaded with { Document = loaded.Document with { Beats = [loaded.Document.Beats[0] with { Audio = new() { Type = AudioKind.Record, Brief = "Introduce the channel.", Recording = new() { Delivery = "warm" } } }] } };
        var result = await new ClipResolver(new Matcher()).ResolveAsync(loaded, Media(), Transcript(), TestContext.Current.CancellationToken);
        var task = Assert.Single(result.Tasks.RecordingTasks);
        Assert.Equal(RecordingReason.ScriptRequestsRecording, task.Reason);
        Assert.Null(task.Script); Assert.Equal("Introduce the channel.", task.Brief);
        Assert.Equal("warm", task.Recording.Delivery); Assert.Equal("medium_closeup", task.Recording.Framing);
    }

    [Fact]
    public async Task InvalidTranscriptAndMismatchedSourceCannotPublishAPlan()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new ClipResolver(new Matcher()).ResolveAsync(Script(), Media(), new("", [new(0, 999, "Outside media.")]), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ClipResolver(new Matcher()).ResolveAsync(Script(), Media() with { Path = "different.mp4" }, Transcript(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingRunnerPublishesBothEditableDocumentsFromVideoAndTranscriptWires()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-resolve-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var video = Path.Combine(directory, "raw.mp4"); File.WriteAllText(video, "fixture");
            var loaded = Script(); var scriptPath = Path.Combine(directory, "script.json");
            File.WriteAllText(scriptPath, EditingJson.Serialize(loaded.Document).GetRawText());
            var graph = JsonSerializer.SerializeToElement(new { version = 4, nodes = new object[] {
                new { id = "video", kind = "videoFile", settings = new { path = video } }, new { id = "asr", kind = "transcribe" },
                new { id = "resolve", kind = "resolveClips", settings = new { scriptFile = scriptPath, sourceId = "raw" } } },
                edges = new[] { new { source = "video", sourcePort = "video", target = "asr", targetPort = "video" }, new { source = "video", sourcePort = "video", target = "resolve", targetPort = "video" }, new { source = "asr", sourcePort = "transcript", target = "resolve", targetPort = "transcript" } },
                controlEdges = new[] { new { source = "video", sourcePort = "then", target = "asr", targetPort = "exec" }, new { source = "asr", sourcePort = "then", target = "resolve", targetPort = "exec" } } });
            var handlers = VideoNodes.CreateHandlers(new Transcriber(), clipMatcher: new Matcher(), mediaReader: new Reader());
            var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "video", handlers), TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("; ", result.Steps.Select(s => s.Error)));
            Assert.Equal(new[] { "video", "asr", "resolve" }, result.Steps.Select(s => s.NodeId));
            Assert.IsType<EditPlanDocument>(EditingJson.Parse(result.Outputs["resolve"]["editPlan"].Value));
            var tasks = Assert.IsType<RecordingTasksDocument>(EditingJson.Parse(result.Outputs["resolve"]["recordingTasks"].Value));
            Assert.Single(tasks.RecordingTasks);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static LoadedEditScript Script()
    {
        EditBeat Find(string id, MatchMode mode, string query, string? fallback = null) => new() { Id = id, Audio = new() { Type = AudioKind.FindClip, Search = new() { Speaker = "narrator", Match = mode, Text = mode == MatchMode.Exact ? query : null, Meaning = mode == MatchMode.Exact ? null : query }, Fallback = new() { Type = AudioKind.Record, Script = fallback } }, Visual = new() { Type = VisualKind.Speaker, Speaker = "narrator" } };
        var script = new EditScriptDocument { Schema = EditingDocumentKinds.Script, Revision = 1, Project = new() { Id = "clip-demo", Title = "Clip resolution", Format = ProjectFormat.Horizontal, AspectRatio = "16:9", RecordingDefaults = new() { Speaker = "narrator", Framing = "medium_closeup", Orientation = "horizontal" } }, Sources = [new() { Id = "raw", Type = SourceKind.VideoFile, Path = "raw.mp4", Speaker = "narrator", Transcription = true }], Beats = [Find("opening", MatchMode.Exact, "Open the settings."), Find("save", MatchMode.Semantic, "Save your settings."), Find("captions", MatchMode.Semantic, "Enable captions.", "Enable captions before exporting.")] };
        return new(script, EditingJson.Serialize(script).GetRawText(), new() { Path = Path.Combine(Path.GetTempPath(), "script.json"), Sha256 = new string('a', 64), Revision = 1 });
    }
    internal static MediaSnapshot Media() => new() { Id = "media-raw", SourceId = "raw", Path = Path.Combine(Path.GetTempPath(), "raw.mp4"), Sha256 = new string('b', 64), Type = MediaKind.Video, Width = 1920, Height = 1080, DurationSec = 20, Fps = "25", Speaker = "narrator" };
    internal static TranscriptResult Transcript() => new("Open the settings. Save the updated preferences.", [new(0, 2, "Open the settings."), new(3, 5, "Save the updated preferences.")]);
    private sealed class Matcher : IClipMatcher
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public bool Uncertain { get; init; }
        public Task<IReadOnlyList<ClipMatchAssessment>> MatchAsync(ClipSearch search, IReadOnlyList<TakeCandidate> candidates, CancellationToken token)
        {
            Calls++; token.ThrowIfCancellationRequested(); if (Fail) throw new HttpRequestException("Provider unavailable.");
            return Task.FromResult<IReadOnlyList<ClipMatchAssessment>>(candidates.Select(c => new ClipMatchAssessment(c.Id, Uncertain ? ClipMatchOutcome.Uncertain : search.Meaning!.StartsWith("Save") && c.Text.StartsWith("Save") ? ClipMatchOutcome.Match : ClipMatchOutcome.NoMatch, search.Meaning!.StartsWith("Save") && c.Text.StartsWith("Save") ? 1 : 0, "fixture_transcript_evaluator")).ToArray());
        }
    }
    private sealed class Transcriber : IVideoTranscriber
    { public Task<TranscriptResult> TranscribeAsync(string path, CancellationToken token) => Task.FromResult(Transcript()); }
    private sealed class Reader : IVideoSnapshotReader
    { public Task<MediaSnapshot> ReadAsync(string path, EditSource source, CancellationToken token) => Task.FromResult(Media() with { Path = path }); }
}
