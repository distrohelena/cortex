using System.Diagnostics;
using System.Security.Cryptography;
using Cortex.Video.Editing;
using Xunit;

namespace Cortex.Video.Tests;

public sealed class VideoSnapshotTests
{
    public static bool NativeToolsAvailable => CutRenderingTests.NativeToolsAvailable;
    [Fact(SkipUnless = nameof(NativeToolsAvailable), Skip = "Requires local ffmpeg and ffprobe.")]
    public async Task ActualVideoIsMeasuredAndFingerprintedWithoutChangingBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "cortex-snapshot-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=10:d=1", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=mono", "-t", "1", "-c:v", "libx264", "-c:a", "aac", "-n", path }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken); Assert.True(process.ExitCode == 0, await error);
            var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));
            var snapshot = await new FfprobeVideoSnapshotReader("ffprobe").ReadAsync(path, new() { Id = "raw", Type = SourceKind.VideoFile, Path = path, Speaker = "declared_narrator" }, TestContext.Current.CancellationToken);
            Assert.Equal(hash, snapshot.Sha256); Assert.Equal(160, snapshot.Width); Assert.Equal(90, snapshot.Height);
            Assert.Equal("10/1", snapshot.Fps); Assert.InRange(snapshot.DurationSec!.Value, .99, 1.1);
            Assert.Equal("declared_narrator", snapshot.Speaker);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public async Task CancelledProbeNeverStartsATool()
        => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfprobeVideoSnapshotReader("unused").ReadAsync("unused", new() { Id = "raw", Type = SourceKind.VideoFile, Path = "unused" }, new(true)));
}
