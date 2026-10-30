using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Cortex.TestSupport.LocalModels;
using Xunit;

namespace Cortex.Inference.Tests;

public sealed class LocalModelCacheTests : IDisposable
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("pinned local model bytes " + new string('x', 70_000));
    private static readonly string PayloadSha = Convert.ToHexStringLower(SHA256.HashData(Payload));
    private readonly string root = Path.Combine(Path.GetTempPath(), "cortex-local-model-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Valid_download_is_hash_checked_and_renamed()
    {
        await using var server = FileServer.Start(Payload);
        var path = await new LocalModelCache(root).EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("models", "a.gguf"), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(root, "models", "a.gguf"), path);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".partial"));
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task Hash_mismatch_deletes_partial_and_throws()
    {
        await using var server = FileServer.Start(Payload);
        var wrong = new string('0', 64);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LocalModelCache(root).EnsureFileAsync(server.Uri, wrong, Path.Combine("models", "a.gguf"), TestContext.Current.CancellationToken));

        Assert.Contains(PayloadSha, error.Message);
        Assert.Contains(wrong, error.Message);
        Assert.False(File.Exists(Path.Combine(root, "models", "a.gguf")));
        Assert.False(File.Exists(Path.Combine(root, "models", "a.gguf.partial")));
    }

    [Fact]
    public async Task Existing_valid_file_is_not_downloaded_again()
    {
        await using var server = FileServer.Start(Payload);
        var cache = new LocalModelCache(root);
        var first = await cache.EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("models", "a.gguf"), TestContext.Current.CancellationToken);
        var second = await cache.EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("models", "a.gguf"), TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task Interrupted_partial_is_replaced_on_next_run()
    {
        await using var server = FileServer.Start(Payload);
        var partial = Path.Combine(root, "models", "a.gguf.partial");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        await File.WriteAllBytesAsync(partial, Payload.AsMemory(0, 1000).ToArray(), TestContext.Current.CancellationToken);

        var path = await new LocalModelCache(root).EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("models", "a.gguf"), TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(partial));
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task Cancelled_download_leaves_no_partial_or_file()
    {
        await using var server = FileServer.Start(Payload, stallAfter: 1000);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalModelCache(root).EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("models", "a.gguf"), cancel.Token));

        Assert.False(File.Exists(Path.Combine(root, "models", "a.gguf")));
        Assert.False(File.Exists(Path.Combine(root, "models", "a.gguf.partial")));
    }

    [Fact]
    public async Task Relative_path_escaping_the_cache_root_is_rejected()
    {
        await using var server = FileServer.Start(Payload);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new LocalModelCache(root).EnsureFileAsync(server.Uri, PayloadSha, Path.Combine("..", "escape.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public void Pinned_manifest_loads_with_every_model_and_hash()
    {
        var manifest = LocalModelManifest.Load();

        Assert.StartsWith("b", manifest.LlamaCpp.Tag);
        Assert.Equal(2, manifest.LlamaCpp.Assets.Count);
        Assert.Equal(["minicpm5-2b", "qwen3-0.6b", "qwen3.5-4b"], manifest.Gguf.Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("laya[serve]==", manifest.Laya.Package);
        Assert.Matches("^[0-9a-f]{40}$", manifest.Laya.ModelRevision);
    }

    [Fact]
    public void Malformed_manifest_hash_is_rejected()
    {
        const string json = """
            {"llamaCpp":{"tag":"b1","assets":[{"url":"https://example.com/a.zip","sha256":"NOT-A-HASH"}]},
             "gguf":{},"laya":{"package":"laya[serve]==1.0","modelRevision":"0000000000000000000000000000000000000000","torch":"torch==1","torchIndexUrl":"https://example.com/"}}
            """;
        Assert.Throws<InvalidDataException>(() => LocalModelManifest.Parse(json));
    }

    [Fact]
    public async Task Readiness_failure_reports_output_tail_and_kills_process()
    {
        var port = LocalServerProcess.FreeLoopbackPort();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LocalServerProcess.StartAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "Write-Output 'model load failed: boom'; exit 3"],
            new Dictionary<string, string>(), new Uri($"http://127.0.0.1:{port}/health"), TimeSpan.FromSeconds(60),
            TestContext.Current.CancellationToken));

        Assert.Contains("model load failed: boom", error.Message);
        Assert.Contains("exited with code 3", error.Message);
    }

    [Fact]
    public async Task Readiness_timeout_kills_process_and_reports()
    {
        var port = LocalServerProcess.FreeLoopbackPort();
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<TimeoutException>(() => LocalServerProcess.StartAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "Write-Output 'still loading'; Start-Sleep 600"],
            new Dictionary<string, string>(), new Uri($"http://127.0.0.1:{port}/health"), TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));

        Assert.Contains("still loading", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Disposing_server_kills_process_tree_and_frees_port()
    {
        var port = LocalServerProcess.FreeLoopbackPort();
        // The listener lives in a grandchild, as a pip console-script launcher would spawn python.exe.
        var inner = $"$l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, {port}); $l.Start(); Start-Sleep 600";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(inner));
        var server = await LocalServerProcess.StartAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", $"& powershell.exe -NoProfile -NonInteractive -EncodedCommand {encoded}"],
            new Dictionary<string, string>(), null, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var process = server.Process;
        try
        {
            await WaitUntilListeningAsync(port, TimeSpan.FromSeconds(60));
            Assert.False(process.HasExited);
        }
        finally
        {
            // A survivor holding the inherited stdout/stderr pipes would make disposal hang, so bound it.
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        Assert.True(process.HasExited);
        var rebound = new TcpListener(IPAddress.Loopback, port);
        rebound.Start();
        rebound.Stop();
    }

    private static async Task WaitUntilListeningAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
                return;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }
        }
    }

    private sealed class FileServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly byte[] payload;
        private readonly int stallAfter;
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        private int requests;

        private FileServer(byte[] payload, int stallAfter)
        {
            this.payload = payload;
            this.stallAfter = stallAfter;
            var port = LocalServerProcess.FreeLoopbackPort();
            Uri = new Uri($"http://localhost:{port}/file.bin");
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            loop = Task.Run(ServeAsync);
        }

        internal Uri Uri { get; }
        internal int Requests => Volatile.Read(ref requests);

        internal static FileServer Start(byte[] payload, int stallAfter = -1) => new(payload, stallAfter);

        private async Task ServeAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) when (stop.IsCancellationRequested) { return; }
                Interlocked.Increment(ref requests);
                try
                {
                    context.Response.ContentLength64 = payload.Length;
                    if (stallAfter < 0)
                    {
                        await context.Response.OutputStream.WriteAsync(payload, stop.Token);
                    }
                    else
                    {
                        await context.Response.OutputStream.WriteAsync(payload.AsMemory(0, stallAfter), stop.Token);
                        await context.Response.OutputStream.FlushAsync(stop.Token);
                        await Task.Delay(Timeout.Infinite, stop.Token);
                    }
                    context.Response.Close();
                }
                catch (Exception) when (stop.IsCancellationRequested) { context.Response.Abort(); return; }
                catch (HttpListenerException) { context.Response.Abort(); }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await loop; } catch (Exception) when (stop.IsCancellationRequested) { }
            listener.Close();
            stop.Dispose();
        }
    }
}
