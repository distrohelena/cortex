namespace Cortex.Core.Shadow;

// Audit evidence from a trusted host adapter, never a capability minted by project JSON.
// This evaluator does not connect a source or start a listener.
public sealed record NativeObservationSourceStatus(string Version, string Health, string SessionRef,
    string DispatchStatus, IReadOnlyDictionary<string, bool> Capabilities);

public static class NativeObservationPreflight
{
    private static readonly IReadOnlyDictionary<string, string> Checks = new Dictionary<string, string> {
        ["stableNativeMessageId"] = "inbound_stable_identity",
        ["durableObservationCursor"] = "inbound_durable_cursor",
        ["replayableObservation"] = "inbound_replayable",
        ["exactSessionBinding"] = "exact_session",
        ["actorIdentity"] = "actor_identity",
        ["fromMe"] = "inbound_from_me",
        ["selfIdentity"] = "self_identity",
        ["dispatchInspection"] = "dispatch_inspection"
    };
    public static IReadOnlyList<string> RequiredCapabilities { get; } = Array.AsReadOnly(Checks.Keys.ToArray());

    public static IReadOnlyList<ShadowPreflightCheck> Check(NativeObservationSourceStatus source, string expectedSourceSessionRef,
        string expectedVersion = "inbound-observation:v1")
    {
        var version = expectedVersion is "inbound-observation:v1" or "inbound-observation:v2"
            && source.Version == expectedVersion;
        var exact = !string.IsNullOrWhiteSpace(expectedSourceSessionRef)
            && StringComparer.Ordinal.Equals(source.SessionRef, expectedSourceSessionRef);
        var healthy = version && exact && source.Health == "healthy";
        var result = new List<ShadowPreflightCheck> {
            new("observation_contract", version, "transport", version ? expectedVersion : "unknown_contract", 0),
            new("observation_health", healthy, "transport", healthy ? "healthy" : "source_not_healthy_or_bound", 0),
            new("legacy_lifecycle_isolated", healthy && source.DispatchStatus == "inactive"
                && source.Capabilities.GetValueOrDefault("dispatchInspection"), "transport", "dispatch_must_be_inactive", 0),
            new("initial_watermark", healthy && source.Capabilities.GetValueOrDefault("durableObservationCursor")
                && source.Capabilities.GetValueOrDefault("replayableObservation"), "transport", "atomic_journal_snapshot_required", 0)
        };
        foreach (var (capability, check) in Checks)
            result.Add(new(check, healthy && source.Capabilities.GetValueOrDefault(capability), "transport", capability, 0));
        return result.AsReadOnly();
    }
}
