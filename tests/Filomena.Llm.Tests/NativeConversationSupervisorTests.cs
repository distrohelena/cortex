using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Observation;
using System.Text.Json;
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeConversationSupervisorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("native-conversations-").FullName;

    [Fact]
    public void Group_and_two_DMs_get_stable_private_paths_and_independent_cursors()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);
        var firstDm = Binding("15550001@s.whatsapp.net", SessionEntryKind.Dm);
        var secondDm = Binding("15550002@s.whatsapp.net", SessionEntryKind.Dm);

        var groupDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, group);
        var firstDmDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, firstDm);
        var secondDmDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, secondDm);

        Assert.Equal(firstDmDirectory, NativeConversationSupervisor.PrepareSessionDirectory(root, firstDm));
        Assert.Equal(3, new[] { groupDirectory, firstDmDirectory, secondDmDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var directory in new[] { groupDirectory, firstDmDirectory, secondDmDirectory })
        {
            Assert.Equal(Path.GetFullPath(root), Directory.GetParent(directory)!.FullName);
            Assert.Matches("\\A[0-9A-F]{64}\\z", Path.GetFileName(directory));
            Assert.DoesNotContain("@", directory, StringComparison.Ordinal);
        }

        SaveAndResolve(groupDirectory, group, "inbound-observation:v2", "group-epoch", 2);
        SaveAndResolve(firstDmDirectory, firstDm, "inbound-observation:v1", "first-dm-epoch", 4);
        SaveAndResolve(secondDmDirectory, secondDm, "inbound-observation:v1", "second-dm-epoch", 7);
    }

    [Fact]
    public void Existing_group_directory_is_preserved_while_DM_uses_opaque_scope_directory()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);
        var dm = Binding("15550001@s.whatsapp.net", SessionEntryKind.Dm);
        var existingGroupDirectory = Directory.CreateDirectory(Path.Combine(root, "existing-group-state")).FullName;
        var historyCheckpoint = Path.Combine(existingGroupDirectory, "history-checkpoint.json");
        File.WriteAllText(historyCheckpoint, "existing-group-history");

        var selectedGroupDirectory = NativeConversationSupervisor.ResolveSessionDirectory(root, group, existingGroupDirectory);
        var dmDirectory = NativeConversationSupervisor.ResolveSessionDirectory(root, dm);

        Assert.Equal(existingGroupDirectory, selectedGroupDirectory);
        Assert.Equal("existing-group-history", File.ReadAllText(historyCheckpoint));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), dm.OpaqueRef), dmDirectory);
        Assert.NotEqual(selectedGroupDirectory, dmDirectory);
    }

    [Fact]
    public void Group_directory_must_be_explicit_and_already_exist()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);

        Assert.Throws<InvalidOperationException>(() => NativeConversationSupervisor.ResolveSessionDirectory(root, group));
        Assert.Throws<DirectoryNotFoundException>(() => NativeConversationSupervisor.ResolveSessionDirectory(
            root, group, Path.Combine(root, "not-migrated")));
    }

    [Fact]
    public async Task Revalidated_member_lease_is_not_accepted_until_old_worker_drains()
    {
        var original = new MemberDmBoundTextBinding("15550001@s.whatsapp.net", "dm-scope", "dm-route", "native-rev-7", "lease-1");
        var refreshed = original with { MembershipLeaseRef = "lease-2" };
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var replacement = NativeConversationSupervisor.AcceptRevalidatedMemberDmLeaseAsync(original, refreshed, drain.Task,
            TestContext.Current.CancellationToken);

        Assert.False(replacement.IsCompleted);
        drain.SetResult();
        Assert.Equal(refreshed, await replacement);
    }

    [Fact]
    public void Managed_member_event_requires_owner_proven_captured_generation()
    {
        using var payload = JsonDocument.Parse("{}");
        var observed = new NativeObservationEvent(1, "native-message-1", "dm-scope", "peer", false,
            "self", DateTimeOffset.UnixEpoch, payload.RootElement, "dm-epoch");

        Assert.Throws<NativeObservationException>(() => NativeConversationSupervisor.ValidateMemberDmEventGeneration(
            observed, "dm-scope", null, "lease-1"));
        Assert.Throws<NativeObservationException>(() => NativeConversationSupervisor.ValidateMemberDmEventGeneration(
            observed, "dm-scope", "lease-2", "lease-1"));
        Assert.Throws<NativeObservationException>(() => NativeConversationSupervisor.ValidateMemberDmEventGeneration(
            observed with { SessionRef = "other-scope" }, "dm-scope", "lease-1", "lease-1"));
        NativeConversationSupervisor.ValidateMemberDmEventGeneration(observed, "dm-scope", "lease-1", "lease-1");
    }

    [Fact]
    public void Prior_or_missing_member_event_lease_is_not_executable_under_new_worker_generation()
    {
        using var payload = JsonDocument.Parse("{}");
        var observed = new NativeObservationEvent(11, "native-message-old", "dm-scope", "peer", false,
            "self", DateTimeOffset.UnixEpoch, payload.RootElement, "dm-epoch")
        { MembershipAuthority = new("native-member-dm-admission:v1", "lease-generation-1") };

        Assert.False(NativeConversationSupervisor.HasCapturedMemberDmEventGeneration(observed,
            "dm-scope", "lease-generation-1", "lease-generation-2"));
        Assert.Throws<NativeObservationException>(() => NativeConversationSupervisor.HasCapturedMemberDmEventGeneration(
            observed with { MembershipAuthority = null }, "dm-scope", null, "lease-generation-2"));
        Assert.True(NativeConversationSupervisor.HasCapturedMemberDmEventGeneration(observed with
        { MembershipAuthority = new("native-member-dm-admission:v1", "lease-generation-2") },
            "dm-scope", "lease-generation-2", "lease-generation-2"));
        Assert.Throws<NativeObservationException>(() => NativeConversationSupervisor.HasCapturedMemberDmEventGeneration(
            observed with { SessionRef = "foreign-scope" }, "dm-scope", "lease-generation-2", "lease-generation-2"));
    }

    [Fact]
    public void Stale_member_event_mapping_becomes_durable_nonexecution_but_fresh_mapping_stays_executable()
    {
        var session = new SessionKey("runtime", "filomena", "whatsapp", "account-a", "15550001@s.whatsapp.net");
        CanonicalShadowInbound Inbound(long sequence, string id) => new("dm-epoch", sequence, session,
            SessionEntryKind.Dm, "dm-namespace", "whatsapp:15550001@s.whatsapp.net",
            new(id, session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UnixEpoch,
                SessionMessageDirection.Inbound, SessionEntryKind.Dm, JsonSerializer.SerializeToElement(new { texto = "inbound" })));
        using var payload = JsonDocument.Parse("{}");
        var oldEvent = new NativeObservationEvent(11, "old-event", "dm-scope", "peer", false, "self",
            DateTimeOffset.UnixEpoch, payload.RootElement, "dm-epoch")
        { MembershipAuthority = new("native-member-dm-admission:v1", "lease-generation-1") };
        var freshEvent = oldEvent with
        {
            Sequence = 12,
            NativeMessageId = "fresh-event",
            MembershipAuthority = new("native-member-dm-admission:v1", "lease-generation-2"),
        };

        var stale = NativeConversationSupervisor.SuppressUnlessCapturedMemberDmEventGeneration(oldEvent,
            Inbound(11, "old-event"), "dm-scope", "lease-generation-1", "lease-generation-2");
        var fresh = NativeConversationSupervisor.SuppressUnlessCapturedMemberDmEventGeneration(freshEvent,
            Inbound(12, "fresh-event"), "dm-scope", "lease-generation-2", "lease-generation-2");

        Assert.True(stale.SkipNonText);
        Assert.Equal("member_dm_event_generation_stale", stale.SuppressionReason);
        Assert.False(fresh.SkipNonText);
        Assert.Null(fresh.SuppressionReason);
    }

    [Fact]
    public async Task Failed_DM_worker_and_individual_drain_do_not_cancel_group_worker()
    {
        var group = Binding("120363318973251043@g.us", SessionEntryKind.Group);
        var dm = Binding("15550001@s.whatsapp.net", SessionEntryKind.Dm);
        var existingGroupDirectory = Directory.CreateDirectory(Path.Combine(root, "existing-group-worker-state")).FullName;
        await using var supervisor = new NativeConversationSupervisor();
        var groupStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dmAttempts = 0;

        var groupWorker = supervisor.StartSession(group, root, async (directory, token) =>
        {
            groupStarted.TrySetResult(directory);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, existingGroupDirectory);
        var dmWorker = supervisor.StartSession(dm, root, (_, _) =>
        {
            Interlocked.Increment(ref dmAttempts);
            throw new InvalidOperationException("fake DM failure");
        });
        var duplicate = supervisor.StartSession(dm, root, (_, _) =>
        {
            Interlocked.Increment(ref dmAttempts);
            return Task.CompletedTask;
        });

        Assert.Same(dmWorker, duplicate);
        Assert.Equal("failed", (await dmWorker.Completion).Status);
        Assert.Equal(existingGroupDirectory, await groupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(existingGroupDirectory, groupWorker.StateDirectory);
        Assert.Equal(1, dmAttempts);
        Assert.False(groupWorker.Completion.IsCompleted);

        await supervisor.StopSessionAsync(dm, TestContext.Current.CancellationToken);
        Assert.False(groupWorker.Completion.IsCompleted);
    }

    [Fact]
    public void Member_discovery_preserves_exact_aliases_and_keeps_pending_nonrunnable()
    {
        using var document = JsonDocument.Parse(MemberDiscovery([
            MemberScope("ready", "15550001@s.whatsapp.net", ["15550001@s.whatsapp.net", "991234:8@lid"], "dm-scope-1"),
            MemberScope("pending", null, null, null),
        ]));

        var discovery = NativeMemberDmDiscoveryValidator.Parse(document.RootElement,
            "account-a", "120363318973251043@g.us", "group-session");

        var ready = Assert.Single(discovery.Scopes, scope => scope.IsRunnable);
        Assert.Equal(new[] { "15550001@s.whatsapp.net", "991234:8@lid" }, ready.NativeAliases);
        Assert.Equal("pending", Assert.Single(discovery.Scopes, scope => !scope.IsRunnable).Status);
    }

    [Fact]
    public void Member_discovery_accepts_only_the_exact_native_versioned_reader_base()
    {
        var wire = JsonNode.Parse(MemberDiscovery([
            MemberScope("ready", "15550001@s.whatsapp.net", ["15550001@s.whatsapp.net"], "dm-scope-1"),
        ]))!;
        wire["scopes"]![0]!["reader"]!["endpoint"] = "http://127.0.0.1:3456/inbound-observation/v1";
        using var nativeShape = JsonDocument.Parse(wire.ToJsonString());

        var parsed = NativeMemberDmDiscoveryValidator.Parse(nativeShape.RootElement,
            "account-a", "120363318973251043@g.us", "group-session");

        Assert.Equal("http://127.0.0.1:3456/inbound-observation/v1",
            Assert.Single(parsed.Scopes).ReaderEndpoint);
    }

    [Fact]
    public void Ready_member_discovery_requires_lease_expiry_diagnostic_field()
    {
        var wire = JsonNode.Parse(MemberDiscovery([
            MemberScope("ready", "15550001@s.whatsapp.net", ["15550001@s.whatsapp.net"], "dm-scope-1"),
        ]))!;
        wire["scopes"]![0]!["leaseExpiresAt"] = null;
        using var noDiagnosticExpiry = JsonDocument.Parse(wire.ToJsonString());

        Assert.Throws<InvalidDataException>(() => NativeMemberDmDiscoveryValidator.Parse(noDiagnosticExpiry.RootElement,
            "account-a", "120363318973251043@g.us", "group-session"));
    }

    [Fact]
    public void Member_discovery_rejects_missing_lease_or_alias_claimed_by_two_peers()
    {
        using var missingLease = JsonDocument.Parse(MemberDiscovery([
            MemberScope("ready", "15550001@s.whatsapp.net", ["15550001@s.whatsapp.net"], "dm-scope-1", lease: null),
        ]));
        Assert.Throws<InvalidDataException>(() => NativeMemberDmDiscoveryValidator.Parse(missingLease.RootElement,
            "account-a", "120363318973251043@g.us", "group-session"));

        using var conflictingAlias = JsonDocument.Parse(MemberDiscovery([
            MemberScope("ready", "15550001@s.whatsapp.net", ["15550001@s.whatsapp.net", "991234:8@lid"], "dm-scope-1"),
            MemberScope("ready", "15550002@s.whatsapp.net", ["15550002@s.whatsapp.net", "991234:8@lid"], "dm-scope-2"),
        ]));
        Assert.Throws<InvalidDataException>(() => NativeMemberDmDiscoveryValidator.Parse(conflictingAlias.RootElement,
            "account-a", "120363318973251043@g.us", "group-session"));
    }

    [Fact]
    public async Task Host_generates_separate_hashed_member_evidence_from_operator_template_and_native_facts()
    {
        const string account = "account-a", groupJid = "120363318973251043@g.us", groupSessionRef = "group-session";
        const string nativeId = "15550001@s.whatsapp.net", scopeRef = "dm-scope-1", lease = "lease-1";
        var groupSession = new SessionKey("runtime", "filomena", "whatsapp", account, groupJid);
        var dmSession = groupSession with { NativeConversationId = nativeId };
        var selfActor = "whatsapp:200@s.whatsapp.net";
        var peerActor = "whatsapp:" + nativeId;
        var groupBinding = new ShadowSessionBinding(groupSession, "whatsapp-group-source", [selfActor], [peerActor], SessionEntryKind.Group);
        var dmBinding = new ShadowSessionBinding(dmSession, FilomenaIdentity.WhatsAppConversationNamespace(nativeId), [selfActor], [peerActor]);
        var routeRef = NativeObservationReader.RouteBindingRef(dmSession);
        var nativeRevision = "base:producer-00000000-0000-0000-0000-000000000001:" + new string('a', 64) + ":registry-2:isolation-G1";
        var membershipDiscovery = JsonSerializer.SerializeToNode(new
        {
            version = "native-group-member-dm-discovery:v1",
            source = new { accountId = account, groupJid, groupSessionRef }, enrollment = "enabled",
            scopes = new[] { new {
                status = "ready", scopeRef, nativeConversationId = nativeId,
                nativeAliases = new[] { nativeId, "991234:8@lid" }, membershipLeaseRef = lease, leaseExpiresAt = (long?)1,
                reader = new { endpoint = "http://127.0.0.1:3456/", version = "inbound-observation:v1",
                    routeBindingRef = routeRef, effectiveConfigRevision = nativeRevision },
                epoch = "dm-epoch-1", initialCursor = new { version = "inbound-observation:v1", epoch = "dm-epoch-1", sessionRef = scopeRef, sequence = 0 }, failureCode = (string?)null,
            }},
        });
        var proof = new { status = "valid", version = "effective-runtime-config:v1", revision = "base" };
        var gate = new { gateVersion = "native-dm-isolation:v1", gateInstanceRevision = "G1", approvedEffectiveConfigRevision = "base",
            accountRef = "account", sessionRef = scopeRef, coveredPathIds = new[] { "commands" }, placementProof = "placement", lifecycleState = "armed" };
        var producer = "00000000-0000-0000-0000-000000000001:" + new string('a', 64);
        var status = JsonSerializer.SerializeToNode(new {
            version = "inbound-observation:v1", health = "healthy", sessionRef = scopeRef, channel = "whatsapp", kind = "dm",
            routeBindingRef = routeRef, journalEpoch = "dm-epoch-1", selfIdentityRef = "self-ref", peerActorRef = "peer-ref",
            capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(capability => capability, _ => true),
            dispatch = new { version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = nativeRevision,
                effectiveSnapshotProof = proof, isolationGate = gate, producerRevision = producer,
                paths = new[] { new { family = "commands", status = "inactive", rawStatus = "inactive", effectiveStatus = "inactive", effectiveConfigRevision = "base" } } },
            bootstrap = new { subsystemOperational = true, readerAvailable = true, journalHealth = "healthy", captureState = "armed",
                activeEpoch = "dm-epoch-1", controlAvailable = true, configSnapshotAvailable = true, dispatchInspectorAvailable = true,
                effectiveConfigRevision = nativeRevision, effectiveSnapshotProof = proof, isolationGate = gate,
                legacyRecovery = new { status = "clear", effectiveConfigRevision = "base", uncoveredOwnerCount = 0, activeOwnerCount = 0,
                    pendingLegacyRecoveryCount = 0, quarantinedCount = 0 } },
        });
        using var groupClient = new HttpClient(new JsonHandler(_ => JsonSerializer.SerializeToNode(new {
            version = "inbound-observation:v2", sessionRef = groupSessionRef, channel = "whatsapp", kind = "group",
            routeBindingRef = NativeObservationReader.RouteBindingRef(groupSession), memberDmDiscovery = membershipDiscovery,
        })!));
        using var dmClient = new HttpClient(new JsonHandler(_ => status!));
        var groupReader = new NativeObservationReader(groupClient, new("http://127.0.0.1:2345"), "reader-token", groupBinding,
            groupSessionRef, selfActor, peerActor, groupParticipants: new Dictionary<string, string> { ["15550001@s.whatsapp.net"] = peerActor });
        var dmReader = new NativeObservationReader(dmClient, new("http://127.0.0.1:3456"), "reader-token", dmBinding,
            scopeRef, selfActor, peerActor);
        var discovery = await NativeConversationSupervisor.ReadMemberDmDiscoveryAsync(groupReader, account, groupJid,
            groupSessionRef, TestContext.Current.CancellationToken);
        var facts = await NativeConversationSupervisor.ValidateMemberDmScopeAsync(groupReader, dmReader, discovery,
            Assert.Single(discovery.Scopes), account, groupJid, groupSessionRef, "project-pin-P3",
            TestContext.Current.CancellationToken);
        var executionGuard = NativeConversationSupervisor.CreateMemberDmExecutionGenerationGuard(groupReader, facts);
        await executionGuard(TestContext.Current.CancellationToken);
        membershipDiscovery!["scopes"]![0]!["membershipLeaseRef"] = "lease-rotated-during-poll";
        await Assert.ThrowsAsync<NativeObservationException>(() => executionGuard(TestContext.Current.CancellationToken));
        membershipDiscovery["scopes"]![0]!["membershipLeaseRef"] = lease;
        var template = new NativeLiveConfig("native-template.json", new("fake-mcp", []), "route", "Filomena", 1000,
            new(true, true, true, true, true, true, true), "template.json", "template-hash",
            ContinuousDm: true, ManagedMemberDm: true);

        var artifact = NativeMemberDmEvidenceFactory.Generate(template, facts, root);

        var evidenceBytes = File.ReadAllBytes(artifact.LiveConfig.CapabilityEvidenceFile);
        using var evidence = JsonDocument.Parse(evidenceBytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidenceBytes)), artifact.LiveConfig.CapabilityEvidenceSha256);
        Assert.Equal(nativeRevision, evidence.RootElement.GetProperty("approvedRevision").GetString());
        Assert.Equal("project-pin-P3", artifact.PinnedProjectRevision);
        Assert.Equal("dm-scope-1", evidence.RootElement.GetProperty("observationSessionRef").GetString());
        Assert.Equal("whatsapp-bound-member-dm-text:v1", evidence.RootElement.GetProperty("boundSendContract").GetString());
        Assert.Equal("lease-1", evidence.RootElement.GetProperty("membershipLeaseRef").GetString());
        Assert.Equal("dm-epoch-1", evidence.RootElement.GetProperty("continueAfter").GetProperty("epoch").GetString());
        Assert.Equal(new[] { nativeId, "991234:8@lid" }, facts.NativeAliases);
        Assert.Equal("account-a", evidence.RootElement.GetProperty("nativeAccountId").GetString());
        Assert.Equal(groupJid, evidence.RootElement.GetProperty("nativeSourceGroupJid").GetString());
        Assert.All(evidence.RootElement.GetProperty("nativeObservationCapabilities").EnumerateObject(),
            capability => Assert.True(capability.Value.GetBoolean()));

        var leaseStateDirectory = NativeConversationSupervisor.PrepareSessionDirectory(root, dmBinding);
        var privateHistory = Path.Combine(leaseStateDirectory, "history.json");
        await File.WriteAllTextAsync(privateHistory, "private-history-before-expiry", TestContext.Current.CancellationToken);
        var cursorStore = new NativeLiveReadCursorStore(leaseStateDirectory, dmBinding.OpaqueRef, "project-pin-P3");
        var durableCursor = new NativeObservationCursor("inbound-observation:v1", "dm-epoch-1", scopeRef, 7);
        cursorStore.Save(durableCursor);
        var uncertainAttempt = Path.Combine(leaseStateDirectory, "outbound-attempt-uncertain.json");
        await File.WriteAllTextAsync(uncertainAttempt, "do-not-retry", TestContext.Current.CancellationToken);

        await using var supervisor = new NativeConversationSupervisor();
        var oldWorkerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionContextReady = new TaskCompletionSource<NativeMemberDmExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldWorker = supervisor.StartMemberDmSession(facts, dmBinding, root, groupReader,
            async (_, context, token) =>
            {
                executionContextReady.TrySetResult(context);
                oldWorkerStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        await oldWorkerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var executionContext = await executionContextReady.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await executionContext.EnsureCurrentGenerationAsync(TestContext.Current.CancellationToken);
        using (var eventPayload = JsonDocument.Parse("{}"))
        {
            var observed = new NativeObservationEvent(1, "native-message-1", scopeRef, peerActor, false,
                "self-ref", DateTimeOffset.UnixEpoch, eventPayload.RootElement, "dm-epoch-1")
            { MembershipAuthority = new("native-member-dm-admission:v1", lease) };
            var eventLease = await executionContext.ResolveEventLeaseAsync(observed, TestContext.Current.CancellationToken);
            NativeConversationSupervisor.ValidateMemberDmEventGeneration(observed,
                executionContext.ScopeFacts.ScopeRef, eventLease, executionContext.SendBinding.MembershipLeaseRef);
        }
        membershipDiscovery!["scopes"]![0]!["membershipLeaseRef"] = "lease-rotated-while-worker-queued";
        await Assert.ThrowsAsync<NativeObservationException>(() =>
            executionContext.EnsureCurrentGenerationAsync(TestContext.Current.CancellationToken));
        membershipDiscovery["scopes"]![0]!["membershipLeaseRef"] = lease;
        var reconstructedBinding = new ShadowSessionBinding(dmSession,
            FilomenaIdentity.WhatsAppConversationNamespace(nativeId), [selfActor], [peerActor]);
        var rediscoveredWorker = supervisor.StartMemberDmSession(facts, reconstructedBinding, root, groupReader,
            (_, _, _) => throw new InvalidOperationException("equivalent rediscovery must reuse active worker"));
        Assert.Same(oldWorker, rediscoveredWorker);

        // Simulate the native five-minute lease expiring/reconnect invalidating it.
        // Fresh owner facts and a distinct lease are then re-read and host-validated.
        var futureWorkBinding = new TaskCompletionSource<MemberDmBoundTextBinding>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restartedWorker = await supervisor.RestartMemberDmSessionAsync(oldWorker, async cancellation =>
        {
            Assert.True(oldWorker.Completion.IsCompleted);
            Assert.Equal("stopped", (await oldWorker.Completion).Status);
            membershipDiscovery!["scopes"]![0]!["membershipLeaseRef"] = "lease-2";
            membershipDiscovery["scopes"]![0]!["leaseExpiresAt"] = 1790000000;
            var refreshedDiscovery = await NativeConversationSupervisor.ReadMemberDmDiscoveryAsync(groupReader,
                account, groupJid, groupSessionRef, cancellation);
            return await NativeConversationSupervisor.ValidateMemberDmScopeAsync(groupReader, dmReader,
                refreshedDiscovery, Assert.Single(refreshedDiscovery.Scopes), account, groupJid, groupSessionRef,
                "project-pin-P3", cancellation);
        }, groupReader, root,
            (_, context, _) =>
            {
                futureWorkBinding.TrySetResult(context.SendBinding);
                return Task.CompletedTask;
            }, TestContext.Current.CancellationToken);

        Assert.Equal("stopped", (await oldWorker.Completion).Status);
        Assert.Equal(leaseStateDirectory, restartedWorker.StateDirectory);
        Assert.Equal("private-history-before-expiry", await File.ReadAllTextAsync(privateHistory, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(uncertainAttempt));
        Assert.Equal(durableCursor, new NativeLiveReadCursorStore(restartedWorker.StateDirectory,
            dmBinding.OpaqueRef, "project-pin-P3").Resolve(durableCursor with { Sequence = 9 },
                durableCursor with { Sequence = 0 }));
        Assert.Equal("lease-2", (await futureWorkBinding.Task.WaitAsync(TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken)).MembershipLeaseRef);
        Assert.Equal("completed", (await restartedWorker.Completion).Status);

        var liveConfigFile = Path.Combine(root, "generated-member-live.json");
        await File.WriteAllTextAsync(liveConfigFile, JsonSerializer.Serialize(artifact.LiveConfig), TestContext.Current.CancellationToken);
        using var output = new StringWriter();
        Assert.Equal(2, await NativeLiveCommand.RunAsync(["--native-live", liveConfigFile, "--authorize-continuous-dm"],
            new StringReader("/quit"), output, TestContext.Current.CancellationToken));
        Assert.Contains("invalid_host_configuration", output.ToString());
    }

    private static string MemberDiscovery(object[] scopes) => JsonSerializer.Serialize(new
    {
        version = "native-group-member-dm-discovery:v1",
        source = new { accountId = "account-a", groupJid = "120363318973251043@g.us", groupSessionRef = "group-session" },
        enrollment = "enabled", scopes,
    });

    private static object MemberScope(string status, string? nativeId, string[]? aliases, string? scopeRef, string? lease = "lease-1") => new
    {
        status, scopeRef, nativeConversationId = nativeId, nativeAliases = aliases,
        membershipLeaseRef = lease, leaseExpiresAt = (long?)1790000000,
        reader = status == "ready" ? new { endpoint = "http://127.0.0.1:3456/", version = "inbound-observation:v1",
            routeBindingRef = "route-ref", effectiveConfigRevision = "dispatch-rev" } : null,
        epoch = status == "ready" ? "dm-epoch" : null,
        initialCursor = status == "ready" ? new { version = "inbound-observation:v1", epoch = "dm-epoch", sessionRef = scopeRef, sequence = 0 } : null,
        failureCode = status == "failed" ? "membership_denied" : null,
    };

    private sealed class JsonHandler(Func<HttpRequestMessage, JsonNode> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request).ToJsonString()) });
    }

    private static ShadowSessionBinding Binding(string nativeId, SessionEntryKind kind)
    {
        var session = new SessionKey("runtime", "filomena", "whatsapp", "account-a", nativeId);
        var conversationNamespace = FilomenaIdentity.WhatsAppConversationNamespace(nativeId);
        return new(session, conversationNamespace, ["whatsapp:15559999@s.whatsapp.net"],
            ["whatsapp:" + nativeId], kind);
    }

    private static void SaveAndResolve(string directory, ShadowSessionBinding binding, string version, string epoch, long sequence)
    {
        var store = new NativeLiveReadCursorStore(directory, binding.OpaqueRef, "pinned-R1");
        var expected = new NativeObservationCursor(version, epoch, binding.OpaqueRef, sequence);
        store.Save(expected);
        Assert.Equal(expected, new NativeLiveReadCursorStore(directory, binding.OpaqueRef, "pinned-R1")
            .Resolve(expected with { Sequence = sequence + 3 }, expected with { Sequence = 0 }));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
