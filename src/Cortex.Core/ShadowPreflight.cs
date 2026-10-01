namespace Cortex.Core.Shadow;

public sealed record ShadowPreflightCheck(string Id, bool Passed, string Phase, string Diagnostic, double DurationMilliseconds);

// Reports are audit data, NOT capabilities. Passing a JSON report never starts a runtime/listener.
public sealed class ShadowPreflightResult
{
    public static IReadOnlyList<string> RequiredChecks { get; } = Array.AsReadOnly(new[] {
        "project_revision", "project_pinned", "outbound_shadow", "enabled_impossible", "exact_session", "dm_only",
        "inbound_stable_identity", "inbound_dedupe", "initial_watermark", "self_identity", "legacy_lifecycle_isolated",
        "observation_contract", "observation_health", "inbound_durable_cursor", "inbound_replayable", "actor_identity", "inbound_from_me", "dispatch_inspection",
        "effective_config_snapshot", "effective_config_revision", "effective_config_current",
        "graphiti_reachable", "graphiti_namespace", "graphiti_participant", "graphiti_boundary_recall", "graphiti_social_recall",
        "graphiti_canary_isolation", "empty_vs_outage", "outbound_journal", "inbound_journal", "transport_send_unavailable" });
    public bool Ready => CriticalFailures.Count == 0;
    public bool TransportReady => Checks.Where(c => !c.Id.StartsWith("graphiti_", StringComparison.Ordinal) && c.Id != "empty_vs_outage").All(c => c.Passed);
    public string? EffectiveConfigRevision { get; }
    public IReadOnlyList<ShadowPreflightCheck> Checks { get; }
    public IReadOnlyList<string> CriticalFailures { get; }
    public IReadOnlyList<string> Warnings { get; } = Array.AsReadOnly(new[] { "Shadow does not prove post-spoken FSM evolution; no listener is started by preflight." });
    public string? ResolvedProjectRevision { get; }
    public string? ResolvedProjectId { get; }
    public string? ConfiguredChannel { get; }
    public string? ResolvedSessionOpaqueRef { get; }
    public string RuntimeVersion => typeof(ShadowPreflightResult).Assembly.GetName().Version?.ToString() ?? "unknown";
    private ShadowPreflightResult(ShadowPreflightCheck[] checks, string? revision, string? session, string? projectId, string? channel, string? effectiveConfigRevision)
    {
        Checks = Array.AsReadOnly(checks);
        CriticalFailures = Array.AsReadOnly(checks.Where(c => !c.Passed).Select(c => c.Id).ToArray());
        ResolvedProjectRevision = revision; ResolvedSessionOpaqueRef = session;
        ResolvedProjectId = projectId; ConfiguredChannel = channel;
        EffectiveConfigRevision = effectiveConfigRevision;
    }
    public static ShadowPreflightResult Create(IEnumerable<ShadowPreflightCheck> checks, string? revision, string? session, string? projectId = null, string? channel = null, string? effectiveConfigRevision = null)
    {
        var groups = checks.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var resolved = RequiredChecks.Union(groups.Keys).Select(id => !groups.TryGetValue(id, out var items)
            ? new(id, false, "not_run", "evidence_missing", 0)
            : items.Length == 1 ? items[0] : new ShadowPreflightCheck(id, false, "invalid", "duplicate_evidence", 0)).ToList();
        if (string.IsNullOrWhiteSpace(revision)) resolved.Add(new("resolved_revision", false, "static", "revision_missing", 0));
        if (string.IsNullOrWhiteSpace(session)) resolved.Add(new("resolved_session", false, "static", "session_missing", 0));
        return new(resolved.ToArray(), revision, session, projectId, channel, effectiveConfigRevision);
    }
}

public static class ShadowSourceEvidence
{
    // Safe default for any source lacking a proven native stream. Cannot be configured green by a project.
    public static IReadOnlyList<ShadowPreflightCheck> Unproven() => NativeObservationPreflight.Check(
        new("unbound", "unavailable", "", "unknown", new Dictionary<string, bool>()), "");
}
