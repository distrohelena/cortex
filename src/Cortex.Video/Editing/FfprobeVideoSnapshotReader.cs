using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cortex.Video.Editing;

public sealed class FfprobeVideoSnapshotReader(string executable) : IVideoSnapshotReader
{
    public async Task<MediaSnapshot> ReadAsync(string path, EditSource source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var full = Path.GetFullPath(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            // Deny concurrent writes while measuring and hashing the same file bytes.
            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type,width,height,r_frame_rate:format=duration", "-of", "json", full }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Exception error) when (error is Win32Exception or FileNotFoundException) { throw new InvalidDataException("Could not start ffprobe.", error); }
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr); throw;
            }
            await stderr;
            if (process.ExitCode != 0) throw new InvalidDataException("ffprobe could not measure the connected video.");
            using var json = JsonDocument.Parse(await stdout);
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new InvalidDataException("The connected video needs an audio stream.");
            var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
            if (video.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The connected file needs a video stream.");
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, deadline.Token));
            return new() { Id = "media-" + hash[..24], SourceId = source.Id, Path = full, Sha256 = hash, Type = MediaKind.Video,
                Width = video.GetProperty("width").GetInt32(), Height = video.GetProperty("height").GetInt32(), Fps = video.GetProperty("r_frame_rate").GetString(),
                DurationSec = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), Speaker = source.Speaker };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidDataException("Video measurement exceeded the two-minute limit."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { throw new InvalidDataException("Invalid ffprobe media properties.", error); }
    }
}
