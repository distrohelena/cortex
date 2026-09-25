using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Security.Cryptography;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Shadow;
using Filomena.Core.Memory;

namespace Filomena.OpenClaw.Observation;

public sealed class NativeObservationException(string code) : Exception(code) { public string Code { get; } = code; }
public sealed record NativeObservationCursor(string Version, string Epoch, string SessionRef, long Sequence);
public sealed record NativeObservationEvent(long Sequence, string NativeMessageId, string SessionRef, string ActorRef,
    bool FromMe, string SelfIdentityRef, DateTimeOffset? OccurredAt, JsonElement Payload, string JournalEpoch);
public sealed record NativeObservationBatch(IReadOnlyList<NativeObservationEvent> Records, NativeObservationCursor Cursor, bool EpochClosed);
public sealed record NativeSelfMentionIdentity(string AccountPn, string? AccountLid, string DisplayName);
public sealed record NativeObservationInspection(NativeObservationSourceStatus Source, string Revision,
    string SelfIdentityRef, string PeerActorRef, IReadOnlyList<string> PathDiagnostics)
{
    internal NativeReceiverScope? ReceiverScope { get; init; }
    internal IReadOnlyList<string> ParticipantActorRefs { get; init; } = [];
    public string RouteBindingRef { get; init; } = "";
    public string? ReceiverEpoch => ReceiverScope?.Epoch;
}

// A serialized report or project-supplied revision cannot mint this reader-local approval.
public sealed class NativeObservationApproval
{
    internal object Owner { get; }
    internal NativeObservationInspection Inspection { get; }
    public string Revision => Inspection.Revision;
    internal NativeObservationApproval(object owner, NativeObservationInspection inspection) { Owner = owner; Inspection = inspection; }
}

/// <summary>HTTP reader only: no tool client, sender, model, ack or listener loop.</summary>
public sealed partial class NativeObservationReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly string token, sourceSession, selfActor, peerActor;
    private readonly string version;
    private readonly IReadOnlyDictionary<string, string> groupActorBindings;
    private readonly ShadowSessionBinding binding;
    private readonly SessionKey transportSession;
    private readonly NativeSelfMentionIdentity? selfMentionIdentity;
    private readonly object authority = new();
    private readonly Dictionary<string, string> actorBindings = new(StringComparer.Ordinal);

    public Uri BoundEndpoint => endpoint;
    public string BoundSessionRef => sourceSession;
    public SessionKey BoundTransportSession => transportSession;

    // Cross-language host binding, not message identity and not authorization by itself.
    public static string RouteBindingRef(SessionKey session) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { "native-route", session.ChannelId, session.AccountId, session.NativeConversationId },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

    public NativeObservationReader(HttpClient client, Uri endpoint, string bearerToken, ShadowSessionBinding binding,
        string expectedSourceSessionRef, string selfActor, string peerActor, SessionKey? transportSession = null,
        IReadOnlyDictionary<string, string>? groupParticipants = null, NativeSelfMentionIdentity? selfMentionIdentity = null)
    {
        if (endpoint.Scheme != "http" || endpoint.Host is not ("127.0.0.1" or "[::1]" or "::1")
            || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(expectedSourceSessionRef)
            || !binding.IsSelf(selfActor) || binding.IsSelf(peerActor) || selfActor == peerActor)
            throw new ArgumentException("Invalid native observation host binding.");
        this.client = client; this.endpoint = endpoint; token = bearerToken; this.binding = binding;
        if (selfMentionIdentity is { } identity &&
            (!System.Text.RegularExpressions.Regex.IsMatch(identity.AccountPn, @"\A\d+@s\.whatsapp\.net\z")
            || (identity.AccountLid is not null && !System.Text.RegularExpressions.Regex.IsMatch(identity.AccountLid, @"\A\d+@lid\z"))
            || string.IsNullOrWhiteSpace(identity.DisplayName) || identity.DisplayName.Length > 100
            || identity.DisplayName.Any(char.IsControl)))
            throw new ArgumentException("Invalid host-owned self mention identity.");
        this.selfMentionIdentity = selfMentionIdentity;
        sourceSession = expectedSourceSessionRef; this.selfActor = selfActor; this.peerActor = peerActor;
        version = binding.Kind == SessionEntryKind.Group ? "inbound-observation:v2" : "inbound-observation:v1";
        if (binding.Kind == SessionEntryKind.Group)
        {
            if (groupParticipants is null || groupParticipants.Count == 0
                || !groupParticipants.Values.Contains(peerActor, StringComparer.Ordinal)
                || groupParticipants.Any(pair =>
                    !System.Text.RegularExpressions.Regex.IsMatch(pair.Key, @"^\d+@(lid|s\.whatsapp\.net)$")
                    || !binding.IsPeer(pair.Value)))
                throw new ArgumentException("Invalid trusted group participants.");
            groupActorBindings = groupParticipants.ToDictionary(
                pair => ActorRef(expectedSourceSessionRef, pair.Key), pair => pair.Value, StringComparer.Ordinal);
            if (groupActorBindings.Count != groupParticipants.Count)
                throw new ArgumentException("Ambiguous trusted group participants.");
        }
        else
        {
            if (groupParticipants is not null) throw new ArgumentException("Group participants on DM binding.");
            groupActorBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        this.transportSession = transportSession ?? binding.Session;
        if ((this.transportSession with { NativeConversationId = binding.Session.NativeConversationId }) != binding.Session
            || (binding.Kind == SessionEntryKind.Group
                ? this.transportSession.NativeConversationId != binding.Session.NativeConversationId
                : !System.Text.RegularExpressions.Regex.IsMatch(this.transportSession.NativeConversationId, @"^\d+@(lid|s\.whatsapp\.net)$")))
            throw new ArgumentException("Invalid host-owned transport alias.");
    }

    private static string ActorRef(string sessionRef, string nativeActorId) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { sessionRef, "actor", nativeActorId },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

    public async Task<NativeObservationInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var root = await GetAsync("status", "", cancellationToken);
        try
        {
            CheckVersion(root); Exact(Text(root, "sessionRef"), sourceSession);
            Exact(Text(root, "channel"), binding.Session.ChannelId);
            Exact(Text(root, "kind"), binding.Kind == SessionEntryKind.Group ? "group" : "dm");
            Exact(Text(root, "routeBindingRef"), RouteBindingRef(transportSession));
            var health = Text(root, "health"); ValidateHealth(health, false);
            var dispatch = root.GetProperty("dispatch"); Exact(Text(dispatch, "version"), "dispatch-inspection:v1");
            var status = Text(dispatch, "status");
            if (status is not ("active" or "inactive" or "unknown")) throw new NativeObservationException("invalid_output");
            var revision = dispatch.GetProperty("effectiveConfigRevision").ValueKind == JsonValueKind.Null ? "" : Text(dispatch, "effectiveConfigRevision");
            var self = root.GetProperty("selfIdentityRef").ValueKind == JsonValueKind.Null ? "" : Text(root, "selfIdentityRef");
            var peer = binding.Kind == SessionEntryKind.Group ? "" : Text(root, "peerActorRef");
            var participants = binding.Kind == SessionEntryKind.Group
                ? root.GetProperty("participantActorRefs").EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : [];
            if (binding.Kind == SessionEntryKind.Group
                && (participants.Length != groupActorBindings.Count
                    || participants.Any(x => !groupActorBindings.ContainsKey(x))
                    || participants.Distinct(StringComparer.Ordinal).Count() != participants.Length))
                throw new NativeObservationException("integrity_violation");
            var capabilities = root.GetProperty("capabilities").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetBoolean(), StringComparer.Ordinal);
            var paths = dispatch.GetProperty("paths").EnumerateArray().Select(p => Text(p, "family") + ":" + Text(p, "status")).ToArray();
            return new(new(version, health, sourceSession, status, capabilities), revision, self, peer, paths)
            { ReceiverScope = ReadReceiverScope(root), ParticipantActorRefs = participants,
                RouteBindingRef = Text(root, "routeBindingRef") };
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("invalid_output"); }
    }

    /// <summary>Reads the additive member-DM descriptor from this authenticated group's existing status route.</summary>
    public async Task<JsonElement> ReadMemberDmDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (binding.Kind != SessionEntryKind.Group)
            throw new NativeObservationException("member_dm_discovery_requires_group_reader");
        var root = await GetAsync("status", "", cancellationToken);
        try
        {
            CheckVersion(root);
            Exact(Text(root, "sessionRef"), sourceSession);
            Exact(Text(root, "channel"), binding.Session.ChannelId);
            Exact(Text(root, "kind"), "group");
            Exact(Text(root, "routeBindingRef"), RouteBindingRef(binding.Session));
            return root.GetProperty("memberDmDiscovery").Clone();
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("invalid_output"); }
    }

    public async Task<NativeObservationApproval> PreflightAsync(CancellationToken cancellationToken)
    {
        var inspected = await InspectAsync(cancellationToken);
        ValidateHealth(inspected.Source.Health, true);
        if (inspected.Source.DispatchStatus != "inactive") throw new NativeObservationException(
            inspected.Source.DispatchStatus == "unknown" ? "dispatch_not_ready" : "dispatch_not_isolated");
        if (NativeObservationPreflight.Check(inspected.Source, sourceSession, version).Any(c => !c.Passed)
            || string.IsNullOrWhiteSpace(inspected.Revision) || string.IsNullOrWhiteSpace(inspected.SelfIdentityRef)
            || (binding.Kind == SessionEntryKind.Dm && inspected.SelfIdentityRef == inspected.PeerActorRef)
            || (binding.Kind == SessionEntryKind.Group && groupActorBindings.ContainsKey(inspected.SelfIdentityRef)))
            throw new NativeObservationException("capability_missing");
        if (selfMentionIdentity is { } identity)
            Exact(inspected.SelfIdentityRef, Convert.ToHexStringLower(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(new[] { sourceSession, "self", identity.AccountPn }))));
        BindActor(inspected.SelfIdentityRef, selfActor);
        if (binding.Kind == SessionEntryKind.Group)
        {
            foreach (var (source, canonical) in groupActorBindings) BindActor(source, canonical);
        }
        else BindActor(inspected.PeerActorRef, peerActor);
        return new(authority, inspected);
    }

    // Provisioning is a separate read-only proof, not authority to start a run or arm capture.
    public async Task<IReadOnlyList<ShadowPreflightCheck>> ProvisioningChecksAsync(bool beforeArm, CancellationToken cancellationToken, string? expectedRevision = null)
    {
        var root = await GetAsync("status", "", cancellationToken);
        try
        {
            CheckVersion(root); Exact(Text(root, "sessionRef"), sourceSession);
            Exact(Text(root, "routeBindingRef"), RouteBindingRef(transportSession));
            var bootstrap = root.GetProperty("bootstrap");
            var revision = Text(bootstrap, "effectiveConfigRevision");
            Exact(revision, Text(root.GetProperty("dispatch"), "effectiveConfigRevision"));
            if (expectedRevision is not null) Exact(revision, expectedRevision);
            var state = Text(bootstrap, "captureState");
            if (state is not ("off" or "armed")) throw new NativeObservationException("invalid_output");
            var hasEpoch = bootstrap.TryGetProperty("activeEpoch", out var epoch) && epoch.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(epoch.GetString());
            ShadowPreflightCheck Check(string id, bool passed, string diagnostic) => new(id, passed, "transport", diagnostic, 0);
            return new[] {
                Check("observation_bootstrap", bootstrap.GetProperty("subsystemOperational").GetBoolean(), "native_host_composition"),
                Check("journal_readable", bootstrap.GetProperty("readerAvailable").GetBoolean(), "independent_of_capture"),
                Check("journal_health", Text(bootstrap, "journalHealth") == "healthy", "native_journal_health"),
                Check("control_surface", bootstrap.GetProperty("controlAvailable").GetBoolean(), "read_only_status_no_arm"),
                Check("config_snapshot_reachable", bootstrap.GetProperty("configSnapshotAvailable").GetBoolean(), "native_config_owner"),
                Check("dispatch_inspector_reachable", bootstrap.GetProperty("dispatchInspectorAvailable").GetBoolean(), "native_dispatch_owner"),
                Check("capture_state", beforeArm ? state == "off" && !hasEpoch : state == "armed" && hasEpoch, state)
            };
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("bootstrap_unproven"); }
    }
    private void BindActor(string source, string canonical)
    {
        if (actorBindings.TryGetValue(source, out var previous) && previous != canonical) throw new NativeObservationException("integrity_violation");
        actorBindings[source] = canonical;
    }

    private void BindNativeGroupActor(JsonElement payload, string actorRef, bool fromMe)
    {
        // Only authenticated, exact-session native journal records reach here. pushName,
        // message text and model output never establish participant identity.
        var key = payload.GetProperty("message").GetProperty("key");
        Exact(Text(key, "remoteJid"), transportSession.NativeConversationId);
        if (fromMe || key.GetProperty("fromMe").GetBoolean())
            throw new NativeObservationException("integrity_violation");
        var participant = CanonicalNativeParticipant(Text(key, "participant"));
        if (!NativeParticipant(participant)) throw new NativeObservationException("integrity_violation");
        Exact(actorRef, ActorRef(sourceSession, participant));
        var alternate = key.TryGetProperty("participantAlt", out var alt) && alt.ValueKind == JsonValueKind.String
            ? CanonicalNativeParticipant(alt.GetString()!) : null;
        if (alternate is not null && !NativeParticipant(alternate))
            throw new NativeObservationException("integrity_violation");
        var phones = new[] { participant, alternate }.Where(x => x?.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (phones.Length > 1) throw new NativeObservationException("integrity_violation");
        var canonical = phones.Length == 1 ? "whatsapp:" + phones[0]
            : actorBindings.GetValueOrDefault(actorRef);
        if (canonical is null || binding.IsSelf(canonical) || !binding.IsPeer(canonical))
            throw new NativeObservationException("native_participant_identity_unresolved");
        foreach (var alias in new[] { participant, alternate }.OfType<string>())
        {
            if (binding.IsSelf("whatsapp:" + alias)
                || (actorBindings.TryGetValue(ActorRef(sourceSession, alias), out var previous) && previous != canonical))
                throw new NativeObservationException("integrity_violation");
        }
        BindActor(actorRef, canonical);
        if (alternate is not null) BindActor(ActorRef(sourceSession, alternate), canonical);
    }

    private static bool NativeParticipant(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+@(lid|s\.whatsapp\.net)$");

    private static string CanonicalNativeParticipant(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @":\d+(?=@)", "");
    public Task<NativeObservationBatch> OpenAsync(NativeObservationApproval approval, CancellationToken cancellationToken) => ReadAsync(approval, null, cancellationToken);
    public Task<NativeObservationBatch> PollAsync(NativeObservationApproval approval, NativeObservationCursor cursor, CancellationToken cancellationToken) => ReadAsync(approval, cursor, cancellationToken);

    private async Task<NativeObservationBatch> ReadAsync(NativeObservationApproval approval, NativeObservationCursor? cursor, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(approval.Owner, authority)) throw new NativeObservationException("invalid_preflight_provenance");
        if (cursor is not null) ValidateCursor(cursor);
        var query = "&revision=" + Uri.EscapeDataString(approval.Revision);
        if (cursor is not null) query += "&cursor=" + Uri.EscapeDataString(JsonSerializer.Serialize(cursor, Json));
        var root = await GetAsync(cursor is null ? "open" : "poll", query, cancellationToken);
        try
        {
            CheckVersion(root); Exact(Text(root, "sessionRef"), sourceSession); Exact(Text(root, "effectiveConfigRevision"), approval.Revision);
            ValidateHealth(Text(root, "status"), true);
            var closed = root.GetProperty("epochClosed").GetBoolean();
            if (cursor is null && closed) throw new NativeObservationException("preflight_stale");
            var next = root.GetProperty(cursor is null ? "cursor" : "nextCursor").Deserialize<NativeObservationCursor>(Json) ?? throw new NativeObservationException("invalid_output");
            ValidateCursor(next);
            if (cursor is not null) Exact(next.Epoch, cursor.Epoch);
            var expected = cursor?.Sequence ?? 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var records = new List<NativeObservationEvent>();
            foreach (var row in root.GetProperty(cursor is null ? "snapshot" : "events").EnumerateArray())
            {
                Exact(Text(row, "sessionRef"), sourceSession); Exact(Text(row, "journalEpoch"), next.Epoch);
                var sequence = row.GetProperty("sequence").GetInt64();
                if (sequence != ++expected) throw new NativeObservationException("gap_detected");
                var native = Text(row, "nativeMessageId"); if (!ids.Add(native)) throw new NativeObservationException("integrity_violation");
                var actor = Text(row, "actorRef"); var self = Text(row, "selfIdentityRef"); var fromMe = row.GetProperty("fromMe").GetBoolean();
                Exact(self, approval.Inspection.SelfIdentityRef);
                if (binding.AllowsNativeGroupParticipants && actor != self)
                    BindNativeGroupActor(row.GetProperty("payload"), actor, fromMe);
                if (!actorBindings.ContainsKey(actor) || (fromMe && actor != self)) throw new NativeObservationException("integrity_violation");
                DateTimeOffset? occurred = row.TryGetProperty("occurredAt", out var at) ? DateTimeOffset.FromUnixTimeMilliseconds(at.GetInt64()) : null;
                records.Add(new(sequence, native, sourceSession, actor, fromMe, self, occurred, row.GetProperty("payload").Clone(), next.Epoch));
            }
            if (expected != next.Sequence) throw new NativeObservationException("gap_detected");
            return new(records.AsReadOnly(), next, closed);
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { throw new NativeObservationException("invalid_output"); }
    }

    public CanonicalShadowInbound ToInbound(NativeObservationEvent record)
    {
        Exact(record.SessionRef, sourceSession);
        if (!actorBindings.TryGetValue(record.ActorRef, out var actor)) throw new NativeObservationException("integrity_violation");
        try
        {
            // Known non-text events are durably ignored, not synthesized as human text.
            // Unknown/malformed payloads still fail closed. No payload/JID reaches a model.
            var nativeMessage = record.Payload.GetProperty("message");
            var hasContent = nativeMessage.TryGetProperty("message", out var content) && content.ValueKind != JsonValueKind.Null;
            // Baileys WAProto: GROUP_CHANGE_SUBJECT=21, GROUP_CHANGE_DESCRIPTION=24.
            // These are explicit protocol notifications, not a missing/decrypt-failed text.
            var metadataOnly = !hasContent && nativeMessage.TryGetProperty("messageStubType", out var stub)
                && stub.TryGetInt32(out var stubType) && stubType is 21 or 24;
            if (!hasContent && !metadataOnly) throw new NativeObservationException("unsupported_payload");
            if (metadataOnly) content = JsonSerializer.SerializeToElement(new { });
            var text = content.TryGetProperty("conversation", out var conversation) && conversation.ValueKind == JsonValueKind.String
                ? conversation.GetString() : content.TryGetProperty("extendedTextMessage", out var extended) ? Text(extended, "text") : null;
            foreach (var media in new[] { "imageMessage", "videoMessage", "documentMessage" })
                if (text is null && content.TryGetProperty(media, out var attachment)
                    && attachment.TryGetProperty("caption", out var caption) && caption.ValueKind == JsonValueKind.String)
                    text = caption.GetString();
            var skipNonText = metadataOnly || string.IsNullOrWhiteSpace(text) && (IsKnownNonText(content) || IsEncryptedEdit(content));
            if (string.IsNullOrWhiteSpace(text) && !skipNonText) throw new NativeObservationException("unsupported_payload");
            var timestamp = record.OccurredAt ?? DateTimeOffset.FromUnixTimeMilliseconds(record.Payload.GetProperty("receivedAt").GetInt64());
            var session = binding.Session;
            // Account echoes can use the agent identity, not a human Person. The inbox
            // durably suppresses these before memory lookup or conversational execution.
            var stableActor = record.FromMe || binding.IsSelf(actor) ? actor
                : MemoryIdentity.Person(binding.ConversationNamespace, actor, null).ActorStableId;
            var inbound = new CanonicalShadowInbound(record.JournalEpoch, record.Sequence, session, binding.Kind, binding.ConversationNamespace, actor,
                new(record.NativeMessageId, session.ChannelId, session.AccountId, session.NativeConversationId, timestamp,
                    SessionMessageDirection.Inbound, binding.Kind, JsonSerializer.SerializeToElement(new
                    {
                        texto = RenderSelfMentions(content, text ?? ""),
                        // This contract supplies identity, not a trusted display name.
                        remetente = new { nome = "", stableId = stableActor },
                        canal = new { nome = binding.Kind == SessionEntryKind.Group ? "Grupo" : "DM", nativeId = session.NativeConversationId },
                        reply = ReadReply(content)
                    })), record.FromMe, skipNonText);
            if (!binding.Accepts(inbound)) throw new NativeObservationException("integrity_violation");
            return inbound;
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("unsupported_payload"); }
    }

    private object? ReadReply(JsonElement content)
    {
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !property.Value.TryGetProperty("contextInfo", out var context)) continue;
            if (!context.TryGetProperty("stanzaId", out var id)) continue;
            if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                throw new NativeObservationException("invalid_reply");
            if (context.TryGetProperty("remoteJid", out var remote)
                && remote.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(remote.GetString()))
                Exact(remote.GetString()!, transportSession.NativeConversationId);
            string? text = null;
            if (context.TryGetProperty("quotedMessage", out var quoted))
            {
                if (quoted.TryGetProperty("conversation", out var conversation)) text = conversation.GetString();
                else if (quoted.TryGetProperty("extendedTextMessage", out var extended)
                    && extended.TryGetProperty("text", out var body)) text = body.GetString();
                else foreach (var kind in new[] { "imageMessage", "videoMessage", "documentMessage" })
                    if (quoted.TryGetProperty(kind, out var media) && media.TryGetProperty("caption", out var caption))
                        text = caption.GetString();
            }
            // A provider-carried quote is context supplied by the sender, not an independent
            // authenticated inbound or authority to execute/resolve another conversation.
            object? sender = null;
            if (context.TryGetProperty("participant", out var participant) && participant.ValueKind == JsonValueKind.String
                && actorBindings.TryGetValue(ActorRef(sourceSession, CanonicalNativeParticipant(participant.GetString()!)), out var quotedActor))
                sender = new { nome = "", stableId = quotedActor };
            return new { id = id.GetString(), texto = text, remetente = sender, origem = "citacao_do_remetente" };
        }
        return null;
    }

    private bool IsEncryptedEdit(JsonElement content)
    {
        // Baileys SecretEncType.MESSAGE_EDIT=2. This is a control event for an
        // existing message, not decrypted conversational input. Its original
        // payload stays in the durable journal; never invent edited text.
        if (!content.TryGetProperty("secretEncryptedMessage", out var edit) || edit.ValueKind != JsonValueKind.Object
            || content.EnumerateObject().Any(p => p.Name is not ("secretEncryptedMessage" or "messageContextInfo"))
            || !edit.TryGetProperty("secretEncType", out var type)
            || !(type.ValueKind == JsonValueKind.String && type.GetString() == "MESSAGE_EDIT"
                || type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var number) && number == 2)) return false;
        if (!edit.TryGetProperty("targetMessageKey", out var target) || target.ValueKind != JsonValueKind.Object
            || Text(target, "remoteJid") != transportSession.NativeConversationId) return false;
        _ = Text(target, "id");
        _ = Text(edit, "encPayload");
        _ = Text(edit, "encIv");
        return true;
    }

    private static bool IsKnownNonText(JsonElement content)
    {
        string[] media = ["imageMessage", "videoMessage", "audioMessage", "documentMessage", "stickerMessage", "reactionMessage"];
        string[] metadata = ["senderKeyDistributionMessage", "messageContextInfo"];
        return content.ValueKind == JsonValueKind.Object
            && content.EnumerateObject().Any(p => media.Contains(p.Name) && p.Value.ValueKind == JsonValueKind.Object)
            && content.EnumerateObject().All(p => (media.Contains(p.Name) || metadata.Contains(p.Name))
                && p.Value.ValueKind == JsonValueKind.Object);
    }

    private async Task<JsonElement> GetAsync(string operation, string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint,
            "inbound-observation/" + (binding.Kind == SessionEntryKind.Group ? "v2" : "v1") + "/"
            + operation + "?session=" + Uri.EscapeDataString(sourceSession) + query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict) throw new NativeObservationException("preflight_stale_or_dispatch_changed");
            if (!response.IsSuccessStatusCode) throw new NativeObservationException("unavailable");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.Clone();
        }
        catch (HttpRequestException) { throw new NativeObservationException("unavailable"); }
        catch (JsonException) { throw new NativeObservationException("invalid_output"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new NativeObservationException("unavailable"); }
    }
    private void ValidateCursor(NativeObservationCursor cursor)
    {
        if (cursor.Version != version) throw new NativeObservationException("version_mismatch");
        Exact(cursor.SessionRef, sourceSession);
        if (string.IsNullOrWhiteSpace(cursor.Epoch) || cursor.Sequence < 0 || cursor.Sequence > 9007199254740991) throw new NativeObservationException("invalid_cursor");
    }
    private void CheckVersion(JsonElement root) { if (Text(root, "version") != version) throw new NativeObservationException("version_mismatch"); }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() is { Length: > 0 } text ? text : throw new NativeObservationException("invalid_output");
    private static void Exact(string actual, string expected) { if (!StringComparer.Ordinal.Equals(actual, expected)) throw new NativeObservationException("integrity_violation"); }
    private static void ValidateHealth(string health, bool requireHealthy)
    {
        if (health == "no_observation_epoch") throw new NativeObservationException("no_observation_epoch");
        if (health is not ("healthy" or "degraded" or "gap_detected" or "cursor_expired" or "unavailable")) throw new NativeObservationException("invalid_output");
        if (requireHealthy && health != "healthy") throw new NativeObservationException(health);
    }
}
