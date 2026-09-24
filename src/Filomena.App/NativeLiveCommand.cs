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
    bool AllowNativeGroupReplies = false)
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
    bool ContinuousGroup = false, NativeObservationCursor? ContinueAfter = null, bool AllowNativeGroupParticipants = false);

public static class NativeLiveCommand
{
    // An operator-reviewed cursor is authority only inside the still-open, exact current stream.
    public static NativeObservationCursor ValidateContinuation(NativeObservationCursor after, NativeObservationCursor current)
    {
        if (current.Version != "inbound-observation:v2" || after.Version != current.Version
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
                "--authorize-continuous-group" => 0,
                var flag when flag.StartsWith("--authorize-interactions=", StringComparison.Ordinal)
                    && flag.Length == "--authorize-interactions=1".Length && flag[^1] is >= '1' and <= '5'
                    => flag[^1] - '0',
                _ => throw new InvalidDataException()
            };
            var file = Path.GetFullPath(args[1]);
            var directory = Path.GetDirectoryName(file)!;
            live = JsonSerializer.Deserialize<NativeLiveConfig>(await File.ReadAllTextAsync(file, token), Json) ?? throw new JsonException();
            if (live.ContinuousGroup != (authorizedBudget == 0)
                || (live.ContinuousGroup ? !live.AllowExactGroupDelivery || live.MaxInteractions != 1
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
            var expectedContract = live.AllowNativeGroupReplies ? "whatsapp-bound-group-reply:v1"
                : live.AllowExactGroupDelivery ? "whatsapp-bound-group-text:v1" : "whatsapp-bound-text:v1";
            if (proof.Version != "native-live-capabilities:v1" || proof.BoundSendContract != expectedContract
                || (live.AllowNativeGroupReplies && !live.AllowExactGroupDelivery)
                || proof.Capabilities != live.Capabilities || !proof.Capabilities.Complete
                || proof.MaxInteractions != live.MaxInteractions
                || proof.ContinuousGroup != live.ContinuousGroup
                || proof.AllowNativeGroupParticipants != live.AllowNativeGroupParticipants
                || (live.AllowNativeGroupParticipants && !live.AllowExactGroupDelivery)
                || proof.ContinueAfter != live.ContinueAfter
                || (live.ContinueAfter is not null && !live.ContinuousGroup)
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
