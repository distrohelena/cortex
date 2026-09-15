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
public sealed record NativeObservationInspection(NativeObservationSourceStatus Source, string Revision,
    string SelfIdentityRef, string PeerActorRef, IReadOnlyList<string> PathDiagnostics);

// A serialized report or project-supplied revision cannot mint this reader-local approval.
public sealed class NativeObservationApproval
{
    internal object Owner { get; }
    internal NativeObservationInspection Inspection { get; }
    public string Revision => Inspection.Revision;
    internal NativeObservationApproval(object owner, NativeObservationInspection inspection) { Owner = owner; Inspection = inspection; }
}

/// <summary>HTTP reader only: no tool client, sender, model, ack or listener loop.</summary>
public sealed class NativeObservationReader
{
    private const string Version = "inbound-observation:v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly string token, sourceSession, selfActor, peerActor;
    private readonly ShadowSessionBinding binding;
    private readonly SessionKey transportSession;
    private readonly object authority = new();
    private readonly Dictionary<string, string> actorBindings = new(StringComparer.Ordinal);

    // Cross-language host binding, not message identity and not authorization by itself.
    public static string RouteBindingRef(SessionKey session) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { "native-route", session.ChannelId, session.AccountId, session.NativeConversationId },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

    public NativeObservationReader(HttpClient client, Uri endpoint, string bearerToken, ShadowSessionBinding binding,
        string expectedSourceSessionRef, string selfActor, string peerActor, SessionKey? transportSession = null)
    {
        if (endpoint.Scheme != "http" || endpoint.Host is not ("127.0.0.1" or "[::1]" or "::1")
            || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(expectedSourceSessionRef)
            || !binding.IsSelf(selfActor) || binding.IsSelf(peerActor) || selfActor == peerActor)
            throw new ArgumentException("Invalid native observation host binding.");
        this.client = client; this.endpoint = endpoint; token = bearerToken; this.binding = binding;
        sourceSession = expectedSourceSessionRef; this.selfActor = selfActor; this.peerActor = peerActor;
        this.transportSession = transportSession ?? binding.Session;
        if ((this.transportSession with { NativeConversationId = binding.Session.NativeConversationId }) != binding.Session
            || !System.Text.RegularExpressions.Regex.IsMatch(this.transportSession.NativeConversationId, @"^\d+@(lid|s\.whatsapp\.net)$"))
            throw new ArgumentException("Invalid host-owned transport alias.");
    }

    public async Task<NativeObservationInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var root = await GetAsync("status", "", cancellationToken);
        try
        {
            CheckVersion(root); Exact(Text(root, "sessionRef"), sourceSession);
            Exact(Text(root, "channel"), binding.Session.ChannelId); Exact(Text(root, "kind"), "dm");
            Exact(Text(root, "routeBindingRef"), RouteBindingRef(transportSession));
            var health = Text(root, "health"); ValidateHealth(health, false);
            var dispatch = root.GetProperty("dispatch"); Exact(Text(dispatch, "version"), "dispatch-inspection:v1");
            var status = Text(dispatch, "status");
            if (status is not ("active" or "inactive" or "unknown")) throw new NativeObservationException("invalid_output");
            var revision = dispatch.GetProperty("effectiveConfigRevision").ValueKind == JsonValueKind.Null ? "" : Text(dispatch, "effectiveConfigRevision");
            var self = root.GetProperty("selfIdentityRef").ValueKind == JsonValueKind.Null ? "" : Text(root, "selfIdentityRef");
            var peer = Text(root, "peerActorRef");
            var capabilities = root.GetProperty("capabilities").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetBoolean(), StringComparer.Ordinal);
            var paths = dispatch.GetProperty("paths").EnumerateArray().Select(p => Text(p, "family") + ":" + Text(p, "status")).ToArray();
            return new(new(Version, health, sourceSession, status, capabilities), revision, self, peer, paths);
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("invalid_output"); }
    }

    public async Task<NativeObservationApproval> PreflightAsync(CancellationToken cancellationToken)
    {
        var inspected = await InspectAsync(cancellationToken);
        ValidateHealth(inspected.Source.Health, true);
        if (inspected.Source.DispatchStatus != "inactive") throw new NativeObservationException("dispatch_not_isolated");
        if (NativeObservationPreflight.Check(inspected.Source, sourceSession).Any(c => !c.Passed)
            || string.IsNullOrWhiteSpace(inspected.Revision) || string.IsNullOrWhiteSpace(inspected.SelfIdentityRef)
            || inspected.SelfIdentityRef == inspected.PeerActorRef) throw new NativeObservationException("capability_missing");
        BindActor(inspected.SelfIdentityRef, selfActor); BindActor(inspected.PeerActorRef, peerActor);
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
            // Only native text variants. No payload/JID envelope goes to the model.
            // Unsupported variants fail explicitly, rather than silently losing an event.
            var content = record.Payload.GetProperty("message").GetProperty("message");
            var text = content.TryGetProperty("conversation", out var conversation) && conversation.ValueKind == JsonValueKind.String
                ? conversation.GetString() : content.TryGetProperty("extendedTextMessage", out var extended) ? Text(extended, "text") : null;
            if (string.IsNullOrWhiteSpace(text)) throw new NativeObservationException("unsupported_payload");
            var timestamp = record.OccurredAt ?? DateTimeOffset.FromUnixTimeMilliseconds(record.Payload.GetProperty("receivedAt").GetInt64());
            var session = binding.Session;
            var person = MemoryIdentity.Person(binding.ConversationNamespace, actor, null);
            var inbound = new CanonicalShadowInbound(record.JournalEpoch, record.Sequence, session, SessionEntryKind.Dm, binding.ConversationNamespace, actor,
                new(record.NativeMessageId, session.ChannelId, session.AccountId, session.NativeConversationId, timestamp,
                    SessionMessageDirection.Inbound, SessionEntryKind.Dm, JsonSerializer.SerializeToElement(new
                    {
                        texto = text,
                        remetente = new { nome = "Pessoa", stableId = person.ActorStableId },
                        canal = new { nome = "DM", nativeId = session.NativeConversationId },
                        reply = (object?)null
                    })), record.FromMe);
            if (!binding.Accepts(inbound)) throw new NativeObservationException("integrity_violation");
            return inbound;
        }
        catch (NativeObservationException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new NativeObservationException("unsupported_payload"); }
    }

    private async Task<JsonElement> GetAsync(string operation, string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "inbound-observation/v1/" + operation + "?session=" + Uri.EscapeDataString(sourceSession) + query));
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
        if (cursor.Version != Version) throw new NativeObservationException("version_mismatch");
        Exact(cursor.SessionRef, sourceSession);
        if (string.IsNullOrWhiteSpace(cursor.Epoch) || cursor.Sequence < 0 || cursor.Sequence > 9007199254740991) throw new NativeObservationException("invalid_cursor");
    }
    private static void CheckVersion(JsonElement root) { if (Text(root, "version") != Version) throw new NativeObservationException("version_mismatch"); }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() is { Length: > 0 } text ? text : throw new NativeObservationException("invalid_output");
    private static void Exact(string actual, string expected) { if (!StringComparer.Ordinal.Equals(actual, expected)) throw new NativeObservationException("integrity_violation"); }
    private static void ValidateHealth(string health, bool requireHealthy)
    {
        if (health == "no_observation_epoch") throw new NativeObservationException("no_observation_epoch");
        if (health is not ("healthy" or "degraded" or "gap_detected" or "cursor_expired" or "unavailable")) throw new NativeObservationException("invalid_output");
        if (requireHealthy && health != "healthy") throw new NativeObservationException(health);
    }
}
