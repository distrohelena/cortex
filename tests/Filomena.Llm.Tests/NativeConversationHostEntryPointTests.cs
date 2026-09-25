using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Observation;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeConversationHostEntryPointTests
{
    [Fact]
    public async Task Native_generated_fixture_roundtrips_discovery_route_and_admission_metadata()
    {
        var fixturePath = Path.Combine(ParticipationGateV1Tests.Root,
            "tests/Filomena.Llm.Tests/Fixtures/native-member-dm-fixture.json");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken));
        var root = fixture.RootElement;
        var group = root.GetProperty("groupDiscovery");
        var groupSessionRef = group.GetProperty("source").GetProperty("groupSessionRef").GetString()!;
        var discovery = NativeMemberDmDiscoveryValidator.Parse(group, "default", "123456@g.us", groupSessionRef);
        var scope = Assert.Single(discovery.Scopes);
        var session = new SessionKey("runtime", "filomena", "whatsapp", "default", scope.NativeConversationId!);
        Assert.Equal(scope.RouteBindingRef, NativeObservationReader.RouteBindingRef(session));
        Assert.Equal("http://127.0.0.1:64683/inbound-observation/v1", scope.ReaderEndpoint);
        Assert.Equal(new[] { "15550000000@s.whatsapp.net", "987654321@lid" }, scope.NativeAliases);

        var effectiveRevision = scope.EffectiveConfigRevision!;
        var fixtureEvent = root.GetProperty("poll").GetProperty("events")[0];
        var status = new
        {
            version = "inbound-observation:v1", health = "healthy", sessionRef = scope.ScopeRef,
            channel = "whatsapp", kind = "dm", routeBindingRef = scope.RouteBindingRef,
            journalEpoch = scope.Epoch, selfIdentityRef = fixtureEvent.GetProperty("selfIdentityRef").GetString(),
            peerActorRef = fixtureEvent.GetProperty("actorRef").GetString(),
            capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(key => key, _ => true),
            dispatch = new { version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = effectiveRevision,
                paths = Array.Empty<object>() },
        };
        using var client = new HttpClient(new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal) ? status : root.GetProperty("poll")));
        var reader = new NativeObservationReader(client, new Uri(scope.ReaderEndpoint!), "fixture-token",
            new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
                ["whatsapp:200@s.whatsapp.net"], ["whatsapp:" + session.NativeConversationId]),
            scope.ScopeRef!, "whatsapp:200@s.whatsapp.net", "whatsapp:" + session.NativeConversationId, session,
            trustedManagedMemberDmAliases: scope.NativeAliases);
        var inspection = await reader.InspectAsync(TestContext.Current.CancellationToken);
        var approval = await reader.PreflightAsync(TestContext.Current.CancellationToken);
        Assert.Equal(scope.RouteBindingRef, inspection.RouteBindingRef);
        var initialCursor = scope.InitialCursor!;
        var batch = await reader.PollAsync(approval,
            new NativeObservationCursor(initialCursor.Version, initialCursor.Epoch, initialCursor.SessionRef, initialCursor.Sequence),
            TestContext.Current.CancellationToken);
        var record = Assert.Single(batch.Records);

        Assert.Equal("fixture-native-message-id", record.NativeMessageId);
        Assert.Equal("native-member-dm-admission:v1", record.MembershipAuthority!.Version);
        Assert.Equal(scope.MembershipLeaseRef, record.MembershipAuthority.MembershipLeaseRef);
        Assert.Equal(scope.ScopeRef, record.SessionRef);
        Assert.Equal(scope.Epoch, record.JournalEpoch);
        Assert.Equal(1, batch.Cursor.Sequence);
        Assert.Equal("987654321@lid", record.Payload.GetProperty("key").GetProperty("remoteJid").GetString());
    }

    [Fact]
    public async Task Native_generated_fixture_with_unavailable_child_control_is_not_provisionable()
    {
        var fixturePath = Path.Combine(ParticipationGateV1Tests.Root,
            "tests/Filomena.Llm.Tests/Fixtures/native-member-dm-fixture.json");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken));
        var root = JsonNode.Parse(fixture.RootElement.GetProperty("childStatus").GetRawText())!.AsObject();
        var bootstrap = root["bootstrap"]!.AsObject();
        Assert.False(bootstrap["controlAvailable"]!.GetValue<bool>());
        root["dispatch"] = new JsonObject
        {
            ["effectiveConfigRevision"] = bootstrap["effectiveConfigRevision"]!.DeepClone(),
        };
        var discovery = fixture.RootElement.GetProperty("groupDiscovery");
        var scope = discovery.GetProperty("scopes")[0];
        var session = new SessionKey("runtime", "filomena", "whatsapp", "default",
            scope.GetProperty("nativeConversationId").GetString()!);
        using var client = new HttpClient(new FakeHandler(_ => root));
        var reader = new NativeObservationReader(client,
            new Uri(scope.GetProperty("reader").GetProperty("endpoint").GetString()!), "fixture-token",
            new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
                ["whatsapp:200@s.whatsapp.net"], ["whatsapp:" + session.NativeConversationId]),
            scope.GetProperty("scopeRef").GetString()!, "whatsapp:200@s.whatsapp.net",
            "whatsapp:" + session.NativeConversationId, session);

        var checks = await reader.ProvisioningChecksAsync(false, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(checks, check => check.Id == "control_surface").Passed);
        Assert.Contains(checks, check => !check.Passed);
    }

    [Fact]
    public async Task Cli_host_keeps_group_checkpoint_starts_two_members_ignores_stranger_and_reuses_workers_on_rediscovery()
    {
        var root = Directory.CreateTempSubdirectory("native-conversation-host-entry-");
        var groupDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "existing-group-state")).FullName;
        var checkpoint = Path.Combine(groupDirectory, "history-checkpoint.json");
        await File.WriteAllTextAsync(checkpoint, "keep-existing-group-history", TestContext.Current.CancellationToken);
        var dmRoot = Path.Combine(root.FullName, "members");
        const string groupJid = "120363318973251043@g.us", groupSessionRef = "group-session";
        const string account = "account-a", readerTokenEnvironment = "FILOMENA_TEST_MEMBER_READER_TOKEN";
        var priorToken = Environment.GetEnvironmentVariable(readerTokenEnvironment);
        Environment.SetEnvironmentVariable(readerTokenEnvironment, "fake-reader-token-never-printed");
        try
        {
            var projectPath = Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-group-shadow-project-v3-gpt6-luna.json");
            var projectBytes = await File.ReadAllBytesAsync(projectPath, TestContext.Current.CancellationToken);
            var projectSha = Convert.ToHexString(SHA256.HashData(projectBytes));
            var pin = PinnedShadowProject.FromBytes(projectBytes, "group-pilot-test", 3, projectSha);
            var groupSession = new SessionKey("runtime", "filomena", "whatsapp", account, groupJid);
            const string self = "whatsapp:200@s.whatsapp.net", groupPeerNative = "15550001@s.whatsapp.net";
            var groupPeer = "whatsapp:" + groupPeerNative;
            var groupBinding = new ShadowSessionBinding(groupSession,
                FilomenaIdentity.WhatsAppConversationNamespace(groupJid), [self], [groupPeer], SessionEntryKind.Group);
            var groupRoles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
                new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                ["group-validator-1", "group-validator-2"], "participation-engaged-v1");
            var groupManualPath = Path.Combine(root.FullName, "group-manual.json");
            var groupManual = new ManualShadowConfig(projectPath, "group-pilot-test", 3, projectSha,
                groupSession, FilomenaIdentity.WhatsAppConversationNamespace(groupJid), [self], groupPeer, "Group",
                groupRoles, groupDirectory, "http://127.0.0.1:2233", [
                    new(groupBinding.ConversationNamespace, groupPeer, "local-control-a", "foreign-a", "foreign-control-a"),
                    new(groupBinding.ConversationNamespace, groupPeer, "local-control-b", "foreign-b", "foreign-control-b")]);
            await WriteJson(groupManualPath, groupManual);
            var participantsPath = Path.Combine(root.FullName, "participants.json");
            await WriteJson(participantsPath, new Dictionary<string, string> { [groupPeerNative] = groupPeer });

            var groupNativePath = Path.Combine(root.FullName, "group-native.json");
            var groupNative = new NativeShadowConfig(groupManualPath, "http://127.0.0.1:2345/inbound-observation/v2",
                readerTokenEnvironment, groupSessionRef, groupJid, "group-native-revision", groupDirectory,
                GroupParticipants: new Dictionary<string, string> { [groupPeerNative] = groupPeer });
            await WriteJson(groupNativePath, groupNative);
            var capabilities = new TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var groupEvidencePath = Path.Combine(root.FullName, "group-proof.json");
            var groupEvidence = new { version = "native-live-capabilities:v1", approvedRevision = "group-native-revision",
                observationSessionRef = groupSessionRef,
                routeBindingRef = NativeObservationReader.RouteBindingRef(groupSession),
                boundSendContract = "whatsapp-bound-group-text:v1", capabilities, maxInteractions = 1,
                revalidateReceiverWithinWindow = false, continuousGroup = true, continueAfter = (NativeObservationCursor?)null,
                allowNativeGroupParticipants = false, continuousDm = false, managedMemberDm = false };
            await WriteJson(groupEvidencePath, groupEvidence);
            var groupEvidenceBytes = await File.ReadAllBytesAsync(groupEvidencePath, TestContext.Current.CancellationToken);
            var groupLivePath = Path.Combine(root.FullName, "group-live.json");
            var groupLive = new NativeLiveConfig(groupNativePath, new("fake-mcp", []), "group-route", "Filomena", 1000,
                capabilities, groupEvidencePath, Convert.ToHexString(SHA256.HashData(groupEvidenceBytes)),
                AllowExactGroupDelivery: true, ContinuousGroup: true);
            await WriteJson(groupLivePath, groupLive);

            var memberTemplatePath = Path.Combine(root.FullName, "member-template.json");
            var memberTemplate = new NativeLiveConfig(groupNativePath, new("fake-mcp", []), "member-route", "Filomena", 1000,
                capabilities, "unused-template-proof.json", "unused-template-hash", ContinuousDm: true, ManagedMemberDm: true);
            await WriteJson(memberTemplatePath, memberTemplate);
            var hostConfigPath = Path.Combine(root.FullName, "conversation-host.json");
            await WriteJson(hostConfigPath, new NativeConversationHostConfig(groupLivePath, memberTemplatePath, dmRoot,
                account, groupJid, pin.Project.ProjectRevision, "operator-foreign-namespace", "operator-foreign-control",
                DiscoveryIntervalMilliseconds: 30000));

            var scope1 = Scope("15550002@s.whatsapp.net", "dm-scope-2", "lease-2", "dm-epoch-2", NativeRevision(), ["15550002@s.whatsapp.net", "991002@lid"]);
            var scope2 = Scope("15550003@s.whatsapp.net", "dm-scope-3", "lease-3", "dm-epoch-3", NativeRevision(), ["15550003@s.whatsapp.net", "991003@lid"]);
            var stranger = new { status = "failed", scopeRef = (string?)null, nativeConversationId = "15559999@s.whatsapp.net",
                nativeAliases = Array.Empty<string>(), membershipLeaseRef = (string?)null, leaseExpiresAt = (long?)null,
                reader = (object?)null, epoch = (string?)null, initialCursor = (object?)null, failureCode = "membership_denied" };
            var scopes = new JsonArray(JsonSerializer.SerializeToNode(scope1), JsonSerializer.SerializeToNode(scope2),
                JsonSerializer.SerializeToNode(stranger));
            var fakeNative = new FakeNativeBoundary(groupSession, groupSessionRef, account, groupJid, scopes);
            var startedMembers = Channel.CreateUnbounded<(string LivePath, string StateDirectory, NativeMemberDmExecutionContext Context)>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
            var groupStarted = new TaskCompletionSource<(string LivePath, string StateDirectory)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var memberStarts = 0;
            var dependencies = new NativeConversationHostDependencies(
                _ => new HttpClient(new FakeHandler(fakeNative.Respond)),
                async (livePath, stateDirectory, _, _, token) =>
                {
                    groupStarted.TrySetResult((livePath, stateDirectory));
                    await WaitForCancellationAsync(token);
                    return 0;
                },
                async (livePath, stateDirectory, context, _, _, token) =>
                {
                    Interlocked.Increment(ref memberStarts);
                    await startedMembers.Writer.WriteAsync((livePath, stateDirectory, context), token);
                    await WaitForCancellationAsync(token);
                    return 0;
                });

            var lines = Channel.CreateUnbounded<string>();
            using var input = new ChannelTextReader(lines.Reader);
            using var output = new StringWriter();
            using var stop = new CancellationTokenSource();
            var running = NativeConversationHostCommand.RunAsync(["--native-conversation-host", hostConfigPath],
                input, output, stop.Token, dependencies);
            var groupWorker = await groupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(Path.GetFullPath(groupLivePath), groupWorker.LivePath);
            Assert.Equal(groupDirectory, groupWorker.StateDirectory);
            await lines.Writer.WriteAsync("/refresh", TestContext.Current.CancellationToken);
            async Task<(string LivePath, string StateDirectory, NativeMemberDmExecutionContext Context)> ReadMemberStartAsync()
            {
                try
                {
                    return await startedMembers.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                }
                catch (TimeoutException) { throw new Xunit.Sdk.XunitException(output.ToString()); }
            }
            var first = await ReadMemberStartAsync();
            var second = await ReadMemberStartAsync();

            await lines.Writer.WriteAsync("/refresh", TestContext.Current.CancellationToken);
            await lines.Writer.WriteAsync("/quit", TestContext.Current.CancellationToken);
            Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            Assert.True(fakeNative.ReadyDiscoveryCount >= 2);
            Assert.Equal(2, memberStarts);
            Assert.NotEqual(first.StateDirectory, second.StateDirectory);
            Assert.All(new[] { first, second }, item =>
            {
                Assert.NotEqual(groupDirectory, item.StateDirectory);
                Assert.StartsWith(Path.GetFullPath(dmRoot), item.StateDirectory, StringComparison.OrdinalIgnoreCase);
                var live = JsonSerializer.Deserialize<NativeLiveConfig>(File.ReadAllText(item.LivePath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.True(live.ManagedMemberDm);
                Assert.Contains("whatsapp-bound-member-dm-text:v1", File.ReadAllText(live.CapabilityEvidenceFile));
                Assert.DoesNotContain("fake-reader-token-never-printed", File.ReadAllText(item.LivePath));
                var native = JsonSerializer.Deserialize<NativeShadowConfig>(File.ReadAllText(live.NativeConfigFile),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal("operator-foreign-namespace", native.NewDmMemoryReadiness!.ForeignNamespace);
                Assert.Equal("operator-foreign-control", native.NewDmMemoryReadiness.ForeignCanary);
                Assert.Equal(item.Context.ScopeFacts.ScopeRef, native.ObservationSessionRef);
                Assert.Equal(item.Context.ScopeFacts.NativeConversationId, native.NativeConversationId);
            });
            Assert.Equal("keep-existing-group-history", await File.ReadAllTextAsync(checkpoint, TestContext.Current.CancellationToken));
            Assert.DoesNotContain("15559999@s.whatsapp.net", output.ToString());
            Assert.DoesNotContain("fake-reader-token-never-printed", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(readerTokenEnvironment, priorToken);
            if (Directory.Exists(root.FullName)) Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static object Scope(string peer, string scopeRef, string lease, string epoch, string revision, string[] aliases)
    {
        var session = new SessionKey("runtime", "filomena", "whatsapp", "account-a", peer);
        return new { status = "ready", scopeRef, nativeConversationId = peer, nativeAliases = aliases,
            membershipLeaseRef = lease, leaseExpiresAt = (long?)1790000000,
            reader = new { endpoint = "http://127.0.0.1:3456/inbound-observation/v1", version = "inbound-observation:v1",
                routeBindingRef = NativeObservationReader.RouteBindingRef(session), effectiveConfigRevision = revision },
            epoch, initialCursor = new { version = "inbound-observation:v1", epoch, sessionRef = scopeRef, sequence = 0 },
            failureCode = (string?)null };
    }

    private static string NativeRevision() => "base:producer-00000000-0000-0000-0000-000000000001:"
        + new string('a', 64) + ":registry-1:isolation-G1";

    private static async Task WriteJson<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

    private static Task WaitForCancellationAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (token.IsCancellationRequested) completion.TrySetResult();
        else token.Register(() => completion.TrySetResult());
        return completion.Task;
    }

    private sealed class FakeNativeBoundary(SessionKey groupSession, string groupSessionRef, string account, string groupJid,
        JsonArray scopes)
    {
        private int readyDiscoveryCount;
        public int ReadyDiscoveryCount => Volatile.Read(ref readyDiscoveryCount);

        public object Respond(HttpRequestMessage request)
        {
            Assert.Equal("Bearer fake-reader-token-never-printed", request.Headers.Authorization!.ToString());
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v2/status", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref readyDiscoveryCount);
                return new { version = "inbound-observation:v2", sessionRef = groupSessionRef,
                    channel = "whatsapp", kind = "group", routeBindingRef = NativeObservationReader.RouteBindingRef(groupSession),
                    memberDmDiscovery = new { version = "native-group-member-dm-discovery:v1",
                        source = new { accountId = account, groupJid, groupSessionRef }, enrollment = "enabled", scopes } };
            }
            var scope = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?').Split('&')
                .Single(part => part.StartsWith("session=", StringComparison.Ordinal))["session=".Length..]);
            var descriptor = scopes.Select(node => node!.AsObject()).Single(node => node["scopeRef"]?.GetValue<string>() == scope);
            var peer = descriptor["nativeConversationId"]!.GetValue<string>();
            var epoch = descriptor["epoch"]!.GetValue<string>();
            var revision = descriptor["reader"]!["effectiveConfigRevision"]!.GetValue<string>();
            var session = groupSession with { NativeConversationId = peer };
            var route = NativeObservationReader.RouteBindingRef(session);
            var proof = new { status = "valid", version = "effective-runtime-config:v1", revision = "base" };
            var gate = new { gateVersion = "native-dm-isolation:v1", gateInstanceRevision = "G1",
                approvedEffectiveConfigRevision = "base", accountRef = "account-a", sessionRef = scope,
                coveredPathIds = new[] { "commands" }, placementProof = "placement", lifecycleState = "armed" };
            var producer = "00000000-0000-0000-0000-000000000001:" + new string('a', 64);
            var paths = new[] { new { family = "commands", status = "inactive", rawStatus = "inactive",
                effectiveStatus = "inactive", effectiveConfigRevision = "base" } };
            return new { version = "inbound-observation:v1", health = "healthy", sessionRef = scope,
                channel = "whatsapp", kind = "dm", routeBindingRef = route, journalEpoch = epoch,
                selfIdentityRef = "self-ref", peerActorRef = "peer-ref",
                capabilities = NativeObservationPreflight.RequiredCapabilities.ToDictionary(key => key, _ => true),
                dispatch = new { version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = revision,
                    effectiveSnapshotProof = proof, isolationGate = gate, producerRevision = producer, paths },
                bootstrap = new { subsystemOperational = true, readerAvailable = true, journalHealth = "healthy",
                    captureState = "armed", activeEpoch = epoch, controlAvailable = true, configSnapshotAvailable = true,
                    dispatchInspectorAvailable = true, effectiveConfigRevision = revision, effectiveSnapshotProof = proof,
                    isolationGate = gate, legacyRecovery = new { status = "clear", effectiveConfigRevision = "base",
                        uncoveredOwnerCount = 0, activeOwnerCount = 0, pendingLegacyRecoveryCount = 0, quarantinedCount = 0 } } };
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(response(request))) });
    }

    private sealed class ChannelTextReader(ChannelReader<string> reader) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await reader.ReadAsync(cancellationToken);
    }
}
