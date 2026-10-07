using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Cortex.Video;

public sealed record TranscriptionTools(string WhisperExecutable, string ModelFile,
    string FfmpegExecutable = "ffmpeg", string FfprobeExecutable = "ffprobe");

public sealed class WhisperCppTranscriber(TranscriptionTools tools, Action<string>? diagnostic = null) : IVideoTranscriber
{
    public async Task<TranscriptResult> TranscribeAsync(string videoPath, CancellationToken token)
    {
        if (!File.Exists(tools.WhisperExecutable)) throw new InvalidDataException("whisper-cli not found: " + tools.WhisperExecutable);
        if (!File.Exists(tools.ModelFile)) throw new InvalidDataException("Whisper model not found: " + tools.ModelFile);
        var directory = Path.Combine(Path.GetTempPath(), "cortex-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        try
        {
            // A successful audio extraction alone would also accept an audio-only file.
            var probe = await RunAsync(tools.FfprobeExecutable,
                ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", Path.GetFullPath(videoPath)], deadline.Token);
            using (var media = JsonDocument.Parse(probe))
            {
                var streams = media.RootElement.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_type").GetString()).ToArray();
                if (!streams.Contains("video") || !streams.Contains("audio")) throw new InvalidDataException("The file must contain both video and audio.");
            }
            var audio = Path.Combine(directory, "audio.wav");
            await RunAsync(tools.FfmpegExecutable,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", Path.GetFullPath(videoPath), "-map", "0:a:0", "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", audio], deadline.Token);
            var output = Path.Combine(directory, "whisper");
            // GPU is enabled by default in whisper.cpp; do not translate Portuguese into English.
            await RunAsync(Path.GetFullPath(tools.WhisperExecutable),
                ["-m", Path.GetFullPath(tools.ModelFile), "-f", audio, "-l", "pt", "-ojf", "-of", output], deadline.Token);
            if (!File.Exists(output + ".json")) throw new InvalidDataException("whisper-cli did not produce the expected JSON.");
            using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(output + ".json", deadline.Token));
            return ParseTranscript(raw.RootElement) with { WordTimingMethod = WhisperWordTimestamps.Method };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidDataException("Transcription exceeded the two-hour limit.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("Invalid media or transcription output.", error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static TranscriptResult ParseTranscript(JsonElement raw)
    {
        if (!raw.TryGetProperty("transcription", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Whisper JSON has no transcription array.");
        var segments = new List<TranscriptSegment>();
        var words = new List<TranscriptWord>();
        var hasTokenData = false;
        double previousStart = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("offsets", out var offsets) ||
                !offsets.TryGetProperty("from", out var from) || !from.TryGetDouble(out var startMs) ||
                !offsets.TryGetProperty("to", out var to) || !to.TryGetDouble(out var endMs) ||
                !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid Whisper segment.");
            var start = startMs / 1000;
            var end = endMs / 1000;
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < previousStart || start < 0 || end < start)
                throw new InvalidDataException("Invalid Whisper timestamps.");
            previousStart = start;
            var segment = new TranscriptSegment(start, end, text.GetString()!.Trim());
            segments.Add(segment);
            if (item.TryGetProperty("tokens", out var tokens))
            {
                hasTokenData = true;
                words.AddRange(WhisperWordTimestamps.Read(tokens, segment));
            }
        }
        return new(string.Join(" ", segments.Select(s => s.Text).Where(s => s.Length > 0)), segments)
        {
            Words = words, WordTimingMethod = hasTokenData ? WhisperWordTimestamps.Method : null
        };
    }

    private async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception error) when (error is Win32Exception or FileNotFoundException)
        {
            throw new InvalidDataException("Could not start tool: " + executable, error);
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        var details = await stderr;
        if (!string.IsNullOrWhiteSpace(details)) diagnostic?.Invoke(details.Trim());
        if (process.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(executable)} failed (exit {process.ExitCode}): {details.Trim()}");
        return await stdout;
    }
}
