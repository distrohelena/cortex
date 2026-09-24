using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.OpenClaw.Observation;

namespace Filomena.App;

// Host-local processed position, independent of WhatsApp read receipts. The runtime's
// durable inbox/outbox still decides dedupe and blocks uncertain execution recovery.
public sealed class NativeLiveReadCursorStore(string directory, string bindingRef, string projectRevision)
{
    private sealed record Position(string Version, string BindingRef, string ProjectRevision, NativeObservationCursor Cursor);
    private sealed record Envelope(string Sha256, JsonElement Payload);
    private readonly string file = Path.Combine(directory, "native-live-read-cursor.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public NativeObservationCursor Resolve(NativeObservationCursor current, NativeObservationCursor? initial)
    {
        var saved = Load();
        if (saved is not null && saved.Epoch != current.Epoch)
            throw new NativeObservationException("native_live_cursor_epoch_changed_requires_reconciliation");
        return NativeLiveCommand.ValidateContinuation(saved ?? initial
            ?? throw new NativeObservationException("native_live_cursor_initial_anchor_required"), current);
    }

    public void Save(NativeObservationCursor cursor)
    {
        NativeLiveCommand.ValidateContinuation(cursor, cursor);
        var previous = Load();
        if (previous is not null && (previous.Epoch != cursor.Epoch || previous.SessionRef != cursor.SessionRef
            || previous.Version != cursor.Version || previous.Sequence > cursor.Sequence))
            throw new NativeObservationException("native_live_cursor_regression");
        var payload = JsonSerializer.SerializeToElement(new Position("native-live-read-cursor:v1", bindingRef, projectRevision, cursor), Json);
        var envelope = new Envelope(Hash(payload), payload);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, envelope, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private NativeObservationCursor? Load()
    {
        if (!File.Exists(file)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(file), Json)!;
            if (stored is null || stored.Sha256 != Hash(stored.Payload)) throw new InvalidDataException();
            var value = stored.Payload.Deserialize<Position>(Json)!;
            if (value is null || value.Version != "native-live-read-cursor:v1" || value.BindingRef != bindingRef
                || value.ProjectRevision != projectRevision || value.Cursor is null) throw new InvalidDataException();
            return value.Cursor;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
        { throw new NativeObservationException("native_live_cursor_invalid_requires_reconciliation"); }
    }
    private static string Hash(JsonElement payload) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json)));
}
