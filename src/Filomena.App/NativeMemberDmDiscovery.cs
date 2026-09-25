using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filomena.App;

public sealed record NativeMemberDmDiscoverySnapshot(string Enrollment,
    IReadOnlyList<NativeMemberDmDiscoveredScope> Scopes);

public sealed record NativeMemberDmDiscoveredScope(string Status, string? FailureCode, string? ScopeRef,
    string? NativeConversationId, IReadOnlyList<string> NativeAliases, string? MembershipLeaseRef,
    long? LeaseExpiresAt, string? ReaderEndpoint, string? RouteBindingRef, string? EffectiveConfigRevision,
    string? Epoch, NativeMemberDmCursor? InitialCursor)
{
    public bool IsRunnable => Status == "ready";
}

public sealed record NativeMemberDmCursor(string Version, string Epoch, string SessionRef, long Sequence);

/// <summary>Strict parser for the additive discovery member on the authenticated group status response.</summary>
public static class NativeMemberDmDiscoveryValidator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private static readonly HashSet<string> FailureCodes = new(StringComparer.Ordinal)
    {
        "enrollment_disabled", "metadata_unavailable", "membership_denied", "dispatch_unavailable",
        "journal_unavailable", "owner_draining",
    };

    public static NativeMemberDmDiscoverySnapshot Parse(JsonElement value, string pinnedAccountId,
        string pinnedGroupJid, string authenticatedGroupSessionRef)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pinnedAccountId);
            ArgumentException.ThrowIfNullOrWhiteSpace(pinnedGroupJid);
            ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedGroupSessionRef);
            var wire = JsonSerializer.Deserialize<DiscoveryWire>(value.GetRawText(), Json)
                ?? throw new InvalidDataException("member_dm_discovery_missing");
            if (wire.Version != "native-group-member-dm-discovery:v1"
                || wire.Source.AccountId != pinnedAccountId
                || wire.Source.GroupJid != pinnedGroupJid
                || wire.Source.GroupSessionRef != authenticatedGroupSessionRef
                || wire.Enrollment is not ("disabled" or "enabled")
                || wire.Scopes is null)
                throw new InvalidDataException("member_dm_discovery_binding_mismatch");

            var parsed = new List<NativeMemberDmDiscoveredScope>(wire.Scopes.Length);
            var scopeRefs = new HashSet<string>(StringComparer.Ordinal);
            var nativePeers = new HashSet<string>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var scope in wire.Scopes)
            {
                if (scope.Status is not ("pending" or "ready" or "failed")
                    || (scope.FailureCode is not null && !FailureCodes.Contains(scope.FailureCode))
                    || (scope.Status == "failed" && scope.FailureCode is null)
                    || (scope.Status == "ready" && (wire.Enrollment != "enabled" || scope.FailureCode is not null)))
                    throw new InvalidDataException("member_dm_discovery_status_invalid");

                if (scope.Status != "ready")
                {
                    parsed.Add(new(scope.Status, scope.FailureCode, scope.ScopeRef, scope.NativeConversationId,
                        scope.NativeAliases ?? [], scope.MembershipLeaseRef, scope.LeaseExpiresAt,
                        scope.Reader?.Endpoint, scope.Reader?.RouteBindingRef, scope.Reader?.EffectiveConfigRevision,
                        scope.Epoch, scope.InitialCursor is null ? null : new(scope.InitialCursor.Version,
                            scope.InitialCursor.Epoch, scope.InitialCursor.SessionRef, scope.InitialCursor.Sequence)));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(scope.ScopeRef) || scope.ScopeRef == authenticatedGroupSessionRef
                    || !scopeRefs.Add(scope.ScopeRef)
                    || !RegexCanonicalPn(scope.NativeConversationId)
                    || !nativePeers.Add(scope.NativeConversationId!)
                    || string.IsNullOrWhiteSpace(scope.MembershipLeaseRef)
                    || string.IsNullOrWhiteSpace(scope.Epoch)
                    || scope.Reader is null || scope.Reader.Version != "inbound-observation:v1"
                    || !LoopbackReaderEndpoint(scope.Reader.Endpoint)
                    || string.IsNullOrWhiteSpace(scope.Reader.RouteBindingRef)
                    || string.IsNullOrWhiteSpace(scope.Reader.EffectiveConfigRevision)
                    || scope.InitialCursor is null || scope.InitialCursor.Version != "inbound-observation:v1"
                    || scope.InitialCursor.Epoch != scope.Epoch || scope.InitialCursor.SessionRef != scope.ScopeRef
                    || scope.InitialCursor.Sequence != 0 || scope.NativeAliases is not { Length: > 0 }
                    || scope.NativeAliases.Any(alias => !RegexNativeAlias(alias))
                    || scope.NativeAliases.Distinct(StringComparer.Ordinal).Count() != scope.NativeAliases.Length
                    || !scope.NativeAliases.Contains(scope.NativeConversationId!, StringComparer.Ordinal))
                    throw new InvalidDataException("member_dm_discovery_ready_scope_invalid");

                foreach (var alias in scope.NativeAliases)
                {
                    if (aliases.TryGetValue(alias, out var previousPeer) && previousPeer != scope.NativeConversationId)
                        throw new InvalidDataException("member_dm_discovery_alias_conflict");
                    aliases[alias] = scope.NativeConversationId!;
                }
                parsed.Add(new(scope.Status, null, scope.ScopeRef, scope.NativeConversationId,
                    scope.NativeAliases, scope.MembershipLeaseRef, scope.LeaseExpiresAt,
                    scope.Reader.Endpoint, scope.Reader.RouteBindingRef, scope.Reader.EffectiveConfigRevision,
                    scope.Epoch, new(scope.InitialCursor.Version, scope.InitialCursor.Epoch,
                        scope.InitialCursor.SessionRef, scope.InitialCursor.Sequence)));
            }
            return new(wire.Enrollment, parsed);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        { throw new InvalidDataException("member_dm_discovery_invalid", e); }
    }

    private static bool LoopbackReaderEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == "http" && uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static bool RegexCanonicalPn(string? value) => value is not null
        && System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[0-9]+@s\.whatsapp\.net\z");

    private static bool RegexNativeAlias(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[0-9]+(?::[0-9]+)?@(s\.whatsapp\.net|lid)\z");

    private sealed record DiscoveryWire(string Version, SourceWire Source, string Enrollment, ScopeWire[] Scopes);
    private sealed record SourceWire(string AccountId, string GroupJid, string GroupSessionRef);
    private sealed record ScopeWire(string Status, string? ScopeRef, string? NativeConversationId, string[]? NativeAliases,
        string? MembershipLeaseRef, long? LeaseExpiresAt, ReaderWire? Reader, string? Epoch,
        CursorWire? InitialCursor, string? FailureCode);
    private sealed record ReaderWire(string Endpoint, string Version, string RouteBindingRef, string EffectiveConfigRevision);
    private sealed record CursorWire(string Version, string Epoch, string SessionRef, long Sequence);
}
