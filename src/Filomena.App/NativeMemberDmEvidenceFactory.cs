using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;

namespace Filomena.App;

public sealed record NativeMemberDmEvidenceArtifact(NativeLiveConfig LiveConfig,
    string PinnedProjectRevision, string NativeApprovedRevision);

/// <summary>Generates a new per-scope proof from an operator template and native-validated facts.</summary>
public static class NativeMemberDmEvidenceFactory
{
    private const string MemberDmContract = "whatsapp-bound-member-dm-text:v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static NativeMemberDmEvidenceArtifact Generate(NativeLiveConfig approvedTemplate,
        NativeConversationSupervisor.ValidatedMemberDmScopeFacts facts, string privateSessionDirectory)
    {
        ArgumentNullException.ThrowIfNull(approvedTemplate);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateSessionDirectory);
        if (!Directory.Exists(privateSessionDirectory))
            throw new DirectoryNotFoundException("member_dm_private_session_directory_required");
        if (!approvedTemplate.ManagedMemberDm || !approvedTemplate.ContinuousDm || approvedTemplate.ContinuousGroup
            || approvedTemplate.MaxInteractions != 1 || approvedTemplate.AllowExactGroupDelivery
            || approvedTemplate.AllowNativeGroupParticipants || approvedTemplate.AllowNativeGroupReplies
            || approvedTemplate.ContinueAfter is not null || !approvedTemplate.Capabilities.Complete)
            throw new InvalidDataException("member_dm_operator_template_not_approved");
        if (string.IsNullOrWhiteSpace(facts.PinnedProjectRevision)
            || string.IsNullOrWhiteSpace(facts.AccountId)
            || !System.Text.RegularExpressions.Regex.IsMatch(facts.SourceGroupJid, @"\A[0-9]+(?:-[0-9]+)?@g\.us\z")
            || !System.Text.RegularExpressions.Regex.IsMatch(facts.NativeConversationId, @"\A[0-9]+@s\.whatsapp\.net\z")
            || facts.NativeAliases.Count == 0
            || !facts.NativeAliases.Contains(facts.NativeConversationId, StringComparer.Ordinal)
            || facts.NativeAliases.Distinct(StringComparer.Ordinal).Count() != facts.NativeAliases.Count
            || facts.NativeAliases.Any(alias => !System.Text.RegularExpressions.Regex.IsMatch(alias,
                @"\A[0-9]+(?::[0-9]+)?@(s\.whatsapp\.net|lid)\z"))
            || string.IsNullOrWhiteSpace(facts.MembershipLeaseRef)
            || string.IsNullOrWhiteSpace(facts.ScopeRef)
            || string.IsNullOrWhiteSpace(facts.NativeApprovedRevision)
            || string.IsNullOrWhiteSpace(facts.RouteBindingRef)
            || facts.ContinueAfter is not { Version: "inbound-observation:v1", Sequence: 0 }
            || facts.ContinueAfter.SessionRef != facts.ScopeRef
            || string.IsNullOrWhiteSpace(facts.ContinueAfter.Epoch)
            || NativeObservationPreflight.RequiredCapabilities.Any(capability =>
                !facts.ObservationCapabilities.GetValueOrDefault(capability)))
            throw new InvalidDataException("member_dm_native_facts_incomplete");

        var nativeCapabilities = facts.ObservationCapabilities.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var evidence = new NativeLiveCapabilityEvidence(
            "native-live-capabilities:v1", facts.NativeApprovedRevision, facts.ScopeRef,
            facts.RouteBindingRef, MemberDmContract, approvedTemplate.Capabilities,
            MaxInteractions: 1,
            RevalidateReceiverWithinWindow: approvedTemplate.RevalidateReceiverWithinWindow,
            ContinuousGroup: false,
            ContinueAfter: facts.ContinueAfter,
            AllowNativeGroupParticipants: false,
            ContinuousDm: true,
            ManagedMemberDm: true,
            NativeConversationId: facts.NativeConversationId,
            MembershipLeaseRef: facts.MembershipLeaseRef,
            NativeObservationCapabilities: nativeCapabilities,
            NativeAccountId: facts.AccountId,
            NativeSourceGroupJid: facts.SourceGroupJid,
            NativeAliases: facts.NativeAliases.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, Json);
        var path = Path.Combine(Path.GetFullPath(privateSessionDirectory),
            "native-member-dm-capability-" + Guid.NewGuid().ToString("N") + ".json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new(approvedTemplate with
        {
            CapabilityEvidenceFile = path,
            CapabilityEvidenceSha256 = hash,
            ContinueAfter = facts.ContinueAfter,
            MemberDmSourceGroupJid = facts.SourceGroupJid,
        }, facts.PinnedProjectRevision, facts.NativeApprovedRevision);
    }
}
