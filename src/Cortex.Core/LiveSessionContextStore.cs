using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Shadow;

// Private host checkpoint. Journal fingerprints prevent resuming across uncheckpointed
// work, uncertain sends, a torn commit or a different lineage. Never retries anything.
internal sealed class LiveSessionContextStore(string directory, ShadowSessionBinding binding, PinnedShadowProject project)
{
    private sealed record Checkpoint(string Version, string AuthorityRef, string ProjectId, long ProjectVersion,
        string ProjectRevision, string InboundHash, string OutboundHash, SessionRuntimeContext? Context);
    private sealed record StoredCheckpoint(string Sha256, JsonElement Payload);
    private readonly string file = Path.Combine(directory, "observation-live-context.json");
    private readonly string inbound = Path.Combine(directory, "observation-live-inbound.jsonl");
    private readonly string outbound = Path.Combine(directory, "observation-live-outbound.jsonl");
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };

    internal SessionRuntimeContext? Load()
    {
        if (!File.Exists(file))
        {
            if (NonEmpty(inbound) || NonEmpty(outbound))
                throw new InvalidDataException("live_context_missing_requires_explicit_import");
            return null;
        }
        var stored = JsonSerializer.Deserialize<StoredCheckpoint>(File.ReadAllBytes(file), Json)
            ?? throw new InvalidDataException("live_context_invalid");
        if (stored.Sha256 != Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored.Payload, Json))))
            throw new InvalidDataException("live_context_checksum_mismatch");
        var saved = stored.Payload.Deserialize<Checkpoint>(Json) ?? throw new InvalidDataException("live_context_invalid");
        if (saved.Version != "live-session-context:v1" || saved.AuthorityRef != binding.AuthorityRef
            || saved.ProjectId != project.Project.ProjectId || saved.ProjectVersion != project.Project.ProjectVersion
            || saved.ProjectRevision != project.Project.ProjectRevision
            || saved.InboundHash != Fingerprint(inbound) || saved.OutboundHash != Fingerprint(outbound))
            throw new InvalidDataException("live_context_or_journal_mismatch_requires_reconciliation");
        return saved.Context;
    }

    internal void Save(SessionRuntimeContext? context)
    {
        var saved = new Checkpoint("live-session-context:v1", binding.AuthorityRef, project.Project.ProjectId,
            project.Project.ProjectVersion, project.Project.ProjectRevision, Fingerprint(inbound), Fingerprint(outbound), context);
        var payload = JsonSerializer.SerializeToElement(saved, Json);
        var stored = new StoredCheckpoint(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json))), payload);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, stored, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool NonEmpty(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
    private static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return Convert.ToHexString(SHA256.HashData([]));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
