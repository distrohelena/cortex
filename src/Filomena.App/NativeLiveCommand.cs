using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Mcp;
using Filomena.OpenClaw.Observation;

namespace Filomena.App;

// Host/deployment document only. Capabilities are reviewed evidence, never inferred from a project.
public sealed record NativeLiveConfig(string NativeConfigFile, OpenClawMcpClientOptions Mcp,
    string TransportRoute, string SpeakerName, int MaxSinglePartCharacters,
    TransportSafetyCapabilities Capabilities, string CapabilityEvidenceFile, string CapabilityEvidenceSha256,
    int MaxInteractions = 1, bool RevalidateReceiverWithinWindow = false, bool AllowExactGroupDelivery = false,
    bool ContinuousGroup = false, NativeObservationCursor? ContinueAfter = null, bool AllowNativeGroupParticipants = false,
    bool AllowNativeGroupReplies = false, bool ContinuousDm = false, bool ManagedMemberDm = false,
    string? MemberDmSourceGroupJid = null)
{
    internal NativeLiveCapabilityEvidence? Evidence { get; init; }

    public void ValidateReplyWiring(OutboundBindings roles)
    {
        if (AllowNativeGroupReplies != (roles.ReplyTargetPorts.Count > 0)
            || (AllowNativeGroupReplies && !AllowExactGroupDelivery)
            || roles.ReplyTargetPorts.Any(pair => !roles.CandidatePorts.ContainsKey(pair.Key)
                || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidDataException("unapproved_native_reply_wiring");
    }

    public LiveRouteBinding BindRoute(ShadowSessionBinding binding) => LiveRouteBindings.FromShadow(
        binding, TransportRoute, binding.Session.NativeConversationId, SpeakerName, MaxSinglePartCharacters)
        with { AllowExactGroupDelivery = AllowExactGroupDelivery };
}

internal sealed record NativeLiveCapabilityEvidence(string Version, string ApprovedRevision,
    string ObservationSessionRef, string RouteBindingRef, string BoundSendContract,
    TransportSafetyCapabilities Capabilities, int MaxInteractions = 1, bool RevalidateReceiverWithinWindow = false,
    bool ContinuousGroup = false, NativeObservationCursor? ContinueAfter = null, bool AllowNativeGroupParticipants = false,
    bool ContinuousDm = false, bool ManagedMemberDm = false, string? NativeConversationId = null,
    string? MembershipLeaseRef = null, IReadOnlyDictionary<string, bool>? NativeObservationCapabilities = null,
    string? NativeAccountId = null, string? NativeSourceGroupJid = null, string[]? NativeAliases = null);

public static class NativeLiveCommand
{
    // An operator-reviewed cursor is authority only inside the still-open, exact current stream.
    public static NativeObservationCursor ValidateContinuation(NativeObservationCursor after, NativeObservationCursor current)
    {
        if (current.Version is not ("inbound-observation:v1" or "inbound-observation:v2") || after.Version != current.Version
            || string.IsNullOrWhiteSpace(current.Epoch) || after.Epoch != current.Epoch
            || string.IsNullOrWhiteSpace(current.SessionRef) || after.SessionRef != current.SessionRef
            || after.Sequence < 0 || after.Sequence > current.Sequence)
            throw new InvalidDataException("invalid_live_continuation");
        return after;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken token)
    {
        NativeLiveConfig live;
        string nativeFile;
        try
        {
            if (args.Length != 3 || args[0] != "--native-live")
                throw new InvalidDataException();
            var authorizedBudget = args[2] switch
            {
                "--authorize-one-response" => 1,
                "--authorize-five-interactions" => 5,
                "--authorize-continuous-group" or "--authorize-continuous-dm" => 0,
                var flag when flag.StartsWith("--authorize-interactions=", StringComparison.Ordinal)
                    && flag.Length == "--authorize-interactions=1".Length && flag[^1] is >= '1' and <= '5'
                    => flag[^1] - '0',
                _ => throw new InvalidDataException()
            };
            var file = Path.GetFullPath(args[1]);
            var directory = Path.GetDirectoryName(file)!;
            live = JsonSerializer.Deserialize<NativeLiveConfig>(await File.ReadAllTextAsync(file, token), Json) ?? throw new JsonException();
            var continuousGroupFlag = args[2] == "--authorize-continuous-group";
            var continuousDmFlag = args[2] == "--authorize-continuous-dm";
            if (live.ContinuousGroup != continuousGroupFlag || live.ContinuousDm != continuousDmFlag
                || (live.ContinuousGroup && live.ContinuousDm)
                || (live.ManagedMemberDm && !live.ContinuousDm)
                || (live.ContinuousGroup ? !live.AllowExactGroupDelivery || live.MaxInteractions != 1
                    : live.ContinuousDm ? live.AllowExactGroupDelivery || live.AllowNativeGroupParticipants
                        || live.AllowNativeGroupReplies || live.MaxInteractions != 1
                    : live.MaxInteractions != authorizedBudget))
                throw new InvalidDataException();
            if (!live.Capabilities.Complete || string.IsNullOrWhiteSpace(live.TransportRoute)
                || string.IsNullOrWhiteSpace(live.SpeakerName) || string.IsNullOrWhiteSpace(live.Mcp.Command)
                || live.MaxSinglePartCharacters is < 1 or > ShadowOutboundHost.NativeSinglePartCeiling)
                throw new InvalidDataException();
            var evidence = await File.ReadAllBytesAsync(Path.GetFullPath(live.CapabilityEvidenceFile, directory), token);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidence)) != live.CapabilityEvidenceSha256)
                throw new InvalidDataException();
            var proof = JsonSerializer.Deserialize<NativeLiveCapabilityEvidence>(evidence, Json) ?? throw new JsonException();
            var expectedContract = live.ManagedMemberDm ? "whatsapp-bound-member-dm-text:v1"
                : live.AllowNativeGroupReplies ? "whatsapp-bound-group-reply:v1"
                : live.AllowExactGroupDelivery ? "whatsapp-bound-group-text:v1" : "whatsapp-bound-text:v1";
            if (proof.Version != "native-live-capabilities:v1" || proof.BoundSendContract != expectedContract
                || (live.AllowNativeGroupReplies && !live.AllowExactGroupDelivery)
                || proof.Capabilities != live.Capabilities || !proof.Capabilities.Complete
                || proof.MaxInteractions != live.MaxInteractions
                || proof.ContinuousGroup != live.ContinuousGroup
                || proof.ContinuousDm != live.ContinuousDm
                || proof.ManagedMemberDm != live.ManagedMemberDm
                || (live.ManagedMemberDm
                    && (!System.Text.RegularExpressions.Regex.IsMatch(proof.NativeConversationId ?? "", @"\A[0-9]+@s\.whatsapp\.net\z")
                        || string.IsNullOrWhiteSpace(proof.MembershipLeaseRef)
                        || string.IsNullOrWhiteSpace(proof.NativeAccountId)
                        || string.IsNullOrWhiteSpace(proof.NativeSourceGroupJid)
                        || proof.NativeSourceGroupJid != live.MemberDmSourceGroupJid
                        || !System.Text.RegularExpressions.Regex.IsMatch(proof.NativeSourceGroupJid, @"\A[0-9]+(?:-[0-9]+)?@g\.us\z")
                        || proof.NativeAliases is not { Length: > 0 }
                        || !proof.NativeAliases.Contains(proof.NativeConversationId, StringComparer.Ordinal)
                        || proof.NativeAliases.Any(alias => !System.Text.RegularExpressions.Regex.IsMatch(alias,
                            @"\A[0-9]+(?::[0-9]+)?@(s\.whatsapp\.net|lid)\z"))
                        || proof.NativeAliases.Distinct(StringComparer.Ordinal).Count() != proof.NativeAliases.Length
                        || proof.NativeObservationCapabilities is null
                        || Filomena.Core.Shadow.NativeObservationPreflight.RequiredCapabilities.Any(capability =>
                            !proof.NativeObservationCapabilities.GetValueOrDefault(capability))))
                || (!live.ManagedMemberDm && (proof.NativeConversationId is not null || proof.MembershipLeaseRef is not null
                    || proof.NativeObservationCapabilities is not null || proof.NativeAccountId is not null
                    || proof.NativeSourceGroupJid is not null || proof.NativeAliases is not null
                    || live.MemberDmSourceGroupJid is not null))
                || proof.AllowNativeGroupParticipants != live.AllowNativeGroupParticipants
                || (live.AllowNativeGroupParticipants && !live.AllowExactGroupDelivery)
                || proof.ContinueAfter != live.ContinueAfter
                || (live.ContinueAfter is not null && !live.ContinuousGroup && !live.ContinuousDm)
                || proof.RevalidateReceiverWithinWindow != live.RevalidateReceiverWithinWindow
                || string.IsNullOrWhiteSpace(proof.ApprovedRevision) || string.IsNullOrWhiteSpace(proof.ObservationSessionRef)
                || string.IsNullOrWhiteSpace(proof.RouteBindingRef)) throw new InvalidDataException();
            live = live with { Evidence = proof };
            nativeFile = Path.GetFullPath(live.NativeConfigFile, directory);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            await output.WriteLineAsync("{\"status\":\"FAIL\",\"diagnostic\":\"invalid_live_authorization\"}");
            return 2;
        }
        return await NativeShadowCommand.RunCoreAsync(["--native-shadow", nativeFile], input, output, token, live);
    }
}
