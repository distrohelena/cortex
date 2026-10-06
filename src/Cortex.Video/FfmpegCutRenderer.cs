using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cortex.Video;

public sealed class FfmpegCutRenderer(string ffmpegExecutable, string ffprobeExecutable, Action<string>? diagnostic = null) : IVideoCutRenderer
{
    public async Task<RenderedVideo> RenderAsync(string videoPath, TakeEditPlan plan, string outputDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(videoPath);
        if (!File.Exists(source)) throw new InvalidDataException("Video not found: " + source);
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidDataException("Select an output directory.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            var media = await Probe(source, deadline.Token);
            var timeline = CutTimeline.Build(plan, media.Duration);
            var directory = Path.Combine(Path.GetFullPath(outputDirectory), "render-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var partial = Path.Combine(directory, "cut.partial.mp4");
            var final = Path.Combine(directory, "cut.mp4");
            var manifest = Path.Combine(directory, "rendered-video.json");
            var pendingManifest = Path.Combine(directory, "rendered-video.partial.json");
            var filters = new List<string>(); var labels = new List<string>();
            for (var index = 0; index < timeline.Count; index++)
            {
                var clip = timeline[index];
                var from = clip.SourceStart.ToString("R", CultureInfo.InvariantCulture); var to = clip.SourceEnd.ToString("R", CultureInfo.InvariantCulture);
                filters.Add($"[0:v:0]trim=start={from}:end={to},setpts=PTS-STARTPTS[v{index}]");
                filters.Add($"[0:a:0]atrim=start={from}:end={to},asetpts=PTS-STARTPTS[a{index}]");
                labels.Add($"[v{index}][a{index}]");
            }
            filters.Add(string.Concat(labels) + $"concat=n={timeline.Count}:v=1:a=1[v][a]");
            diagnostic?.Invoke($"Rendering {timeline.Count} selected clips ({timeline[^1].End:F2} proposed seconds) with FFmpeg.");
            try
            {
                await Run(ffmpegExecutable, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-i", source,
                    "-filter_complex", string.Join(';', filters), "-map", "[v]", "-map", "[a]", "-r", media.Fps, "-fps_mode", "cfr",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", partial], deadline.Token);
                var output = await Probe(partial, deadline.Token);
                var rate = media.Fps.Split('/');
                var fps = double.Parse(rate[0], CultureInfo.InvariantCulture) / (rate.Length == 2 ? double.Parse(rate[1], CultureInfo.InvariantCulture) : 1);
                if (Math.Abs(output.Duration - timeline[^1].End) > .15 + timeline.Count / fps)
                    throw new InvalidDataException("Rendered duration does not match the selected intervals.");
                token.ThrowIfCancellationRequested();
                File.Move(partial, final);
                var result = new RenderedVideo(final, output.Duration, timeline);
                await File.WriteAllTextAsync(pendingManifest, JsonSerializer.Serialize(result, VideoNodes.JsonOptions), deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                File.Move(pendingManifest, manifest);
                diagnostic?.Invoke("Rendered video: " + final);
                return result;
            }
            catch
            {
                if (File.Exists(partial)) File.Delete(partial);
                if (File.Exists(final)) File.Delete(final);
                if (File.Exists(pendingManifest)) File.Delete(pendingManifest);
                if (File.Exists(manifest)) File.Delete(manifest);
                throw;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidDataException("Cut rendering exceeded the 30-minute limit."); }
    }

    private async Task<(double Duration, string Fps)> Probe(string path, CancellationToken token)
    {
        var json = await Run(ffprobeExecutable, ["-v", "error", "-show_entries", "stream=codec_type,r_frame_rate:format=duration", "-of", "json", path], token);
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (!streams.Any(stream => stream.GetProperty("codec_type").GetString() == "video") || !streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))
            throw new InvalidDataException("The video must contain both video and audio streams.");
        var duration = double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var fps = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("r_frame_rate").GetString()!;
        if (!double.IsFinite(duration) || duration <= 0 || !Regex.IsMatch(fps, @"^[1-9][0-9]*(/[1-9][0-9]*)?$"))
            throw new InvalidDataException("The video needs a positive duration and frame rate.");
        return (duration, fps);
    }

    private async Task<string> Run(string executable, string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception error) when (error is Win32Exception or FileNotFoundException)
        { throw new InvalidDataException("Could not start tool: " + executable, error); }
        var output = process.StandardOutput.ReadToEndAsync(); var errorOutput = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output, errorOutput); throw;
        }
        var details = await errorOutput;
        if (process.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(executable)} failed (exit {process.ExitCode}): {details[..Math.Min(details.Length, 8192)].Trim()}");
        return await output;
    }
}
