using System.Net;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Flows;
using Cortex.Core.Shadow;
using Cortex.OpenClaw.Observation;
using Xunit;

namespace Cortex.OpenClaw.Tests;

public sealed class NativeObservationReaderTests
{
    [Fact]
    public async Task Reader_uses_the_exact_native_versioned_endpoint_base_without_double_prefix()
    {
        string? requestPath = null;
        using var client = new HttpClient(new Handler(request =>
        {
            requestPath = request.RequestUri!.PathAndQuery;
            return Status();
        }));
        var endpoint = new Uri("http://127.0.0.1:12345/inbound-observation/v1");
        var reader = new NativeObservationReader(client, endpoint, "token", Binding, "source", Self, Peer);

        await reader.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/inbound-observation/v1/status?session=source", requestPath);
    }

    [Fact]
    public async Task Reader_preserves_the_exact_optional_member_admission_generation_projection()
    {
        var memberEvent = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        memberEvent["membershipAuthority"] = new System.Text.Json.Nodes.JsonObject
        {
            ["version"] = "native-member-dm-admission:v1",
            ["membershipLeaseRef"] = "lease-captured-on-admission",
        };
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Status() : Batch(false, [memberEvent])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);

        var record = Assert.Single((await reader.PollAsync(approval,
            new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken)).Records);

        Assert.Equal("native-member-dm-admission:v1", record.MembershipAuthority!.Version);
        Assert.Equal("lease-captured-on-admission", record.MembershipAuthority.MembershipLeaseRef);
    }

    [Fact]
    public async Task Managed_member_dm_maps_trusted_lid_then_pn_redelivery_to_one_canonical_peer()
    {
        const string lid = "991234:7@lid", pn = "100@s.whatsapp.net";
        var replies = new Queue<System.Text.Json.Nodes.JsonNode>();
        foreach (var (sequence, alias) in new[] { (1, lid), (2, pn) })
        {
            var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
            row["sequence"] = sequence;
            row["nativeMessageId"] = "same-native-message-id";
            row["actorRef"] = ActorRef(alias);
            row["payload"]!["message"]!["key"] = JsonSerializer.SerializeToNode(new
                { remoteJid = alias, fromMe = false });
            row["payload"]!["message"]!["message"] = JsonSerializer.SerializeToNode(new { extendedTextMessage = new
                { text = "hello", contextInfo = new { stanzaId = "quoted-id", remoteJid = alias } } });
            var batch = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Batch(false, [row])))!;
            batch["nextCursor"]!["sequence"] = sequence;
            replies.Enqueue(batch);
        }
        using var client = new HttpClient(new Handler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : replies.Dequeue()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding,
            "source", Self, Peer, trustedManagedMemberDmAliases: [pn, lid]);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);

        var firstBatch = await reader.PollAsync(approval,
            new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken);
        var first = reader.ToInbound(Assert.Single(firstBatch.Records));
        var secondBatch = await reader.PollAsync(approval, firstBatch.Cursor, TestContext.Current.CancellationToken);
        var second = reader.ToInbound(Assert.Single(secondBatch.Records));

        Assert.Equal("same-native-message-id", first.Message.ProviderMessageId);
        Assert.Equal(first.Message.ProviderMessageId, second.Message.ProviderMessageId);
        Assert.Equal(Peer, first.ActorId);
        Assert.Equal(Peer, second.ActorId);
        Assert.Equal(first.Message.Message.GetProperty("remetente").GetProperty("stableId").GetString(),
            second.Message.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.Equal(Session.NativeConversationId, second.Message.Message.GetProperty("canal").GetProperty("nativeId").GetString());
        Assert.Equal("quoted-id", first.Message.Message.GetProperty("reply").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("actor_alias")]
    [InlineData("payload_remote_jid")]
    [InlineData("reply_remote_jid")]
    public async Task Managed_member_dm_rejects_alias_outside_owner_validated_set(string mutation)
    {
        const string lid = "991234@lid", foreign = "888888@s.whatsapp.net";
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row["actorRef"] = ActorRef(mutation == "actor_alias" ? foreign : lid);
        row["payload"]!["message"]!["key"] = JsonSerializer.SerializeToNode(new
            { remoteJid = mutation == "payload_remote_jid" ? foreign : lid, fromMe = false });
        if (mutation == "reply_remote_jid")
            row["payload"]!["message"]!["message"] = JsonSerializer.SerializeToNode(new { extendedTextMessage = new
                { text = "hello", contextInfo = new { stanzaId = "quoted-id", remoteJid = foreign } } });
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Status() : Batch(false, [row])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding,
            "source", Self, Peer, trustedManagedMemberDmAliases: ["100@s.whatsapp.net", lid]);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);

        if (mutation == "actor_alias")
            await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval,
                new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken));
        else
        {
            var record = Assert.Single((await reader.PollAsync(approval,
                new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken)).Records);
            Assert.Equal("integrity_violation", Assert.Throws<NativeObservationException>(() => reader.ToInbound(record)).Code);
        }
    }

    [Theory]
    [InlineData("unknown_version")]
    [InlineData("missing_lease")]
    [InlineData("extra_field")]
    public async Task Reader_rejects_unknown_or_malformed_member_admission_projection(string mutation)
    {
        var memberEvent = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        var authority = new System.Text.Json.Nodes.JsonObject
        {
            ["version"] = mutation == "unknown_version" ? "native-member-dm-admission:v2" : "native-member-dm-admission:v1",
            ["membershipLeaseRef"] = mutation == "missing_lease" ? null : "lease-captured-on-admission",
        };
        if (mutation == "extra_field") authority["untrusted"] = true;
        memberEvent["membershipAuthority"] = authority;
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Status() : Batch(false, [memberEvent])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);

        Assert.Equal("invalid_output", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval,
            new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken))).Code);
    }

    [Theory]
    [InlineData("700@s.whatsapp.net", true, false, "@Filo tá aí")]
    [InlineData("800@lid", true, false, "@Filo tá aí")]
    [InlineData("900@lid", true, false, "@900 tá aí")]
    [InlineData("800@lid", false, false, "@800 tá aí")]
    [InlineData("800@lid", true, true, "@800 tá aí")]
    public async Task Only_current_provider_mentions_of_trusted_self_are_rendered_as_the_agent_name(
        string mentioned, bool metadata, bool quotedOnly, string expected)
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["selfIdentityRef"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "source", "self", "700@s.whatsapp.net" })));
        using var client = new HttpClient(new Handler(_ => status));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer,
            selfMentionIdentity: new("700@s.whatsapp.net", "800@lid", "Filo"));
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var mentions = metadata ? new[] { mentioned } : Array.Empty<string>();
        var payload = JsonSerializer.SerializeToElement(new { message = new { message = new {
            extendedTextMessage = new { text = "@" + mentioned.Split('@')[0] + " tá aí", contextInfo = new {
                mentionedJid = quotedOnly ? Array.Empty<string>() : mentions,
                quotedMessage = new { extendedTextMessage = new { contextInfo = new { mentionedJid = mentions } } }
            } }
        } } });
        var inbound = reader.ToInbound(new(1, "native", "source", "peer-ref", false,
            status["selfIdentityRef"]!.GetValue<string>(), DateTimeOffset.UtcNow, payload, "epoch"));
        Assert.Equal(expected, inbound.Message.Message.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task Poll_revalidation_waits_without_authority_then_resumes_same_window()
    {
        var root = RenewableStatus();
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(r => { requests.Add(r.RequestUri!.AbsolutePath); return root; }));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var prior = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var cursor = new NativeObservationCursor("inbound-observation:v1", "epoch", "source", 7);
        root["dispatch"]!["status"] = "unknown";
        Assert.Null(await reader.TryRevalidateForPollingAsync(prior, cursor, TestContext.Current.CancellationToken));
        root = RenewableStatus("P2");
        Assert.NotNull(await reader.TryRevalidateForPollingAsync(prior, cursor, TestContext.Current.CancellationToken));
        Assert.Equal(7, cursor.Sequence);
        Assert.All(requests, path => Assert.EndsWith("/status", path));
        root["dispatch"]!["status"] = "active";
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.TryRevalidateForPollingAsync(prior, cursor, TestContext.Current.CancellationToken));
        root = RenewableStatus("P2");
        root["bootstrap"]!["isolationGate"]!["gateInstanceRevision"] = "new-window";
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.TryRevalidateForPollingAsync(prior, cursor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Member_DM_discovery_uses_only_the_authenticated_group_status_reader()
    {
        var paths = new List<string>();
        var memberDiscovery = new { version = "native-group-member-dm-discovery:v1", source = new { accountId = "default", groupJid = GroupSession.NativeConversationId,
            groupSessionRef = "source" }, enrollment = "enabled", scopes = Array.Empty<object>() };
        using var client = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            var root = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
            root["version"] = "inbound-observation:v2";
            root["kind"] = "group";
            root["routeBindingRef"] = NativeObservationReader.RouteBindingRef(GroupSession);
            root.AsObject().Remove("peerActorRef");
            root["participantActorRefs"] = JsonSerializer.SerializeToNode(new[] { GroupActorRef(GroupParticipant) });
            root["memberDmDiscovery"] = JsonSerializer.SerializeToNode(memberDiscovery);
            return root;
        }));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", GroupBinding, "source", Self, Peer,
            groupParticipants: new Dictionary<string, string> { [GroupParticipant] = Peer });

        var discovery = await reader.ReadMemberDmDiscoveryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("native-group-member-dm-discovery:v1", discovery.GetProperty("version").GetString());
        Assert.Equal(new[] { "/inbound-observation/v2/status?session=source" }, paths);
    }

    [Fact]
    public async Task Self_mention_identity_must_match_the_native_account_proof()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer,
            selfMentionIdentity: new("700@s.whatsapp.net", "800@lid", "Filo"));
        Assert.Equal("integrity_violation", (await Assert.ThrowsAsync<NativeObservationException>(
            () => reader.PreflightAsync(TestContext.Current.CancellationToken))).Code);
    }


    [Theory]
    [InlineData("MESSAGE_EDIT", true, true)]
    [InlineData("UNKNOWN", true, false)]
    [InlineData("MESSAGE_EDIT", false, false)]
    public async Task Encrypted_edit_is_retained_as_control_not_new_human_text(string type, bool sameSession, bool recognized)
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var payload = JsonSerializer.SerializeToElement(new { message = new { message = new {
            messageContextInfo = new { },
            secretEncryptedMessage = new { secretEncType = type, encPayload = "AQID", encIv = "AQID",
                targetMessageKey = new { id = "original-id", remoteJid = sameSession ? Binding.Session.NativeConversationId : "other@g.us" } }
        } } });
        var record = new NativeObservationEvent(227, "edit-id", "source", "peer-ref", false, "self-ref", DateTimeOffset.UtcNow, payload, "epoch");
        if (recognized)
        {
            var inbound = reader.ToInbound(record);
            Assert.True(inbound.SkipNonText);
            Assert.Equal("edit-id", inbound.Message.ProviderMessageId);
            Assert.Equal("", inbound.Message.Message.GetProperty("texto").GetString());
        }
        else Assert.Throws<NativeObservationException>(() => reader.ToInbound(record));
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(21, true)]
    [InlineData(9999, false)]
    public async Task Group_metadata_protocol_notification_is_ignored_but_unknown_stub_is_not(int stub, bool known)
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var record = new NativeObservationEvent(1, "native-id", "source", "peer-ref", false, "self-ref", DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { message = new { messageStubType = stub, messageStubParameters = new[] { "Renamed group" } } }), "epoch");
        if (known) Assert.True(reader.ToInbound(record).SkipNonText);
        else Assert.Throws<NativeObservationException>(() => reader.ToInbound(record));
    }

    [Theory]
    [InlineData(2, "No session found to decrypt message", true, true)]
    [InlineData(2, "No session found to decrypt message", false, false)]
    [InlineData(2, "unknown decrypt failure", true, false)]
    [InlineData(9999, "No session found to decrypt message", true, false)]
    public async Task Proven_ciphertext_failure_is_durable_without_executing_or_blocking_later_text(
        int stub, string diagnostic, bool correctId, bool recognized)
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var record = new NativeObservationEvent(1, "cipher-id", "source", "peer-ref", false, "self-ref", DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { message = new { key = new { id = correctId ? "cipher-id" : "other-id", remoteJid = Binding.Session.NativeConversationId },
                messageStubType = stub, messageStubParameters = new[] { diagnostic } } }), "epoch");
        if (!recognized) { Assert.Throws<NativeObservationException>(() => reader.ToInbound(record)); return; }
        var input = reader.ToInbound(record);
        Assert.True(input.SkipNonText);
        Assert.Equal("ciphertext_unavailable_requires_manual_reconciliation", input.SuppressionReason);
        Assert.Equal("", input.Message.Message.GetProperty("texto").GetString());
        var directory = Path.Combine(Path.GetTempPath(), "filomena-ciphertext-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "inbox.jsonl");
            using (var journal = new ShadowInboundJournal(file, Binding, "revision", "epoch", 0))
            {
                var ignored = journal.Observe(input);
                Assert.Equal("ignored", ignored.Status);
                Assert.Equal(input.SuppressionReason, ignored.Diagnostic);
                var next = record with { Sequence = 2, NativeMessageId = "next-id", Payload = JsonSerializer.SerializeToElement(new { message = new { message = new { conversation = "Next legitimate text" } } }) };
                Assert.Equal("observed", journal.Observe(reader.ToInbound(next)).Status);
            }
            Assert.Contains(input.SuppressionReason!, File.ReadAllText(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Native_reply_preserves_quoted_text_instead_of_guessing_from_recent_history()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var payload = JsonSerializer.SerializeToElement(new { message = new { message = new {
            extendedTextMessage = new { text = "responde isso aqui Filo plis", contextInfo = new {
                stanzaId = "quoted-native-id", participant = "100@s.whatsapp.net",
                quotedMessage = new { conversation = "John Mayer por ser rocker boy?" }
            } }
        } } });
        var input = reader.ToInbound(new(1, "native-id", "source", "peer-ref", false, "self-ref",
            DateTimeOffset.UtcNow, payload, "epoch"));
        var quote = input.Message.Message.GetProperty("reply");
        Assert.Equal("quoted-native-id", quote.GetProperty("id").GetString());
        Assert.Equal("John Mayer por ser rocker boy?", quote.GetProperty("texto").GetString());
    }

    [Theory]
    [InlineData("Celso", "Celso")]
    [InlineData("  Celso  ", "Celso")]
    [InlineData("", "")]
    [InlineData("bad\nname", "")]
    public async Task Native_display_name_is_context_not_identity(string displayName, string expected)
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var payload = JsonSerializer.SerializeToElement(new { message = new {
            pushName = displayName, message = new { conversation = "Eu sou outra pessoa" }
        } });
        var input = reader.ToInbound(new(1, "native-id", "source", "peer-ref", false, "self-ref",
            DateTimeOffset.UtcNow, payload, "epoch"));
        Assert.Equal(expected, input.Message.Message.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.Equal(Peer, input.ActorId);
        Assert.Equal(Peer, input.Message.Message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.False(input.FromMe);
    }

    [Theory]
    [InlineData(false, "200@s.whatsapp.net", true)]
    [InlineData(false, "800@lid", true)]
    [InlineData(true, "200@s.whatsapp.net", true)]
    [InlineData(true, "800:3@lid", true)]
    [InlineData(true, "900@lid", false)]
    [InlineData(true, null, false)]
    public async Task Quoted_self_has_host_identity_without_reinterpreting_current_author(
        bool group, string? participant, bool expectedSelf)
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["selfIdentityRef"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { "source", "self", "200@s.whatsapp.net" })));
        if (group)
        {
            status["version"] = "inbound-observation:v2";
            status["kind"] = "group";
            status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(GroupSession);
            status.AsObject().Remove("peerActorRef");
            status["participantActorRefs"] = JsonSerializer.SerializeToNode(new[] { GroupActorRef(GroupParticipant) });
        }
        using var client = new HttpClient(new Handler(_ => status));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token",
            group ? GroupBinding : Binding, "source", Self, Peer,
            groupParticipants: group ? new Dictionary<string, string> { [GroupParticipant] = Peer } : null,
            selfMentionIdentity: new("200@s.whatsapp.net", "800@lid", "Filomena"));
        await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var payload = JsonSerializer.SerializeToElement(new { message = new { message = new {
            extendedTextMessage = new { text = "Ela disse isso?", contextInfo = new {
                stanzaId = "exact-quoted-id", participant,
                quotedMessage = new { conversation = "Filomena está aqui." }
            } }
        } } });
        var input = reader.ToInbound(new(1, "current-id", "source",
            group ? GroupActorRef(GroupParticipant) : "peer-ref", false,
            status["selfIdentityRef"]!.GetValue<string>(), DateTimeOffset.UtcNow, payload, "epoch"));
        var message = input.Message.Message;
        var quote = message.GetProperty("reply");
        Assert.Equal(Peer, input.ActorId);
        Assert.False(input.FromMe);
        Assert.Equal("Ela disse isso?", message.GetProperty("texto").GetString());
        Assert.Equal("exact-quoted-id", quote.GetProperty("id").GetString());
        Assert.Equal("citacao_do_remetente", quote.GetProperty("origem").GetString());
        if (expectedSelf)
        {
            Assert.Equal(JsonValueKind.Object, quote.GetProperty("remetente").ValueKind);
            Assert.Equal(Self, quote.GetProperty("remetente").GetProperty("stableId").GetString());
            Assert.Equal("Filomena", quote.GetProperty("remetente").GetProperty("nome").GetString());
        }
        else Assert.Equal(JsonValueKind.Null, quote.GetProperty("remetente").ValueKind);
    }

    private static string Producer(string id) => (id == "P1" ? "00000000-0000-0000-0000-000000000001" : "00000000-0000-0000-0000-000000000002") + ":" + new string('a', 64);
    private static string ReceiverRevision(string id) => $"base:producer-{Producer(id)}:registry-2:isolation-G1";
    private static System.Text.Json.Nodes.JsonNode RenewableStatus(string producer = "P1")
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        var proof = new { status = "valid", version = "effective-runtime-config:v1", revision = "base" };
        var gate = new { gateVersion = "native-dm-isolation:v1", gateInstanceRevision = "G1",
            approvedEffectiveConfigRevision = "base", accountRef = "account", sessionRef = "source",
            coveredPathIds = new[] { "commands" }, placementProof = "placement", lifecycleState = "armed" };
        var revision = ReceiverRevision(producer);
        root["dispatch"] = JsonSerializer.SerializeToNode(new { version = "dispatch-inspection:v1", status = "inactive",
            effectiveConfigRevision = revision, effectiveSnapshotProof = proof, isolationGate = gate, producerRevision = Producer(producer),
            paths = new[] { new { family = "commands", status = "excluded", rawStatus = "active", effectiveStatus = "excluded",
                effectiveConfigRevision = "base", exclusionStatus = "excluded_by_gate", placementProof = "placement" } } });
        root["bootstrap"] = JsonSerializer.SerializeToNode(new { subsystemOperational = true, readerAvailable = true,
            journalHealth = "healthy", captureState = "armed", activeEpoch = "epoch", controlAvailable = true,
            configSnapshotAvailable = true, dispatchInspectorAvailable = true, effectiveConfigRevision = revision,
            effectiveSnapshotProof = proof, isolationGate = gate,
            legacyRecovery = new { status = "clear", effectiveConfigRevision = "base", uncoveredOwnerCount = 0,
                activeOwnerCount = 0, pendingLegacyRecoveryCount = 0, quarantinedCount = 0 } });
        return root;
    }

    [Fact]
    public async Task Receiver_revalidation_preserves_cursor_and_polls_once_with_new_approval()
    {
        var root = RenewableStatus();
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(r => {
            requests.Add(r.RequestUri!.AbsolutePath);
            if (r.RequestUri.AbsolutePath.EndsWith("/status")) return root;
            Assert.Contains(Uri.EscapeDataString(ReceiverRevision("P2")), r.RequestUri.Query);
            var batch = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Batch(false, [Event()])))!;
            batch["effectiveConfigRevision"] = ReceiverRevision("P2");
            return batch;
        }));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var prior = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var cursor = new NativeObservationCursor("inbound-observation:v1", "epoch", "source", 0);
        root = RenewableStatus("P2");
        var renewed = await reader.RevalidateAsync(prior, cursor, TestContext.Current.CancellationToken);
        Assert.NotEqual(prior.Revision, renewed.Revision);
        Assert.Equal(0, cursor.Sequence);
        var result = await reader.PollAsync(renewed, cursor, TestContext.Current.CancellationToken);
        Assert.Equal("N1", Assert.Single(result.Records).NativeMessageId);
        Assert.Equal(1, result.Cursor.Sequence);
        Assert.DoesNotContain(requests, p => p.EndsWith("/open"));
        Assert.Single(requests, p => p.EndsWith("/poll"));
    }

    [Theory]
    [InlineData("config")]
    [InlineData("gate")]
    [InlineData("epoch")]
    [InlineData("self")]
    [InlineData("peer")]
    [InlineData("placement")]
    [InlineData("path")]
    [InlineData("unknown")]
    [InlineData("version")]
    [InlineData("registry")]
    [InlineData("bootstrap")]
    [InlineData("disarmed")]
    [InlineData("recovery")]
    [InlineData("active")]
    [InlineData("health")]
    [InlineData("retained-config")]
    public async Task Receiver_revalidation_never_widens_the_approved_window(string change)
    {
        var root = RenewableStatus();
        using var client = new HttpClient(new Handler(_ => root));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var prior = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        root = RenewableStatus("P2");
        switch (change)
        {
            case "config": root["bootstrap"]!["effectiveSnapshotProof"]!["revision"] = "other"; break;
            case "gate": root["bootstrap"]!["isolationGate"]!["gateInstanceRevision"] = "other"; break;
            case "epoch": root["bootstrap"]!["activeEpoch"] = "other"; break;
            case "self": root["selfIdentityRef"] = "other"; break;
            case "peer": root["peerActorRef"] = "other"; break;
            case "placement": root["dispatch"]!["paths"]![0]!["placementProof"] = "other"; break;
            case "path": root["dispatch"]!["paths"]![0]!["family"] = "future"; break;
            case "unknown": root["dispatch"]!["status"] = "unknown"; break;
            case "version": root["bootstrap"]!["effectiveSnapshotProof"]!["version"] = "future"; break;
            case "registry": root["dispatch"]!["effectiveConfigRevision"] = ReceiverRevision("P2").Replace(":registry-2", ":registry-3"); break;
            case "bootstrap": root["bootstrap"] = null; break;
            case "disarmed": root["bootstrap"]!["captureState"] = "off"; break;
            case "recovery": root["bootstrap"]!["legacyRecovery"]!["activeOwnerCount"] = 1; break;
            case "active": root["dispatch"]!["status"] = "active"; break;
            case "health": root["health"] = "degraded"; break;
            case "retained-config":
                root["dispatch"]!["producerRevision"] = Producer("P2").Replace(new string('a', 64), new string('b', 64));
                root["dispatch"]!["effectiveConfigRevision"] = ReceiverRevision("P2").Replace(new string('a', 64), new string('b', 64));
                root["bootstrap"]!["effectiveConfigRevision"] = root["dispatch"]!["effectiveConfigRevision"]!.DeepClone();
                break;
        }
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.RevalidateAsync(prior,
            new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Receiver_revalidation_rejects_foreign_approval_wrong_epoch_and_missing_scope()
    {
        var root = RenewableStatus();
        using var client = new HttpClient(new Handler(_ => root));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var other = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var cursor = new NativeObservationCursor("inbound-observation:v1", "epoch", "source", 0);
        await Assert.ThrowsAsync<NativeObservationException>(() => other.RevalidateAsync(approval, cursor, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.RevalidateAsync(approval, cursor with { Epoch = "other" }, TestContext.Current.CancellationToken));
        root = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        var noScope = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.RevalidateAsync(noScope, cursor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Trusted_native_lid_binding_keeps_logical_pn_memory_identity()
    {
        var transport = Session with { NativeConversationId = "12345@lid" };
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(transport);
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? status : Batch(false, [Event()])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer, transport);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var batch = await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken);
        var inbound = reader.ToInbound(Assert.Single(batch.Records));
        Assert.Equal("100@s.whatsapp.net", inbound.Session.NativeConversationId);
        Assert.Equal("whatsapp-test", inbound.ConversationNamespace);
        Assert.Equal(Peer, inbound.ActorId);
        Assert.Equal("N1", inbound.Message.ProviderMessageId);
    }

    [Fact]
    public void Transport_alias_cannot_change_account()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        Assert.Throws<ArgumentException>(() => new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding,
            "source", Self, Peer, Session with { AccountId = "foreign" }));
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("armed", false)]
    public async Task Provisioning_checks_reader_without_requiring_capture(string captureState, bool beforeArm)
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["bootstrap"] = JsonSerializer.SerializeToNode(new { subsystemOperational = true, readerAvailable = true,
            journalHealth = "healthy", captureState, activeEpoch = captureState == "armed" ? "epoch" : null,
            controlAvailable = true, configSnapshotAvailable = true, dispatchInspectorAvailable = true,
            effectiveConfigRevision = "R1" });
        using var handler = new Handler(_ => status);
        using var client = new HttpClient(handler);
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var checks = await reader.ProvisioningChecksAsync(beforeArm, TestContext.Current.CancellationToken);
        Assert.All(checks, check => Assert.True(check.Passed, check.Id));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task Provisioning_does_not_infer_bootstrap_from_reader_capabilities()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        await Assert.ThrowsAsync<NativeObservationException>(() => reader.ProvisioningChecksAsync(true, TestContext.Current.CancellationToken));
    }
    private static readonly SessionKey Session = new("runtime", "character", "whatsapp", "test", "100@s.whatsapp.net");
    [Fact]
    public async Task Native_agent_echo_is_suppressed_without_constructing_a_human_memory_identity()
    {
        const string agent = "agent:filomena";
        var binding = new ShadowSessionBinding(Session, "whatsapp-test", [agent], [Peer]);
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Status() : Batch(false, [Event(self: true)])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", binding, "source", agent, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var record = Assert.Single((await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0),
            TestContext.Current.CancellationToken)).Records);
        var input = reader.ToInbound(record);
        Assert.True(input.FromMe);
        Assert.Equal(agent, input.ActorId);
        var dir = Directory.CreateTempSubdirectory("native-agent-echo-");
        try
        {
            using var inbox = new ShadowInboundJournal(Path.Combine(dir.FullName, "inbox.jsonl"), binding, "project", "epoch", 0);
            Assert.Equal("self", inbox.Observe(input).Status);
            Assert.False(inbox.ReconciliationRequired);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Native_sender_without_trusted_name_does_not_invent_a_name_for_the_model()
    {
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [Event()])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var batch = await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken);
        var inbound = reader.ToInbound(Assert.Single(batch.Records));
        var message = inbound.Message.Message;
        Assert.Equal(Peer, message.GetProperty("remetente").GetProperty("stableId").GetString());
        var model = FlowDomain.ForModel(message, FlowDomain.Message, new Dictionary<string, string>());
        Assert.Equal("", model.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.DoesNotContain(Peer, model.GetRawText());
        Assert.Equal("hello", model.GetProperty("texto").GetString());
    }

    private const string Self = "whatsapp:200@s.whatsapp.net", Peer = "whatsapp:100@s.whatsapp.net";
    private static readonly ShadowSessionBinding Binding = new(Session, "whatsapp-test", [Self], [Peer]);
    private static readonly SessionKey GroupSession = Session with { NativeConversationId = "123456@g.us" };
    private static readonly ShadowSessionBinding GroupBinding = new(GroupSession, "whatsapp-group-test",
        [Self], [Peer], SessionEntryKind.Group);
    private const string GroupParticipant = "15550000000@s.whatsapp.net";
    [Theory]
    [InlineData("valid", true)]
    [InlineData("no_authority", false)]
    [InlineData("wrong_group", false)]
    [InlineData("wrong_actor", false)]
    [InlineData("from_me", false)]
    [InlineData("no_canonical", false)]
    [InlineData("self_alias", false)]
    [InlineData("known_alias_conflict", false)]
    [InlineData("device_suffix", true)]
    public async Task New_group_member_requires_native_group_and_actor_proof(string mutation, bool valid)
    {
        var binding = new ShadowSessionBinding(GroupSession, "whatsapp-group-test", [Self], [Peer], SessionEntryKind.Group);
        if (mutation != "no_authority")
        {
            binding.EnableNativeGroupParticipants();
        }
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["version"] = "inbound-observation:v2"; status["kind"] = "group";
        status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(GroupSession);
        status.AsObject().Remove("peerActorRef");
        status["participantActorRefs"] = JsonSerializer.SerializeToNode(new[] { GroupActorRef(GroupParticipant) });
        const string lid = "199999@lid", pn = "15550000099@s.whatsapp.net";
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row["actorRef"] = mutation == "wrong_actor" ? "forged" : GroupActorRef(lid);
        row["payload"]!["message"]!["key"] = JsonSerializer.SerializeToNode(new {
            remoteJid = mutation == "wrong_group" ? "999@g.us" : GroupSession.NativeConversationId,
            participant = mutation == "device_suffix" ? "199999:3@lid" : lid,
            participantAlt = mutation switch { "no_canonical" => null, "self_alias" => "200@s.whatsapp.net",
                "known_alias_conflict" => GroupParticipant, _ => pn }, fromMe = mutation == "from_me" });
        var batch = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Batch(false, [row])))!;
        batch["version"] = "inbound-observation:v2"; batch["nextCursor"]!["version"] = "inbound-observation:v2";
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? status : batch));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", binding, "source", Self, Peer,
            groupParticipants: new Dictionary<string, string> { [GroupParticipant] = Peer });
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        async Task<CanonicalShadowInbound> Read() => reader.ToInbound(Assert.Single((await reader.PollAsync(approval,
            new("inbound-observation:v2", "epoch", "source", 0), TestContext.Current.CancellationToken)).Records));
        if (valid) Assert.Equal("whatsapp:" + pn, (await Read()).ActorId);
        else await Assert.ThrowsAsync<NativeObservationException>(Read);
    }

    [Fact]
    public void Native_group_permission_cannot_be_enabled_on_a_DM()
        => Assert.Throws<InvalidOperationException>(() => new ShadowSessionBinding(Session, "whatsapp-test", [Self], [Peer])
            .EnableNativeGroupParticipants());

    [Theory]
    [InlineData("legenda humana", false)]
    [InlineData(null, true)]
    public async Task Document_with_caption_wrapper_preserves_caption_and_identity_without_executing_attachment(string? caption, bool skipped)
    {
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row["payload"]!["message"]!["message"] = JsonSerializer.SerializeToNode(new {
            documentWithCaptionMessage = new { message = new { documentMessage = new { caption } } },
            messageContextInfo = new { }
        });
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [row])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var record = Assert.Single((await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken)).Records);
        var inbound = reader.ToInbound(record);
        Assert.Equal("N1", inbound.Message.ProviderMessageId);
        Assert.Equal(skipped, inbound.SkipNonText);
        Assert.Equal(caption ?? "", inbound.Message.Message.GetProperty("texto").GetString());
    }

    [Theory]
    [InlineData("imageMessage", true)]
    [InlineData("albumMessage", true)]
    [InlineData("reactionMessage", true)]
    [InlineData("stickerMessage", true)]
    [InlineData("audioMessage", true)]
    [InlineData("pollCreationMessage", true)]
    [InlineData("pollCreationMessageV2", true)]
    [InlineData("pollCreationMessageV3", true)]
    [InlineData("pollUpdateMessage", true)]
    [InlineData("futureMessage", false)]
    public async Task Known_non_text_is_explicitly_skipped_but_unknown_payload_is_not(string kind, bool supported)
    {
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row["payload"]!["message"]!["message"] = new System.Text.Json.Nodes.JsonObject {
            [kind] = new System.Text.Json.Nodes.JsonObject(), ["messageContextInfo"] = new System.Text.Json.Nodes.JsonObject() };
        if (kind == "albumMessage")
        {
            row["payload"]!["message"]!["message"]![kind]!["expectedImageCount"] = 2;
            row["payload"]!["message"]!["message"]![kind]!["expectedVideoCount"] = 0;
        }
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [row])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var record = Assert.Single((await reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0),
            TestContext.Current.CancellationToken)).Records);
        if (!supported) { Assert.Throws<NativeObservationException>(() => reader.ToInbound(record)); return; }
        var input = reader.ToInbound(record);
        Assert.True(input.SkipNonText);
        Assert.Equal("N1", input.Message.ProviderMessageId);
        Assert.Equal("", input.Message.Message.GetProperty("texto").GetString());
        row["payload"]!["message"]!["message"]![kind]!["caption"] = "a real caption";
        if (kind == "imageMessage")
        {
            var captionRecord = record with { Payload = JsonSerializer.SerializeToElement(row["payload"]) };
            Assert.False(reader.ToInbound(captionRecord).SkipNonText);
            Assert.Equal("a real caption", reader.ToInbound(captionRecord).Message.Message.GetProperty("texto").GetString());
        }
    }

    private static string GroupActorRef(string nativeActor) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { "source", "actor", nativeActor })));

    private static string ActorRef(string nativeActor) => GroupActorRef(nativeActor);

    [Fact]
    public async Task Group_v2_maps_trusted_participant_and_cursor_to_group_inbound()
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["version"] = "inbound-observation:v2";
        status["kind"] = "group";
        status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(GroupSession);
        status.AsObject().Remove("peerActorRef");
        status["participantActorRefs"] = JsonSerializer.SerializeToNode(new[] { GroupActorRef(GroupParticipant) });
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row["actorRef"] = GroupActorRef(GroupParticipant);
        var batch = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Batch(false, [row])))!;
        batch["version"] = "inbound-observation:v2";
        batch["nextCursor"]!["version"] = "inbound-observation:v2";
        using var client = new HttpClient(new Handler(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/status") ? status : batch));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token",
            GroupBinding, "source", Self, Peer,
            groupParticipants: new Dictionary<string, string> { [GroupParticipant] = Peer });
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var result = await reader.PollAsync(approval,
            new("inbound-observation:v2", "epoch", "source", 0), TestContext.Current.CancellationToken);
        var inbound = reader.ToInbound(Assert.Single(result.Records));
        Assert.Equal(SessionEntryKind.Group, inbound.Kind);
        Assert.Equal(GroupSession, inbound.Session);
        Assert.Equal(Peer, inbound.ActorId);
        Assert.Equal("Grupo", inbound.Message.Message.GetProperty("canal").GetProperty("nome").GetString());
    }

    [Theory]
    [InlineData("wrong_version")]
    [InlineData("foreign_participant")]
    [InlineData("missing_participant")]
    public async Task Group_v2_preflight_rejects_untrusted_contract_or_participants(string mutation)
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["version"] = mutation == "wrong_version" ? "inbound-observation:v1" : "inbound-observation:v2";
        status["kind"] = "group";
        status["routeBindingRef"] = NativeObservationReader.RouteBindingRef(GroupSession);
        status.AsObject().Remove("peerActorRef");
        status["participantActorRefs"] = JsonSerializer.SerializeToNode(
            mutation == "missing_participant" ? Array.Empty<string>() :
            [mutation == "foreign_participant" ? "foreign-ref" : GroupActorRef(GroupParticipant)]);
        using var client = new HttpClient(new Handler(_ => status));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token",
            GroupBinding, "source", Self, Peer,
            groupParticipants: new Dictionary<string, string> { [GroupParticipant] = Peer });
        await Assert.ThrowsAsync<NativeObservationException>(() =>
            reader.PreflightAsync(TestContext.Current.CancellationToken));
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static object Cursor(long sequence) => new { version = "inbound-observation:v1", epoch = "epoch", sessionRef = "source", sequence };
    private static object Status(string health = "healthy") => new
    {
        version = "inbound-observation:v1",
        health,
        sessionRef = "source",
        channel = "whatsapp",
        kind = "dm",
        routeBindingRef = NativeObservationReader.RouteBindingRef(Session),
        journalEpoch = "epoch",
        selfIdentityRef = "self-ref",
        peerActorRef = "peer-ref",
        capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(k => k, _ => true),
        dispatch = new { version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = "R1", capturedAt = 1000, diagnostic = "excluded", paths = Array.Empty<object>() }
    };
    private static object Event(string session = "source", bool self = false) => new
    {
        sequence = 1,
        nativeMessageId = "N1",
        sessionRef = session,
        actorRef = self ? "self-ref" : "peer-ref",
        fromMe = self,
        selfIdentityRef = "self-ref",
        occurredAt = 1000,
        journalEpoch = "epoch",
        payload = new { receivedAt = 1100, message = new { message = new { conversation = "hello" } } }
    };
    private static object Batch(bool open, object[] events) => open
        ? new { version = "inbound-observation:v1", sessionRef = "source", effectiveConfigRevision = "R1", status = "healthy", epochClosed = false, snapshot = events, cursor = Cursor(events.Length) }
        : new { version = "inbound-observation:v1", sessionRef = "source", effectiveConfigRevision = "R1", status = "healthy", epochClosed = false, events, nextCursor = Cursor(events.Length) };

    [Fact]
    public async Task Snapshot_is_context_poll_preserves_metadata_and_registry_deduplicates_after_restart()
    {
        using var handler = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : r.RequestUri.AbsolutePath.EndsWith("/open") ? Batch(true, []) : Batch(false, [Event()]));
        using var client = new HttpClient(handler);
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NeverComplete());
        runtime.UpdateProject(new("test", 1, "project", JsonSerializer.SerializeToElement(new
        {
            version = 1,
            stateScope = "session",
            initialStateId = "initial",
            states = new[]{new {id="initial",name="Initial",graph=new {
                version=3,nodes=new object[]{new {id="dm",kind="dm"},new{id="exit",kind="exit",settings=new{mode="switch",targetStateId="initial"}}},
                edges=Array.Empty<object>(),controlEdges=new[]{new{source="dm",sourcePort="then",target="exit",targetPort="exec"}}
            }}}
        })));
        var opened = await reader.OpenAsync(approval, TestContext.Current.CancellationToken);
        Assert.Empty(opened.Records);
        Assert.Empty(runtime.ReadOutcomes(Session));
        var poll = await reader.PollAsync(approval, opened.Cursor, TestContext.Current.CancellationToken);
        var record = Assert.Single(poll.Records);
        Assert.Null(record.MembershipAuthority); // ordinary DM keeps the original projection shape
        Assert.Equal("N1", record.NativeMessageId); Assert.Equal(1, record.Sequence);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), record.OccurredAt);
        var inbound = reader.ToInbound(record);
        var restartedReader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var reopenedApproval = await restartedReader.PreflightAsync(TestContext.Current.CancellationToken);
        var replay = Assert.Single((await restartedReader.PollAsync(reopenedApproval, opened.Cursor, TestContext.Current.CancellationToken)).Records);
        Assert.Equal(record.NativeMessageId, replay.NativeMessageId);
        Assert.Equal(record.Sequence, replay.Sequence);
        var path = Path.Combine(Path.GetTempPath(), "reader-inbox-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using (var inbox = new ShadowInboundJournal(path, Binding, "project", "epoch", 0))
            {
                Assert.Equal("observed", inbox.Observe(inbound).Status);
                var admission = await runtime.ReceiveAsync(Session, inbound.Message, TestContext.Current.CancellationToken);
                Assert.True(admission.Status == SessionAdmissionStatus.Accepted, admission.Reason);
                inbox.Admitted("N1", admission.AdmissionWatermark!.Value);
                await runtime.WaitForIdleAsync(Session, TestContext.Current.CancellationToken);
                var outcome = Assert.Single(runtime.ReadOutcomes(Session));
                inbox.Completed("N1", outcome.BatchId, outcome.ExecutionId, outcome.Success);
                Assert.Equal("duplicate", inbox.Observe(inbound).Status);
            }
            using var restarted = ShadowInboundJournal.StartNonResumableRun(path, Binding, "project", "epoch", 1);
            Assert.Equal("backlog", restarted.Observe(inbound).Status);
        }
        finally { File.Delete(path); }
        Assert.All(handler.Methods, m => Assert.Equal(HttpMethod.Get, m));
        Assert.Single(runtime.ReadOutcomes(Session));
    }

    [Theory]
    [InlineData("degraded")]
    [InlineData("gap_detected")]
    [InlineData("cursor_expired")]
    [InlineData("unavailable")]
    public async Task Unhealthy_is_not_valid_empty(string health)
    {
        using var client = new HttpClient(new Handler(_ => Status(health)));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var error = await Assert.ThrowsAsync<NativeObservationException>(() => reader.PreflightAsync(TestContext.Current.CancellationToken));
        Assert.Equal(health, error.Code);
    }

    [Fact]
    public async Task Wrong_session_is_integrity_failure_and_self_does_not_open_execution()
    {
        var wrong = false;
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [Event(wrong ? "foreign" : "source", true)])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        var cursor = new NativeObservationCursor("inbound-observation:v1", "epoch", "source", 0);
        var own = reader.ToInbound(Assert.Single((await reader.PollAsync(approval, cursor, TestContext.Current.CancellationToken)).Records));
        Assert.True(own.FromMe); Assert.True(Binding.IsSelf(own.ActorId));
        var path = Path.Combine(Path.GetTempPath(), "reader-self-" + Guid.NewGuid() + ".jsonl");
        try
        {
            using var inbox = new ShadowInboundJournal(path, Binding, "project", "epoch", 0);
            Assert.NotEqual("observed", inbox.Observe(own).Status);
        }
        finally { File.Delete(path); }
        wrong = true;
        Assert.Equal("integrity_violation", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval, cursor, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Approval_is_reader_local_and_version_or_revisions_are_not_coerced()
    {
        using var client = new HttpClient(new Handler(_ => Status()));
        var a = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var b = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await a.PreflightAsync(TestContext.Current.CancellationToken);
        Assert.Equal("invalid_preflight_provenance", (await Assert.ThrowsAsync<NativeObservationException>(() => b.OpenAsync(approval, TestContext.Current.CancellationToken))).Code);
        Assert.Equal("version_mismatch", (await Assert.ThrowsAsync<NativeObservationException>(() => a.PollAsync(approval, new("future", "epoch", "source", 0), TestContext.Current.CancellationToken))).Code);
        using var wrongClient = new HttpClient(new Handler(_ => new { version = "future" }));
        var wrong = new NativeObservationReader(wrongClient, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        Assert.Equal("version_mismatch", (await Assert.ThrowsAsync<NativeObservationException>(() => wrong.PreflightAsync(TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Http_conflict_is_stale_not_empty()
    {
        var stale = false;
        using var client = new HttpClient(new FaultHandler(() => stale));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        stale = true;
        Assert.Equal("preflight_stale_or_dispatch_changed", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.OpenAsync(approval, TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    public async Task Foreign_route_cannot_be_bound_to_the_host_session()
    {
        var status = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Status()))!;
        status["routeBindingRef"] = "foreign";
        using var client = new HttpClient(new Handler(_ => status));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        Assert.Equal("integrity_violation", (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PreflightAsync(TestContext.Current.CancellationToken))).Code);
    }
    [Theory]
    [InlineData("sequence", "gap_detected")]
    [InlineData("nativeMessageId", "invalid_output")]
    [InlineData("actorRef", "integrity_violation")]
    public async Task Corrupt_records_fail_closed_without_partial_batch(string field, string code)
    {
        var row = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Event()))!;
        row[field] = field == "sequence" ? System.Text.Json.Nodes.JsonValue.Create(2) : System.Text.Json.Nodes.JsonValue.Create(field == "nativeMessageId" ? "" : "foreign");
        using var client = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/status") ? Status() : Batch(false, [row])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "token", Binding, "source", Self, Peer);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        Assert.Equal(code, (await Assert.ThrowsAsync<NativeObservationException>(() => reader.PollAsync(approval, new("inbound-observation:v1", "epoch", "source", 0), TestContext.Current.CancellationToken))).Code);
    }

    private sealed class FaultHandler(Func<bool> stale) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(stale() ? new HttpResponseMessage(HttpStatusCode.Conflict) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Status(), Json)) });
    }

    private sealed class Handler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response(request), Json)) });
        }
    }
    private sealed class NeverComplete : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new InvalidOperationException("No model call allowed.");
    }
}
