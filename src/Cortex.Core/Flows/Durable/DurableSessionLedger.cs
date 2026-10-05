using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

/// <summary>Each receipt is a single atomic immutable artifact; no separate crash-prone index is authoritative.</summary>
public sealed class DurableSessionLedger(FileExecutionArtifactStore store) : ISessionLedger
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record StoredRecord(long Sequence, LedgerRecord Record);
    private sealed record CursorV1(int Version, string ScopeId, string Collection, long After, long Through);
    private static ArtifactScope Scope(string installationId, SessionKey session) => new(installationId, session, "session-ledger", "session-ledger-v1");
    private static string AdmissionKey(string inputKey) => DurableIdentity.Hash("admission", [inputKey]);

    public async ValueTask<LedgerAdmission> AdmitAsync(LedgerAdmissionRequest request, CancellationToken token)
    {
        DurableIdentity.Require(request.InputKey);
        if (request.InputRef.Scope.InstallationId != request.InstallationId || request.InputRef.Scope.Session != request.Session
            || request.Digest != request.InputRef.Sha256) throw new InvalidDataException("Admission scope or digest mismatch.");
        await store.LedgerGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var previous = await FindAdmissionAsync(request.InstallationId, request.Session, request.InputKey, token).ConfigureAwait(false);
            if (previous is not null)
            {
                if (previous.Digest != request.Digest || previous.InputRef != request.InputRef) throw new InvalidDataException("Conflicting admission input.");
                return previous;
            }
            await store.ReadAsync(request.InputRef, token).ConfigureAwait(false);
            DurableIdentity.Require(request.BatchId);
            DurableIdentity.Require(request.ExecutionId);
            var scope = Scope(request.InstallationId, request.Session);
            var references = await store.ListAsync(scope, "cortex-v1-admission-", token).ConfigureAwait(false);
            long lastWatermark = 0;
            foreach (var reference in references)
                lastWatermark = Math.Max(lastWatermark, (await Read<LedgerAdmission>(reference, token).ConfigureAwait(false)).Watermark);
            if (request.ProposedWatermark != checked(lastWatermark + 1)) throw new InvalidDataException("Admission watermark is not the next committed watermark.");
            var receipt = new LedgerAdmission(SessionAdmissionStatus.Accepted, request.ProposedWatermark,
                request.BatchId, request.ExecutionId, request.InputKey, request.Digest, request.InputRef);
            await store.PutOnceAsync(scope, AdmissionKey(request.InputKey), JsonSerializer.SerializeToElement(receipt, Options), token).ConfigureAwait(false);
            return receipt;
        }
        finally { store.LedgerGate.Release(); }
    }

    public async ValueTask<LedgerAdmission?> FindAdmissionAsync(string installationId, SessionKey session, string inputKey, CancellationToken token)
    {
        var reference = await store.FindAsync(Scope(installationId, session), AdmissionKey(inputKey), token).ConfigureAwait(false);
        if (reference is null) return null;
        var receipt = await Read<LedgerAdmission>(reference, token).ConfigureAwait(false);
        if (receipt.InputKey != inputKey) throw new InvalidDataException("Admission identity mismatch.");
        return receipt;
    }

    public async ValueTask RecordOnceAsync(LedgerRecord record, CancellationToken token)
    {
        Collection(record.Collection);
        if (record.Collection == "admissions") throw new ArgumentException("Use AdmitAsync to record admissions.");
        if (record.Reference.Scope.InstallationId != record.InstallationId || record.Reference.Scope.Session != record.Session)
            throw new InvalidDataException("Ledger record scope mismatch.");
        await store.LedgerGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var scope = Scope(record.InstallationId, record.Session);
            var key = RecordKey(record.Collection, record.OperationId);
            var existing = await store.FindAsync(scope, key, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if ((await Read<StoredRecord>(existing, token).ConfigureAwait(false)).Record != record) throw new InvalidDataException("Conflicting ledger record.");
                return;
            }
            await store.ReadAsync(record.Reference, token).ConfigureAwait(false);
            var records = await Records(scope, record.Collection, token).ConfigureAwait(false);
            var sequence = records.Count == 0 ? 1 : checked(records[^1].Sequence + 1);
            await store.PutOnceAsync(scope, key, JsonSerializer.SerializeToElement(new StoredRecord(sequence, record), Options), token).ConfigureAwait(false);
        }
        finally { store.LedgerGate.Release(); }
    }

    public async ValueTask<ArtifactPage<ArtifactRef>> ReadPageAsync(string installationId, SessionKey session, string collection, PageRequest page, CancellationToken token)
    {
        Collection(collection); page.Validate();
        var scope = Scope(installationId, session);
        var scopeId = DurableIdentity.SessionWorkflowId(installationId, session);
        await store.LedgerGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var records = await Records(scope, collection, token).ConfigureAwait(false);
            var cursor = page.Cursor is null ? new CursorV1(1, scopeId, collection, 0, records.Count == 0 ? 0 : records[^1].Sequence) : DecodeCursor(page.Cursor);
            if (cursor.Version != 1 || cursor.ScopeId != scopeId || cursor.Collection != collection || cursor.After < 0 || cursor.Through < cursor.After)
                throw new InvalidDataException("Invalid ledger page cursor.");
            var available = records.Where(x => x.Sequence > cursor.After && x.Sequence <= cursor.Through).Take(page.Limit + 1).ToArray();
            var items = available.Take(page.Limit).ToArray();
            var next = available.Length > page.Limit ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor with { After = items[^1].Sequence }, Options)) : null;
            return new(items.Select(x => x.Record.Reference).ToArray(), next);
        }
        finally { store.LedgerGate.Release(); }
    }
    private static string RecordKey(string collection, string operationId) => collection + "-" + DurableIdentity.Hash("record", [operationId]);
    private static void Collection(string collection)
    {
        if (collection is not ("history" or "outcomes" or "traces" or "feedback" or "admissions")) throw new ArgumentException("Unknown ledger collection.");
    }
    private async ValueTask<List<StoredRecord>> Records(ArtifactScope scope, string collection, CancellationToken token)
    {
        var result = new List<StoredRecord>();
        if (collection == "admissions")
        {
            foreach (var reference in await store.ListAsync(scope, "cortex-v1-admission-", token).ConfigureAwait(false))
            {
                var admission = await Read<LedgerAdmission>(reference, token).ConfigureAwait(false);
                result.Add(new(admission.Watermark, new(scope.InstallationId, scope.Session, collection, admission.InputKey, reference)));
            }
        }
        foreach (var reference in await store.ListAsync(scope, collection + "-", token).ConfigureAwait(false))
            result.Add(await Read<StoredRecord>(reference, token).ConfigureAwait(false));
        result.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        if (result.Select(x => x.Sequence).Distinct().Count() != result.Count || result.Any(x => x.Sequence < 1)) throw new InvalidDataException("Corrupt ledger sequence.");
        return result;
    }
    private async ValueTask<T> Read<T>(ArtifactRef reference, CancellationToken token) =>
        (await store.ReadAsync(reference, token).ConfigureAwait(false)).Deserialize<T>(Options) ?? throw new InvalidDataException("Empty ledger artifact.");
    private static CursorV1 DecodeCursor(string cursor)
    {
        try { return JsonSerializer.Deserialize<CursorV1>(Convert.FromBase64String(cursor), Options) ?? throw new InvalidDataException("Empty cursor."); }
        catch (Exception error) when (error is FormatException or JsonException) { throw new InvalidDataException("Malformed cursor.", error); }
    }
}
