using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Cortex.TestSupport.LocalModels;

/// <summary>
/// Persistent download cache for local-model tests. Layout: <c>llama.cpp\</c> (one folder, replaced when the pinned tag
/// changes), <c>models\</c>, <c>laya-venv\</c>. Every download streams to <c>*.partial</c>, is SHA-256 checked, and only
/// then renamed; a mismatch deletes the partial and fails.
/// </summary>
internal sealed class LocalModelCache(string root)
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private const string StampFile = ".tag";
    internal const string CudaTorchCheck = "import sys, torch; sys.exit(0 if torch.version.cuda else 1)";

    internal LocalModelCache() : this(DefaultRoot()) { }

    /// <summary>A download fails when no bytes (or no response headers) arrive for this long.</summary>
    internal TimeSpan ReadInactivityTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Runs an external command and throws on a non-zero exit. Replaceable so the Laya venv logic is testable offline.</summary>
    internal Func<string, IReadOnlyList<string>, CancellationToken, Task> Run { get; init; } = RunAsync;

    internal string Root { get; } = Path.GetFullPath(root);

    internal static string DefaultRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CORTEX_LOCAL_MODELS_DIR");
        return !string.IsNullOrWhiteSpace(configured) ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cortex", "local-models");
    }

    internal async Task<string> EnsureFileAsync(Uri source, string sha256, string relativePath, CancellationToken token)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path))
        {
            if (await HashAsync(path, token) == sha256) return path;
            File.Delete(path); // stale pin or corruption: the final name only ever holds verified bytes
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        try
        {
            string actual;
            using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                inactivity.CancelAfter(ReadInactivityTimeout);
                using var response = await Http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, inactivity.Token);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(inactivity.Token);
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 20];
                while (true)
                {
                    inactivity.CancelAfter(ReadInactivityTimeout);
                    var read = await input.ReadAsync(buffer, inactivity.Token);
                    if (read == 0) break;
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"Download of {source} stalled: no data for {ReadInactivityTimeout}. The partial download was deleted.");
            }
            if (actual != sha256)
                throw new InvalidDataException($"SHA-256 mismatch for {source}: expected {sha256}, downloaded {actual}. The partial download was deleted.");
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    /// <summary>Returns <c>llama.cpp\llama-server.exe</c>; a missing or different <c>.tag</c> stamp wipes and re-extracts the folder.</summary>
    internal async Task<string> EnsureLlamaServerAsync(LocalModelManifest manifest, CancellationToken token)
    {
        using var _ = await LockAsync(token);
        var directory = Resolve("llama.cpp");
        var server = Path.Combine(directory, "llama-server.exe");
        var stamp = Path.Combine(directory, StampFile);
        if (File.Exists(server) && File.Exists(stamp) && (await File.ReadAllTextAsync(stamp, token)).Trim() == manifest.LlamaCpp.Tag) return server;

        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        foreach (var asset in manifest.LlamaCpp.Assets)
        {
            var zip = await EnsureFileAsync(asset.Url, asset.Sha256, Path.Combine("llama.cpp", asset.FileName), token);
            ZipFile.ExtractToDirectory(zip, directory, overwriteFiles: true);
            File.Delete(zip);
        }
        if (!File.Exists(server)) throw new InvalidDataException($"llama.cpp {manifest.LlamaCpp.Tag} assets did not contain llama-server.exe at the archive root.");
        await File.WriteAllTextAsync(stamp, manifest.LlamaCpp.Tag, token);
        return server;
    }

    internal async Task<string> EnsureGgufAsync(LocalModelManifest manifest, string modelKey, CancellationToken token)
    {
        var pin = manifest.Model(modelKey);
        using var _ = await LockAsync(token);
        return await EnsureFileAsync(pin.Url, pin.Sha256, Path.Combine("models", pin.File), token);
    }

    /// <summary>
    /// Creates <c>laya-venv\</c> with Python 3.11 and installs the pinned CUDA torch and <c>laya[serve]</c> in one resolver
    /// pass (PyTorch's index as an extra index, so nothing can swap torch for PyPI's CPU build), then proves
    /// <c>torch.version.cuda</c> is set. Only then is the <c>.tag</c> stamp written; a different or missing stamp rebuilds
    /// the venv. Returns the venv's <c>python.exe</c>.
    /// </summary>
    internal async Task<string> EnsureLayaAsync(LocalModelManifest manifest, CancellationToken token)
    {
        using var _ = await LockAsync(token);
        var directory = Resolve("laya-venv");
        var python = Path.Combine(directory, "Scripts", "python.exe");
        var stamp = Path.Combine(directory, StampFile);
        var expected = $"{manifest.Laya.Torch} {manifest.Laya.Package}";
        if (File.Exists(python) && File.Exists(stamp) && (await File.ReadAllTextAsync(stamp, token)).Trim() == expected) return python;

        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        await Run("py", ["-3.11", "-m", "venv", directory], token);
        await Run(python, ["-m", "pip", "install", "--disable-pip-version-check", "--no-input",
            "--extra-index-url", manifest.Laya.TorchIndexUrl.ToString(), manifest.Laya.Torch, manifest.Laya.Package], token);
        try { await Run(python, ["-c", CudaTorchCheck], token); }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"laya-venv has no CUDA torch after installing {manifest.Laya.Torch} and {manifest.Laya.Package}; the venv was left unstamped and is rebuilt on the next run.", error);
        }
        await File.WriteAllTextAsync(stamp, expected, token);
        return python;
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException($"'{relativePath}' must be a path relative to the cache root.", nameof(relativePath));
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{relativePath}' escapes the cache root '{Root}'.", nameof(relativePath));
        return full;
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }

    /// <summary>Cross-process lock on <c>.lock</c>, so parallel test classes or a running script never extract or download over each other.</summary>
    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, ".lock");
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(250, token); }
        }
    }

    private static async Task RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} exited with code {process.ExitCode}.\n{Tail(await output)}\n{Tail(await error)}");
    }

    private static string Tail(string text) => text.Length <= 4000 ? text : text[^4000..];
}
