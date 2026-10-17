using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class CutRenderingTests
{
    public static bool NativeToolsAvailable => new[] { "ffmpeg", "ffprobe" }.All(tool =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory.Trim('"'), tool + (OperatingSystem.IsWindows() ? ".exe" : "")))));

    [Fact]
    public void ManualKeepEditsRebuildTimelineAndPreserveCandidateOrder()
    {
        var cuts = CutTimeline.Build(Plan(), 4);
        Assert.Equal(new[] { "blue", "red" }, cuts.Select(cut => cut.CandidateId));
        Assert.Equal(new ProposedTakeClip("blue", 2, 3, 0, 1), cuts[0]);
        Assert.Equal(new ProposedTakeClip("red", 0, 1, 1, 2), cuts[1]);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 5)]
    [InlineData(2, 2)]
    [InlineData(double.NaN, 1)]
    public void InvalidSelectedIntervalsAreRejected(double start, double end)
    {
        var plan = Plan() with { Candidates = [new("invalid", new(start, end, []), "", Keep: true)] };
        Assert.Throws<InvalidDataException>(() => CutTimeline.Build(plan, 4));
    }

    [Fact]
    public void EmptySelectionFailsRatherThanProducingAnEmptyMovie()
    {
        var plan = Plan() with { Candidates = Plan().Candidates.Select(candidate => candidate with { Keep = false }).ToArray() };
        Assert.Throws<InvalidDataException>(() => CutTimeline.Build(plan, 4));
    }

    [Fact]
    public async Task SavedEditableJsonRunsThroughTheExistingTypedGraph()
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-cut-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "video.mp4"); File.WriteAllText(source, "fixture");
            var planFile = Path.Combine(root, "plan.json"); File.WriteAllText(planFile, JsonSerializer.Serialize(Plan(), VideoNodes.JsonOptions));
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new object[] { new { id = "video", kind = "videoFile", settings = new { path = source } },
                    new { id = "render", kind = "renderCuts", settings = new { editPlanFile = planFile, outputDirectory = root } } },
                edges = new[] { new { source = "video", sourcePort = "video", target = "render", targetPort = "video" } },
                controlEdges = new[] { new { source = "video", sourcePort = "then", target = "render", targetPort = "exec" } }
            });
            var renderer = new RecordingRenderer();
            var flow = TypedFlowCompiler.Compile(graph, "video", VideoNodes.CreateHandlers(new UnusedTranscriber(), renderer: renderer));
            var result = await TypedFlowRunner.RunAsync(flow, TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("; ", result.Steps.Select(step => step.Error)));
            Assert.Equal(new[] { "video", "render" }, result.Steps.Select(step => step.NodeId));
            Assert.Equal(3, renderer.Received!.Candidates.Count);
            FlowDomain.Check(result.Outputs["render"]["video"].Value, RenderCutsNode.RenderedVideoType);
            Assert.Equal(2, result.Outputs["render"]["video"].Value.GetProperty("duration").GetDouble());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact(SkipUnless = nameof(NativeToolsAvailable), Skip = "Requires local ffmpeg and ffprobe executables.")]
    public async Task ActualFfmpegCutsSelectedFramesInEditedOrderAndPreservesAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex cuts " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source ç.mp4");
            await Tool("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "color=c=red:s=64x48:r=10:d=1",
                "-f", "lavfi", "-i", "color=c=green:s=64x48:r=10:d=1", "-f", "lavfi", "-i", "color=c=blue:s=64x48:r=10:d=1",
                "-f", "lavfi", "-i", "color=c=white:s=64x48:r=10:d=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-filter_complex", "[0:v][1:v][2:v][3:v]concat=n=4:v=1:a=0[v]", "-map", "[v]", "-map", "4:a", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
            var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
            var renderer = new FfmpegCutRenderer("ffmpeg", "ffprobe");
            var result = await renderer.RenderAsync(source, Plan(), Path.Combine(root, "output"), TestContext.Current.CancellationToken);
            Assert.True(File.Exists(result.Path));
            Assert.InRange(result.Duration, 1.9, 2.15);
            var blue = await Pixel(result.Path, "0", root); var red = await Pixel(result.Path, "1.2", root);
            Assert.InRange(blue[2], 200, 255); Assert.InRange(blue[0], 0, 40);
            Assert.InRange(red[0], 200, 255); Assert.InRange(red[2], 0, 40);
            using var media = JsonDocument.Parse(await Tool("ffprobe", ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", result.Path]));
            Assert.Equal(new[] { "video", "audio" }, media.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString()));
            var pcm = Path.Combine(root, "audio.raw");
            await Tool("ffmpeg", ["-v", "error", "-i", result.Path, "-map", "0:a:0", "-f", "f32le", pcm]);
            var audio = File.ReadAllBytes(pcm);
            Assert.True(Enumerable.Range(0, audio.Length / 4).Average(i => Math.Abs(BitConverter.ToSingle(audio, i * 4))) > .03);
            var again = await renderer.RenderAsync(source, Plan(), Path.Combine(root, "output"), TestContext.Current.CancellationToken);
            Assert.NotEqual(result.Path, again.Path);
            Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectedPlanRunsDirectlyButRejectsConflictingFile(bool alsoFile)
    {
        var settings = new Dictionary<string, string> { ["outputDirectory"] = Path.GetTempPath() };
        if (alsoFile) settings["editPlanFile"] = "conflicting.json";
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[] { new { id = "source", kind = "saved" }, new { id = "render", kind = "renderCuts", settings } },
            edges = new[] { new { source = "source", sourcePort = "video", target = "render", targetPort = "video" }, new { source = "source", sourcePort = "plan", target = "render", targetPort = "editPlan" } },
            controlEdges = new[] { new { source = "source", sourcePort = "then", target = "render", targetPort = "exec" } }
        });
        var renderer = new RecordingRenderer();
        var entry = new FlowNodeHandler(new NodeDefinition("saved", 1, FlowEffect.Pure,
            dataOutputs: [new("video", VideoNodes.VideoType), new("plan", TakeSuggestionNode.EditPlanType)], controlOutputs: [new("then")]),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>
            {
                ["video"] = JsonSerializer.SerializeToElement(new { path = "fixture.mp4" }),
                ["plan"] = JsonSerializer.SerializeToElement(Plan(), VideoNodes.JsonOptions)
            }), isEntry: true);
        var result = await TypedFlowRunner.RunAsync(TypedFlowCompiler.Compile(graph, "source", [entry, RenderCutsNode.Create(renderer)]), TestContext.Current.CancellationToken);
        Assert.Equal(!alsoFile, result.Success);
        if (alsoFile) Assert.Null(renderer.Received);
        else Assert.NotNull(renderer.Received);
    }

    [Fact]
    public async Task CancelledRenderDoesNotCreateOutput()
    {
        var output = Path.Combine(Path.GetTempPath(), "cortex-cancelled-cut-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfmpegCutRenderer("ffmpeg", "ffprobe").RenderAsync("unused", Plan(), output, new(true)));
        Assert.False(Directory.Exists(output));
    }

    static TakeEditPlan Plan() => new(1, "draft", "sourceSeconds", "transcriptOnly", "Test", "fixture", "fixture", "fixture",
        [new("blue", new(2, 3, [2]), "Blue", Assessment: new(3, 3), Keep: true),
            new("green", new(1, 2, [1]), "Green", Assessment: new(3, 3)),
            new("red", new(0, 1, [0]), "Red", Assessment: new(3, 3), Keep: true)],
        [], [], [new("stale", 99, 100, 0, 1)]);

    static async Task<byte[]> Pixel(string video, string at, string root)
    {
        var path = Path.Combine(root, "pixel-" + at + ".rgb");
        await Tool("ffmpeg", ["-v", "error", "-ss", at, "-i", video, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", path]);
        var bytes = File.ReadAllBytes(path); return bytes.Skip((24 * 64 + 32) * 3).Take(3).ToArray();
    }

    static async Task<string> Tool(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await error); return await output;
    }

    sealed class RecordingRenderer : IVideoCutRenderer
    {
        public TakeEditPlan? Received { get; private set; }
        public Task<RenderedVideo> RenderAsync(string videoPath, TakeEditPlan plan, string outputDirectory, CancellationToken token)
        { Received = plan; return Task.FromResult(new RenderedVideo(Path.Combine(outputDirectory, "fixture.mp4"), 2, CutTimeline.Build(plan, 4))); }
    }

    sealed class UnusedTranscriber : IVideoTranscriber
    {
        public Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token) => throw new InvalidOperationException("Transcription must not run when rendering a saved plan.");
    }
}
