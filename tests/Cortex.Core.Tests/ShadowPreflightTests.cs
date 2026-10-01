using Cortex.Core.Shadow;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class ShadowPreflightTests
{
    [Fact]
    public void Observation_v1_requires_healthy_exact_source_and_every_capability()
    {
        var capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(id => id, _ => true);
        var source = new NativeObservationSourceStatus("inbound-observation:v1", "healthy", "session-ref", "inactive", capabilities);
        Assert.All(NativeObservationPreflight.Check(source, "session-ref"), check => Assert.True(check.Passed));
        foreach (var capability in capabilities.Keys.ToArray())
        {
            var incomplete = new Dictionary<string, bool>(capabilities);
            incomplete.Remove(capability);
            Assert.Contains(NativeObservationPreflight.Check(source with { Capabilities = incomplete }, "session-ref"), check => !check.Passed);
        }
        foreach (var health in new[] { "degraded", "gap_detected", "cursor_expired", "unavailable", "unknown" })
            Assert.Contains(NativeObservationPreflight.Check(source with { Health = health }, "session-ref"), check => !check.Passed);
        Assert.Contains(NativeObservationPreflight.Check(source, "other"), check => !check.Passed);
        Assert.Contains(NativeObservationPreflight.Check(source with { Version = "latest" }, "session-ref"), check => !check.Passed);
        foreach (var dispatch in new[] { "active", "unknown" })
            Assert.Contains(NativeObservationPreflight.Check(source with { DispatchStatus = dispatch }, "session-ref"), check => !check.Passed);
    }

    [Fact]
    public void Missing_checks_never_imply_ready()
    {
        var result = ShadowPreflightResult.Create([], "revision", "opaque");
        Assert.False(result.Ready);
        Assert.Equal(ShadowPreflightResult.RequiredChecks.Count, result.CriticalFailures.Count);
    }

    [Fact]
    public void Every_critical_check_is_required_and_duplicate_checks_fail_closed()
    {
        var checks = ShadowPreflightResult.RequiredChecks.Select(id => new ShadowPreflightCheck(id, true, "test", "test evidence", 0)).ToArray();
        Assert.True(ShadowPreflightResult.Create(checks, "revision", "opaque").Ready);
        foreach (var check in checks)
            Assert.False(ShadowPreflightResult.Create(checks.Where(c => c.Id != check.Id), "revision", "opaque").Ready);
        Assert.False(ShadowPreflightResult.Create(checks.Append(checks[0]), "revision", "opaque").Ready);
    }

    [Fact]
    public void Current_openclaw_contract_cannot_authorize_live_even_with_all_other_checks_green()
    {
        var blocked = ShadowSourceEvidence.Unproven();
        var other = ShadowPreflightResult.RequiredChecks.Where(id => blocked.All(c => c.Id != id))
            .Select(id => new ShadowPreflightCheck(id, true, "test", "test evidence", 0));
        var result = ShadowPreflightResult.Create(other.Concat(blocked), "revision", "opaque");
        Assert.False(result.Ready);
        Assert.Contains("inbound_stable_identity", result.CriticalFailures);
        Assert.Contains("self_identity", result.CriticalFailures);
        Assert.Contains("legacy_lifecycle_isolated", result.CriticalFailures);
    }
}
