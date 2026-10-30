using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Cortex.TestSupport.LocalModels;

/// <summary>
/// A local server (llama-server, laya-serve) owned by a test. Disposal kills the whole process tree and waits for it,
/// so a failing or interrupted test never leaves a server holding the GPU or the port.
/// </summary>
internal sealed class LocalServerProcess : IAsyncDisposable
{
    private const int TailLines = 80;
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Queue<string> tail = new();
    private readonly Uri? baseUri;
    private bool disposed;

    private LocalServerProcess(Process process, Uri? baseUri)
    {
        Process = process;
        this.baseUri = baseUri;
    }

    internal Process Process { get; }

    internal Uri BaseUri => baseUri ?? throw new InvalidOperationException("This process was started without a readiness URL, so it has no base URI.");

    /// <summary>
    /// Starts the process and, when <paramref name="readiness"/> is given, polls it until it answers 200. On exit or
    /// timeout the process tree is killed and the exception carries the tail of its stdout/stderr.
    /// </summary>
    internal static async Task<LocalServerProcess> StartAsync(string fileName, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, Uri? readiness, TimeSpan timeout, CancellationToken token)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment) start.Environment[name] = value;

        var process = new Process { StartInfo = start };
        var server = new LocalServerProcess(process, readiness is null ? null : new Uri(readiness, "/"));
        process.OutputDataReceived += (_, e) => server.Record(e.Data);
        process.ErrorDataReceived += (_, e) => server.Record(e.Data);
        if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (readiness is null) return server;

        try
        {
            await server.WaitReadyAsync(fileName, readiness, timeout, token);
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    internal static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    internal string OutputTail()
    {
        lock (tail) return string.Join(Environment.NewLine, tail);
    }

    private void Record(string? line)
    {
        if (line is null) return;
        lock (tail)
        {
            tail.Enqueue(line);
            while (tail.Count > TailLines) tail.Dequeue();
        }
    }

    private async Task WaitReadyAsync(string fileName, Uri readiness, TimeSpan timeout, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Process.HasExited)
            {
                await Process.WaitForExitAsync(token); // drains the redirected streams into the tail
                throw new InvalidOperationException(
                    $"{fileName} exited with code {Process.ExitCode} before {readiness} was ready. Output tail:{Environment.NewLine}{OutputTail()}");
            }
            try
            {
                using var response = await Probe.GetAsync(readiness, token);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { }

            if (deadline.Elapsed >= timeout)
                throw new TimeoutException($"{fileName} did not answer 200 on {readiness} within {timeout}. Output tail:{Environment.NewLine}{OutputTail()}");
            await Task.Delay(250, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { } // exited between the check and the kill
        await Process.WaitForExitAsync();
        // Process is deliberately not disposed: callers may still read HasExited/ExitCode after disposal.
    }
}
