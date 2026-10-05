using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Core.Flows.Durable;

/// <summary>Installation-local storage. The host must place the directory in its private data area.</summary>
public sealed class FileExecutionArtifactStore : IExecutionArtifactStore, IAsyncDisposable
{
    private readonly string directory;
    private readonly string installationId;
    private readonly FileStream writerLock;
    private readonly SemaphoreSlim gate = new(1, 1);
    internal SemaphoreSlim LedgerGate { get; } = new(1, 1);
    private bool disposed;
    private sealed record Envelope(ArtifactRef Reference, JsonElement Payload);
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public FileExecutionArtifactStore(string directory, string installationId)
    {
        DurableIdentity.Require(installationId);
        this.directory = Path.GetFullPath(directory);
        this.installationId = installationId;
        Directory.CreateDirectory(this.directory);
        RejectLink(this.directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(this.directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var lockPath = Path.Combine(this.directory, ".writer.lock");
        RejectLink(lockPath);
        writerLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var marker = Path.Combine(this.directory, ".installation");
            RejectLink(marker);
            if (File.Exists(marker))
            {
                if (File.ReadAllText(marker) != installationId) throw new InvalidDataException("Artifact directory belongs to a different installation.");
            }
            else WriteAtomic(marker, Encoding.UTF8.GetBytes(installationId));
        }
        catch { writerLock.Dispose(); throw; }
    }

    public async ValueTask<ArtifactRef> PutOnceAsync(ArtifactScope scope, string key, JsonElement payload, CancellationToken token)
    {
        Validate(scope, key);
        var clone = payload.Clone();
        var reference = new ArtifactRef(1, scope, key, Digest(clone));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var path = ArtifactPath(scope, key);
            if (File.Exists(path))
            {
                var existing = ReadEnvelope(path);
                if (existing.Reference != reference) throw new InvalidDataException("Artifact operation key was already committed with different content.");
                return existing.Reference;
            }
            token.ThrowIfCancellationRequested();
            WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(new Envelope(reference, clone), Options));
            return reference;
        }
        finally { gate.Release(); }
    }

    public async ValueTask<JsonElement> ReadAsync(ArtifactRef reference, CancellationToken token)
    {
        DurableIdentity.Validate(reference);
        Validate(reference.Scope, reference.Key);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var envelope = ReadEnvelope(ArtifactPath(reference.Scope, reference.Key));
            if (envelope.Reference != reference) throw new InvalidDataException("Artifact scope or digest mismatch.");
            return envelope.Payload.Clone();
        }
        finally { gate.Release(); }
    }

    public async ValueTask<ArtifactRef?> FindAsync(ArtifactScope scope, string key, CancellationToken token)
    {
        Validate(scope, key);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var path = ArtifactPath(scope, key);
            if (!File.Exists(path)) return null;
            var reference = ReadEnvelope(path).Reference;
            if (reference.Scope != scope || reference.Key != key) throw new InvalidDataException("Artifact identity mismatch.");
            return reference;
        }
        finally { gate.Release(); }
    }

    internal async ValueTask<IReadOnlyList<ArtifactRef>> ListAsync(ArtifactScope scope, string prefix, CancellationToken token)
    {
        Validate(scope, prefix);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var result = new List<ArtifactRef>();
            foreach (var path in Directory.EnumerateFiles(directory, ScopeHash(scope) + "_*.json"))
            {
                token.ThrowIfCancellationRequested();
                var reference = ReadEnvelope(path).Reference;
                if (reference.Scope != scope || path != ArtifactPath(scope, reference.Key)) throw new InvalidDataException("Artifact identity mismatch.");
                if (reference.Key.StartsWith(prefix, StringComparison.Ordinal)) result.Add(reference);
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private void Validate(ArtifactScope scope, string key)
    {
        DurableIdentity.Validate(scope);
        DurableIdentity.Require(key);
        if (key is "." or ".." || key.IndexOfAny(['/', '\\', ':']) >= 0) throw new ArgumentException("Artifact keys cannot be filesystem paths.");
        if (scope.InstallationId != installationId) throw new InvalidDataException("Foreign installation.");
    }
    private string ArtifactPath(ArtifactScope scope, string key) => Path.Combine(directory,
        ScopeHash(scope) + "_" + DurableIdentity.Hash("key", [key]) + ".json");
    private static string ScopeHash(ArtifactScope scope) => DurableIdentity.Hash("scope", DurableIdentity.ScopeParts(scope));
    private static string Digest(JsonElement payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload.GetRawText())));
    private static Envelope ReadEnvelope(string path)
    {
        try
        {
            RejectLink(path);
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException("Empty artifact.");
            DurableIdentity.Validate(envelope.Reference);
            if (Digest(envelope.Payload) != envelope.Reference.Sha256) throw new InvalidDataException("Corrupt artifact digest.");
            return envelope;
        }
        catch (Exception error) when (error is JsonException or FileNotFoundException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException("Artifact is missing or malformed.", error); }
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Artifact paths cannot be symbolic links or reparse points.");
    }
    private static void WriteAtomic(string path, byte[] bytes)
    {
        RejectLink(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (!disposed) { disposed = true; writerLock.Dispose(); } }
        finally { gate.Release(); }
    }
}
