using Filomena.Core.Flows;

namespace Filomena.Core.Inference;

public sealed class HostInferenceGrant
{
    internal HostInferenceGrant() { }
}

public sealed class HostInferenceAuthority
{
    private readonly object gate = new();
    private readonly Dictionary<HostInferenceGrant, Entry> grants = new();
    private readonly TimeProvider timeProvider;
    private readonly Action<InferenceAttemptObservation>? observe;
    private int observationFailed;
    public bool ObservationFailed => Volatile.Read(ref observationFailed) != 0;
    internal void RecordObservationFailure() => Interlocked.Exchange(ref observationFailed, 1);

    public HostInferenceAuthority(TimeProvider? timeProvider = null, Action<InferenceAttemptObservation>? observe = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.observe = observe;
    }

    public HostInferenceGrant Grant(InferenceScope scope, string principalRef, IReadOnlyList<HostInferenceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalRef);
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0) throw new ArgumentException("Profiles required.", nameof(profiles));
        var copied = profiles.ToDictionary(p => p.ProfileId, StringComparer.Ordinal);
        var grant = new HostInferenceGrant();
        lock (gate) grants.Add(grant, new(scope, principalRef, copied));
        return grant;
    }

    public void Revoke(HostInferenceGrant grant)
    {
        lock (gate)
        {
            if (!grants.TryGetValue(grant, out var entry)) throw new InferenceDeniedException("foreign_grant");
            entry.Revoked = true;
        }
    }

    public IFlowCompletion Bind(HostInferenceGrant grant, InferenceScope scope, string profileId, InferenceCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);
        lock (gate)
        {
            var entry = Require(grant, scope);
            if (!entry.Profiles.TryGetValue(profileId, out var profile)) throw new InferenceDeniedException("unknown_profile");
            if (correlation.StepId != profile.ExpectedRequest.NodeId) throw new InferenceDeniedException("step_mismatch");
            return new HostBoundFlowCompletion(this, grant, scope, entry.PrincipalRef, profile, correlation, timeProvider, observe);
        }
    }

    internal void Admit(HostInferenceGrant grant, InferenceScope scope, CancellationToken token)
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            Require(grant, scope);
            // Linearization point relative to Revoke. Already admitted attempts may finish.
        }
    }

    private Entry Require(HostInferenceGrant grant, InferenceScope scope)
    {
        if (!grants.TryGetValue(grant, out var entry)) throw new InferenceDeniedException("foreign_grant");
        if (entry.Scope != scope) throw new InferenceDeniedException("scope_mismatch");
        if (entry.Revoked) throw new InferenceDeniedException("grant_revoked");
        return entry;
    }

    private sealed class Entry(InferenceScope scope, string principalRef, Dictionary<string, HostInferenceProfile> profiles)
    {
        internal InferenceScope Scope { get; } = scope;
        internal string PrincipalRef { get; } = principalRef;
        internal Dictionary<string, HostInferenceProfile> Profiles { get; } = profiles;
        internal bool Revoked { get; set; }
    }
}
