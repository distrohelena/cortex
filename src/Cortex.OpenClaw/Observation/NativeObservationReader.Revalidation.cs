using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace Cortex.OpenClaw.Observation;

internal sealed record NativeReceiverScope(string Epoch, string Fingerprint);

public sealed partial class NativeObservationReader
{
    public async Task<NativeObservationApproval?> TryRevalidateForPollingAsync(NativeObservationApproval previous,
        NativeObservationCursor cursor, CancellationToken cancellationToken)
    {
        // Idle polling only: null grants NO authority, advances NO cursor and retries
        // NO execution. The previous scope remains pinned until a full proof returns.
        try { return await RevalidateAsync(previous, cursor, cancellationToken); }
        catch (NativeObservationException e) when (IsPollingUnavailable(e)) { return null; }
    }

    public static bool IsPollingUnavailable(NativeObservationException error) => error.Code is
        "dispatch_not_ready" or "unavailable" or "preflight_stale_or_dispatch_changed" or "preflight_stale";

    // Reader-minted approvals only. No Open, cursor advancement, capture control,
    // runtime restart or send is involved in this read-only revalidation.
    public async Task<NativeObservationApproval> RevalidateAsync(NativeObservationApproval previous,
        NativeObservationCursor cursor, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(previous.Owner, authority)) throw new NativeObservationException("invalid_preflight_provenance");
        ValidateCursor(cursor);
        var scope = previous.Inspection.ReceiverScope;
        if (scope is null || scope.Epoch != cursor.Epoch)
            throw new NativeObservationException("receiver_revalidation_unproven");
        var current = await PreflightAsync(cancellationToken);
        if (current.Inspection.ReceiverScope != scope)
            throw new NativeObservationException("receiver_revalidation_scope_changed");
        return current;
    }

    private NativeReceiverScope? ReadReceiverScope(JsonElement root)
    {
        try
        {
            var b = root.GetProperty("bootstrap");
            var d = root.GetProperty("dispatch");
            foreach (var flag in new[] { "subsystemOperational", "readerAvailable", "controlAvailable",
                         "configSnapshotAvailable", "dispatchInspectorAvailable" })
                if (!b.GetProperty(flag).GetBoolean()) return null;
            if (Text(b, "captureState") != "armed" || Text(b, "journalHealth") != "healthy"
                || Text(d, "status") != "inactive") return null;
            var epoch = Text(b, "activeEpoch");
            if (epoch != Text(root, "journalEpoch")) return null;
            var proof = b.GetProperty("effectiveSnapshotProof");
            if (Text(proof, "status") != "valid" || Text(proof, "version") != "effective-runtime-config:v1") return null;
            var baseRevision = Text(proof, "revision");
            var recovery = b.GetProperty("legacyRecovery");
            if (Text(recovery, "status") is not ("clear" or "quarantined")
                || Text(recovery, "effectiveConfigRevision") != baseRevision
                || recovery.GetProperty("uncoveredOwnerCount").GetInt64() != 0
                || recovery.GetProperty("activeOwnerCount").GetInt64() != 0
                || recovery.GetProperty("pendingLegacyRecoveryCount").GetInt64() != recovery.GetProperty("quarantinedCount").GetInt64()) return null;
            var gate = b.GetProperty("isolationGate");
            if (Text(gate, "gateVersion") != (binding.Kind == Cortex.Core.Flows.Sessions.SessionEntryKind.Group
                    ? "native-group-isolation:v1" : "native-dm-isolation:v1")
                || Text(gate, "lifecycleState") != "armed"
                || Text(gate, "approvedEffectiveConfigRevision") != baseRevision) return null;
            _ = Text(gate, "accountRef"); _ = Text(gate, "sessionRef"); _ = Text(gate, "placementProof");
            if (Canonical(proof) != Canonical(d.GetProperty("effectiveSnapshotProof"))
                || Canonical(gate) != Canonical(d.GetProperty("isolationGate"))) return null;
            var revision = Text(d, "effectiveConfigRevision");
            if (revision != Text(b, "effectiveConfigRevision")) return null;
            // Additional lifetime fence: retain the native registry generation and
            // gate lifetime. This token is NOT used as snapshot validity evidence.
            // Only the producer component may differ, and only in this v1 shape.
            var producer = Text(d, "producerRevision");
            // Native v1 producer = receiver UUID + retained-config SHA256.
            // Reconnection changes the UUID, not the retained configuration.
            if (producer.Length != 101 || producer[36] != ':' || !Guid.TryParseExact(producer[..36], "D", out _)
                || producer[37..].Any(c => !char.IsAsciiHexDigit(c))) return null;
            var prefix = baseRevision + ":producer-" + producer + ":registry-";
            if (!revision.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var suffix = revision[prefix.Length..];
            var gateSuffix = ":isolation-" + Text(gate, "gateInstanceRevision");
            if (!suffix.EndsWith(gateSuffix, StringComparison.Ordinal)
                || !ulong.TryParse(suffix[..^gateSuffix.Length], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return null;
            var coverage = gate.GetProperty("coveredPathIds").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
            var paths = d.GetProperty("paths").EnumerateArray().ToArray();
            if (paths.Length == 0 || paths.Select(p => Text(p, "family")).Distinct().Count() != paths.Length) return null;
            foreach (var p in paths)
            {
                if (Text(p, "effectiveConfigRevision") != baseRevision) return null;
                var effective = Text(p, "effectiveStatus");
                if (effective == "inactive")
                {
                    if (Text(p, "rawStatus") != "inactive") return null;
                }
                else if (effective != "excluded" || !coverage.Contains(Text(p, "family"))
                    || Text(p, "exclusionStatus") != "excluded_by_gate" || string.IsNullOrWhiteSpace(Text(p, "placementProof"))) return null;
                if (Text(p, "status") != effective) return null;
            }
            var stable = new JsonObject();
            foreach (var field in new[] { "version", "sessionRef", "channel", "kind", "routeBindingRef",
                         "selfIdentityRef", binding.Kind == Cortex.Core.Flows.Sessions.SessionEntryKind.Group
                             ? "participantActorRefs" : "peerActorRef", "capabilities" })
                stable[field] = JsonNode.Parse(root.GetProperty(field).GetRawText());
            stable["proof"] = JsonNode.Parse(proof.GetRawText());
            stable["gate"] = JsonNode.Parse(gate.GetRawText());
            stable["legacyRecovery"] = JsonNode.Parse(recovery.GetRawText());
            stable["registryAndGateLifetime"] = suffix;
            stable["retainedConfigHash"] = producer[37..];
            stable["paths"] = new JsonArray(paths.OrderBy(p => Text(p, "family"), StringComparer.Ordinal)
                .Select(p => JsonNode.Parse(p.GetRawText())).ToArray());
            var bytes = System.Text.Encoding.UTF8.GetBytes(Canonical(JsonSerializer.SerializeToElement(stable)));
            return new(epoch, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception e) when (e is NativeObservationException or JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException) { return null; }
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
}
