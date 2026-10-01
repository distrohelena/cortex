using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Filomena.App;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Cortex.Inference;
using Cortex.Memory;
using Cortex.OpenClaw.Mcp;
using Cortex.OpenClaw.Observation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeConversationHostCompositionTests
{
    [Fact]
    public async Task Managed_member_runtime_runs_through_host_command_with_real_guards_and_external_fakes()
    {
        var root = Directory.CreateTempSubdirectory("member-dm-runtime-composition-");
        var peer = "15555550123@s.whatsapp.net";
        const string account = "synthetic-account", groupJid = "123456@g.us", groupRef = "synthetic-group-session";
        const string scopeRef = "dm-scope-2", leaseRef = "lease-2", epoch = "dm-epoch-2";
        var native = new SyntheticNativeWireServer(account, groupJid, groupRef, peer, scopeRef, leaseRef, epoch);
        var tokenName = "FILOMENA_TEST_MEMBER_RUNTIME_READER_TOKEN";
        var priorToken = Environment.GetEnvironmentVariable(tokenName);
        var priorOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Task<int>? running = null;
        CancellationTokenSource? stop = null;
        Exception? testFailure = null;
        Exception? cleanupFailure = null;
        var lines = Channel.CreateUnbounded<string>();
        var input = new ChannelTextReader(lines.Reader);
        var output = new ChannelTextWriter();
        Environment.SetEnvironmentVariable(tokenName, "synthetic-reader-token");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "synthetic-no-network-key");
        try
        {
            var projectPath = Path.Combine(ParticipationGateV1Tests.Root, "docs/manual-group-shadow-project-v3-gpt6-luna.json");
            var projectBytes = await File.ReadAllBytesAsync(projectPath, TestContext.Current.CancellationToken);
            var projectSha = Convert.ToHexString(SHA256.HashData(projectBytes));
            var pin = PinnedShadowProject.FromBytes(projectBytes, "group-pilot-test", 3, projectSha);
            var groupDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "existing-group-state")).FullName;
            var stateRoot = Path.Combine(root.FullName, "members");
            var session = new SessionKey("runtime", "filomena", "whatsapp", account, groupJid);
            const string self = "whatsapp:999999@s.whatsapp.net", groupPeerNative = "111111@s.whatsapp.net";
            var groupPeer = "whatsapp:" + groupPeerNative;
            var capabilities = new TransportSafetyCapabilities(true, true, true, true, true, true, true);
            var roles = new OutboundBindings(["group-response-boundaries"], ["group-compiled-boundaries"],
                new Dictionary<string, string> { ["group-generation"] = "texto", ["group-regeneration"] = "texto" },
                ["group-validator-1", "group-validator-2"], "participation-engaged-v1")
            {
                ReplyTargetPorts = new Dictionary<string, string> { ["group-generation"] = "replyTo" }
            };
            var groupBinding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(groupJid),
                [self], [groupPeer], SessionEntryKind.Group);
            var manualPath = Path.Combine(root.FullName, "group-manual.json");
            await WriteJson(manualPath, new ManualShadowConfig(projectPath, "group-pilot-test", 3, projectSha,
                session, groupBinding.ConversationNamespace, [self], groupPeer, "Group", roles, groupDirectory,
                "http://127.0.0.1:8000/mcp", [
                    new ShadowGraphitiProbeRequest(groupBinding.ConversationNamespace, groupPeer, "local-peer-1", "foreign-peer-1", "foreign-canary-1"),
                    new ShadowGraphitiProbeRequest(groupBinding.ConversationNamespace, groupPeer, "local-peer-2", "foreign-peer-2", "foreign-canary-2")
                ]));
            var nativePath = Path.Combine(root.FullName, "group-native.json");
            var groupNative = new NativeShadowConfig(manualPath, native.Endpoint("v2").AbsoluteUri, tokenName,
                groupRef, groupJid, "group-native-revision", groupDirectory,
                GroupParticipants: new Dictionary<string, string> { [groupPeerNative] = groupPeer });
            await WriteJson(nativePath, groupNative);
            var evidencePath = Path.Combine(root.FullName, "group-proof.json");
            var groupEvidence = new
            {
                version = "native-live-capabilities:v1", approvedRevision = "group-native-revision",
                observationSessionRef = groupRef, routeBindingRef = NativeObservationReader.RouteBindingRef(session),
                boundSendContract = "whatsapp-bound-group-reply:v1", capabilities, maxInteractions = 1,
                revalidateReceiverWithinWindow = false, continuousGroup = true,
                continueAfter = (NativeObservationCursor?)null, allowNativeGroupParticipants = false,
                allowNativeGroupReplies = true, continuousDm = false, managedMemberDm = false
            };
            await WriteJson(evidencePath, groupEvidence);
            var evidenceSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(evidencePath,
                TestContext.Current.CancellationToken)));
            var groupLivePath = Path.Combine(root.FullName, "group-live.json");
            var groupLive = new NativeLiveConfig(nativePath, new("fake-mcp", []), "group-route", "Filomena", 1000,
                capabilities, evidencePath, evidenceSha, AllowExactGroupDelivery: true,
                ContinuousGroup: true, AllowNativeGroupReplies: true);
            await WriteJson(groupLivePath, groupLive);
            var memberTemplatePeer = "15555550999@s.whatsapp.net";
            var memberTemplateSession = session with { NativeConversationId = memberTemplatePeer };
            var memberRoles = new OutboundBindings(["dm-response-boundaries"], ["dm-compiled-boundaries"],
                new Dictionary<string, string> { ["dm-generation"] = "texto", ["dm-regeneration"] = "texto" },
                ["dm-validator-1", "dm-validator-2"], "participation-engaged-v1");
            var memberManualPath = Path.Combine(root.FullName, "member-template-manual.json");
            await WriteJson(memberManualPath, new ManualShadowConfig(projectPath, "group-pilot-test", 3, projectSha,
                memberTemplateSession, FilomenaIdentity.WhatsAppConversationNamespace(memberTemplatePeer), [self],
                "whatsapp:" + memberTemplatePeer, "Template DM", memberRoles, groupDirectory,
                "http://127.0.0.1:8000/mcp", []));
            var memberNativePath = Path.Combine(root.FullName, "member-template-native.json");
            await WriteJson(memberNativePath, groupNative with
            {
                ManualConfigFile = memberManualPath,
                NativeConversationId = memberTemplatePeer,
                JournalDirectory = groupDirectory,
                GroupParticipants = null,
            });
            var memberTemplatePath = Path.Combine(root.FullName, "member-template.json");
            await WriteJson(memberTemplatePath, new NativeLiveConfig(memberNativePath, new("fake-mcp", []), "member-route",
                "Filomena", 1000, capabilities, "template-proof.json", "template-hash",
                ContinuousDm: true, ManagedMemberDm: true));
            var hostPath = Path.Combine(root.FullName, "host.json");
            await WriteJson(hostPath, new NativeConversationHostConfig(groupLivePath, memberTemplatePath, stateRoot,
                account, groupJid, pin.Project.ProjectRevision, "foreign-namespace", "foreign-canary",
                DiscoveryIntervalMilliseconds: 250));

            var graphiti = new FakeGraphiti("foreign-namespace", "foreign-canary");
            var mcp = new FakeMcp(peer, account);
            var completions = new ScriptedDmCompletion();
            var httpFactoryCalls = 0;
            var factories = new NativeShadowExternalFactories(
                (_, _) => (graphiti, graphiti),
                (_, _, _) => Task.FromResult<IMcpToolClient>(mcp),
                (_, _) =>
                {
                    Interlocked.Increment(ref httpFactoryCalls);
                    return new HttpClient(new RejectingHttpHandler());
                },
                (_, _) => completions);
            ManualShadowConfig? generatedMemberManual = null;
            var dependencies = new NativeConversationHostDependencies(
                _ => new HttpClient(new NativeHandler(native.Respond)),
                async (_, _, _, _, cancellation) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                    return 0;
                },
                async (livePath, stateDirectory, context, input, output, cancellation) =>
                {
                    var generatedLive = JsonSerializer.Deserialize<NativeLiveConfig>(File.ReadAllText(livePath),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    var generatedNative = JsonSerializer.Deserialize<NativeShadowConfig>(File.ReadAllText(generatedLive.NativeConfigFile),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    generatedMemberManual = JsonSerializer.Deserialize<ManualShadowConfig>(File.ReadAllText(generatedNative.ManualConfigFile),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    return await NativeLiveCommand.RunManagedMemberDmWithFactoriesAsync(
                        ["--native-live", livePath, "--authorize-continuous-dm"], input, output,
                        cancellation, context, factories);
                });

            stop = new CancellationTokenSource();
            running = NativeConversationHostCommand.RunAsync(["--native-conversation-host", hostPath],
                input, output, stop.Token, dependencies);

            var runStarted = await output.ReadUntilAsync(line =>
                line.TryGetProperty("phase", out var phase) && phase.GetString() == "run_started"
                && line.TryGetProperty("source", out var source) && source.GetString() == "native_observation_v1",
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("native_observation_v1", runStarted.GetProperty("source").GetString());
            await lines.Writer.WriteAsync("/poll", TestContext.Current.CancellationToken);
            var interaction = await output.ReadUntilAsync(line =>
                line.TryGetProperty("phase", out var phase) && phase.GetString() == "interaction",
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(interaction.GetProperty("outcome").GetString() == "SENT", interaction.GetRawText());
            Assert.Contains(interaction.GetProperty("decisions").EnumerateArray(), decision =>
                decision.GetProperty("status").GetString() == "admitted");
            Assert.Single(native.OpenedScopes, opened => opened == scopeRef);
            Assert.Single(interaction.GetProperty("outcomes").EnumerateArray());
            Assert.Single(interaction.GetProperty("nativeEventRefs").EnumerateArray());
            var send = Assert.Single(mcp.SendCalls);
            Assert.Equal("messages_send_bound", send.ToolName);
            Assert.Equal("whatsapp-bound-member-dm-text:v1", send.ContractVersion);
            Assert.Equal(peer, send.Recipient);
            Assert.Equal("synthetic-account", send.AccountId);
            Assert.Equal(scopeRef, send.ScopeRef);
            Assert.Equal(SyntheticNativeWireServer.MemberRevision, send.EffectiveConfigRevision);
            Assert.Equal(leaseRef, send.MembershipLeaseRef);
            Assert.Equal(peer, send.NativeConversationId);
            Assert.Equal("synthetic-provider-receipt", send.ReceiptMessageId);
            Assert.NotNull(generatedMemberManual);
            Assert.Equal(memberRoles.RecallNodes, generatedMemberManual.OutboundBindings.RecallNodes);
            Assert.Equal(memberRoles.CompilerNodes, generatedMemberManual.OutboundBindings.CompilerNodes);
            Assert.Equal(memberRoles.CandidatePorts.OrderBy(pair => pair.Key),
                generatedMemberManual.OutboundBindings.CandidatePorts.OrderBy(pair => pair.Key));
            Assert.Equal(memberRoles.EnforcementNodes, generatedMemberManual.OutboundBindings.EnforcementNodes);
            Assert.Empty(generatedMemberManual.OutboundBindings.ReplyTargetPorts);
            Assert.Contains("conversation_get", mcp.Calls);
            Assert.NotEmpty(graphiti.Calls);
            Assert.Equal(1, httpFactoryCalls);
            Assert.Contains("dm-situation", completions.Calls);
            Assert.DoesNotContain("dm-presence", completions.Calls);
            Assert.Contains("dm-generation", completions.Calls);
            await lines.Writer.WriteAsync("/quit", TestContext.Current.CancellationToken);
            Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        catch (Exception error)
        {
            testFailure = error;
        }
        finally
        {
            var hostDrained = running is null;
            try
            {
                try
                {
                    stop?.Cancel();
                    if (running is not null)
                    {
                        try { await running; }
                        finally { hostDrained = running.IsCompleted; }
                    }
                }
                catch (Exception error) { cleanupFailure = error; }
            }
            finally
            {
                Environment.SetEnvironmentVariable(tokenName, priorToken);
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", priorOpenAiKey);
            }
            try { lines.Writer.TryComplete(); output.Dispose(); }
            catch (Exception error) { cleanupFailure = cleanupFailure is null ? error : new AggregateException(cleanupFailure, error); }
            try { await native.DisposeAsync(); }
            catch (Exception error) { cleanupFailure = cleanupFailure is null ? error : new AggregateException(cleanupFailure, error); }
            try
            {
                if (Directory.Exists(root.FullName))
                {
                    if (!hostDrained)
                        throw new IOException("Host did not drain; preserving temporary state instead of deleting active worker files.");
                    var reports = Directory.GetFiles(root.FullName, "native-live-*.jsonl", SearchOption.AllDirectories);
                    if (testFailure is null) Assert.Single(reports);
                    foreach (var reportPath in reports)
                    {
                        using var reportReleased = new FileStream(reportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    Directory.Delete(root.FullName, recursive: true);
                }
            }
            catch (Exception error) { cleanupFailure = cleanupFailure is null ? error : new AggregateException(cleanupFailure, error); }
            stop?.Dispose();
        }
        if (testFailure is not null && cleanupFailure is not null)
            throw new AggregateException("Composition smoke failed and cleanup also failed.", testFailure, cleanupFailure);
        if (testFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(testFailure).Throw();
        if (cleanupFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private static async Task WriteJson<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

    private sealed class ScriptedDmCompletion : IFlowCompletion
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Calls.Enqueue(request.NodeId);
            return request.NodeId switch
            {
                "dm-situation" => Task.FromResult("{\"ambiente\":\"casual\",\"envolvimento\":\"direto\",\"ritmo\":\"aberto\",\"necessita_cuidado\":false,\"oportunidade_social\":\"fraca\",\"resumo\":\"troca sintética dirigida\"}"),
                "dm-presence" => Task.FromResult("{\"acao\":\"responder\",\"continuar_acompanhando\":false,\"motivo\":\"chamada_direta\"}"),
                "dm-generation" or "dm-regeneration" => Task.FromResult("Olá, resposta sintética."),
                "dm-validator-1" or "dm-validator-2" => Task.FromResult("{}"),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected completion call for {request.NodeId}; fake is fail-closed.")
            };
        }
    }

    private sealed class FakeGraphiti(string foreignNamespace, string canary) : IGraphitiReadClient, IAsyncDisposable
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid participant, int limit, CancellationToken token)
        {
            Calls.Enqueue("neighborhood:" + ns);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                status = "entity_not_found", version = "entity-neighborhood-absence:v1",
                group_id = ns, entity_uuid = participant, facts = Array.Empty<object>()
            }));
        }
        public Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token)
        {
            Calls.Enqueue("search:" + ns + ":" + query);
            var facts = ns == foreignNamespace && query == canary
                ? new object[] { Fact(ns, canary) } : [];
            return Task.FromResult(JsonSerializer.SerializeToElement(new { facts }));
        }
        private static object Fact(string ns, string text)
        {
            var person = MemoryIdentity.Person("whatsapp-dm-15555550123-s-whatsapp-net", "whatsapp:15555550123@s.whatsapp.net", null);
            return new
            {
                uuid = "fact", group_id = ns, fact = text, name = "LIKES",
                source_node_uuid = person.ParticipantUuid, target_node_uuid = "entity",
                source_node = new { uuid = person.ParticipantUuid, stable_id = person.ActorStableId, labels = new[] { "Person" } },
                target_node = new { uuid = "entity", labels = new[] { "Entity" } }
            };
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMcp(string peer, string account) : IMcpToolClient
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public ConcurrentQueue<BoundSendCall> SendCalls { get; } = new();
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([new("messages_send_bound", "Synthetic bound-send", JsonSerializer.SerializeToElement(new { type = "object" }))]);
        public Task<JsonElement> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken token)
        {
            Calls.Enqueue(name);
            if (name == "conversation_get")
                return Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    conversation = new { channel = "whatsapp", accountId = account, to = peer }
                }));
            if (name != "messages_send_bound") throw new Xunit.Sdk.XunitException("Unexpected MCP tool " + name);
            var expected = (IReadOnlyDictionary<string, object?>)arguments["expected"]!;
            var call = new BoundSendCall(name, String(expected, "contractVersion"), String(expected, "accountId"),
                String(expected, "recipient"), String(expected, "nativeConversationId"), String(expected, "scopeRef"),
                String(expected, "routeBindingRef"), String(expected, "effectiveConfigRevision"), String(expected, "membershipLeaseRef"),
                "synthetic-provider-receipt");
            SendCalls.Enqueue(call);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                result = new { contractVersion = call.ContractVersion, attemptRef = String(expected, "attemptRef"),
                    channel = "whatsapp", accountId = call.AccountId, recipient = call.Recipient,
                    messageId = call.ReceiptMessageId, nativeConversationId = call.NativeConversationId,
                    scopeRef = call.ScopeRef, routeBindingRef = call.RouteBindingRef,
                    effectiveConfigRevision = call.EffectiveConfigRevision, membershipLeaseRef = call.MembershipLeaseRef }
            }));
        }
        private static string String(IReadOnlyDictionary<string, object?> values, string key) =>
            values.TryGetValue(key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)! : "";
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record BoundSendCall(string ToolName, string ContractVersion, string AccountId, string Recipient,
        string NativeConversationId, string ScopeRef, string RouteBindingRef, string EffectiveConfigRevision,
        string MembershipLeaseRef, string ReceiptMessageId);

    private sealed class RejectingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new Xunit.Sdk.XunitException("No inference HTTP call is expected before the first poll.");
    }

    private sealed class NativeHandler(Func<HttpRequestMessage, object> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(respond(request))) });
    }

    private sealed class ChannelTextReader(ChannelReader<string> reader) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken token) => await reader.ReadAsync(token);
    }

    private sealed class ChannelTextWriter : TextWriter
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
        private readonly ConcurrentQueue<string> history = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(string? value)
        {
            var line = value ?? "";
            history.Enqueue(line);
            return lines.Writer.WriteAsync(line).AsTask();
        }
        public async Task<JsonElement> ReadUntilAsync(Func<JsonElement, bool> predicate, TimeSpan timeout, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    var line = await lines.Reader.ReadAsync(deadline.Token);
                    using var document = JsonDocument.Parse(line);
                    var value = document.RootElement.Clone();
                    if (predicate(value)) return value;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for host output. Emitted lines: " + string.Join("\n", history));
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) lines.Writer.TryComplete(); base.Dispose(disposing); }
    }

    private sealed class SyntheticNativeWireServer : IAsyncDisposable
    {
        public const string MemberRevision = "base:producer-00000000-0000-0000-0000-000000000001:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:registry-1:isolation-G1";
        private readonly string account, groupJid, groupRef, peer, scopeRef, leaseRef, epoch;
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        public ConcurrentQueue<string> OpenedScopes { get; } = new();
        private readonly int port;
        public SyntheticNativeWireServer(string account, string groupJid, string groupRef, string peer,
            string scopeRef, string leaseRef, string epoch)
        {
            this.account = account; this.groupJid = groupJid; this.groupRef = groupRef;
            this.peer = peer; this.scopeRef = scopeRef; this.leaseRef = leaseRef; this.epoch = epoch;
            port = ReservePort();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            loop = Task.Run(ListenAsync);
        }
        public Uri Endpoint(string version) => new($"http://127.0.0.1:{port}/inbound-observation/{version}");
        public object Respond(HttpRequestMessage request)
        {
            var uri = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var session = query["session"] ?? "";
            var groupSession = session == groupRef;
            if (uri.AbsolutePath.EndsWith("/status", StringComparison.Ordinal))
                return groupSession ? GroupStatus() : MemberStatus();
            if (uri.AbsolutePath.EndsWith("/open", StringComparison.Ordinal))
            {
                OpenedScopes.Enqueue(session);
                return Batch(session, groupSession, open: true);
            }
            if (uri.AbsolutePath.EndsWith("/poll", StringComparison.Ordinal))
                return Batch(session, groupSession, open: false);
            throw new InvalidOperationException("Unexpected synthetic native path " + uri.AbsolutePath);
        }
        private object GroupStatus() => new
        {
            version = "inbound-observation:v2", health = "healthy", sessionRef = groupRef,
            channel = "whatsapp", kind = "group", routeBindingRef = NativeObservationReader.RouteBindingRef(
                new SessionKey("runtime", "filomena", "whatsapp", account, groupJid)),
            selfIdentityRef = SelfRef(groupRef), participantActorRefs = new[] { ActorRef(groupRef, "111111@s.whatsapp.net") },
            capabilities = Capabilities(), dispatch = Dispatch("group-native-revision"), bootstrap = Bootstrap("group-native-revision", "group-epoch", groupRef),
            memberDmDiscovery = new
            {
                version = "native-group-member-dm-discovery:v1",
                source = new { accountId = account, groupJid, groupSessionRef = groupRef }, enrollment = "enabled",
                scopes = new object[]
                {
                    new { status = "ready", scopeRef, nativeConversationId = peer, nativeAliases = new[] { peer },
                        membershipLeaseRef = leaseRef, leaseExpiresAt = (long?)1790000000,
                        reader = new { endpoint = Endpoint("v1").AbsoluteUri, version = "inbound-observation:v1",
                            routeBindingRef = NativeObservationReader.RouteBindingRef(DmSession()), effectiveConfigRevision = MemberRevision },
                        epoch, initialCursor = new NativeObservationCursor("inbound-observation:v1", epoch, scopeRef, 0), failureCode = (string?)null },
                    new { status = "failed", scopeRef = (string?)null, nativeConversationId = "15559999@s.whatsapp.net",
                        nativeAliases = Array.Empty<string>(), membershipLeaseRef = (string?)null, leaseExpiresAt = (long?)null,
                        reader = (object?)null, epoch = (string?)null, initialCursor = (object?)null, failureCode = "membership_denied" }
                }
            }
        };
        private object MemberStatus()
        {
            var dispatch = Dispatch(MemberRevision);
            dispatch["isolationGate"] = JsonSerializer.SerializeToNode(IsolationGate(scopeRef, true));
            dispatch["producerRevision"] = "00000000-0000-0000-0000-000000000001:" + new string('a', 64);
            return new
            {
                version = "inbound-observation:v1", health = "healthy", sessionRef = scopeRef, channel = "whatsapp", kind = "dm",
                routeBindingRef = NativeObservationReader.RouteBindingRef(DmSession()), journalEpoch = epoch,
                selfIdentityRef = SelfRef(scopeRef), peerActorRef = ActorRef(scopeRef, peer), capabilities = Capabilities(),
                dispatch, bootstrap = Bootstrap(MemberRevision, epoch, scopeRef, true)
            };
        }
        private int pollCount;
        private object Batch(string session, bool groupSession, bool open)
        {
            var group = groupSession;
            var sequence = open || group ? 0 : Volatile.Read(ref pollCount) == 0 ? 0 : 1;
            object[] events = [];
            if (!open && !group && Interlocked.Increment(ref pollCount) == 1)
            {
                sequence = 1;
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                events = [new
                {
                    sequence = 1, nativeMessageId = "synthetic-dm-event-1", sessionRef = session,
                    actorRef = ActorRef(scopeRef, peer), selfIdentityRef = SelfRef(scopeRef), fromMe = false,
                    journalEpoch = epoch,
                    membershipAuthority = new { version = "native-member-dm-admission:v1", membershipLeaseRef = leaseRef },
                    payload = new
                    {
                        receivedAt = timestamp,
                        message = new { key = new { remoteJid = peer, id = "synthetic-dm-event-1", fromMe = false },
                            message = new { conversation = "Olá, conversa sintética." }, messageTimestamp = timestamp / 1000 },
                        memberDmScope = new { accountId = account, groupJid, nativeConversationId = peer,
                            aliases = new[] { peer }, membershipLeaseRef = leaseRef },
                        observationIdentity = new { nativeMessageId = "synthetic-dm-event-1", actorId = peer, fromMe = false,
                            selfIdentity = new { accountIdentity = "synthetic-self", aliases = new[] { "synthetic-self" } },
                            memberDmAuthority = new { version = "native-member-dm-admission:v1", membershipLeaseRef = leaseRef } }
                    }
                }];
            }
            return new { version = group ? "inbound-observation:v2" : "inbound-observation:v1", sessionRef = session,
                effectiveConfigRevision = group ? "group-native-revision" : MemberRevision, status = "healthy",
                epochClosed = false, cursor = new NativeObservationCursor(group ? "inbound-observation:v2" : "inbound-observation:v1", group ? "group-epoch" : epoch, session, 0), snapshot = Array.Empty<object>(),
                nextCursor = new NativeObservationCursor(group ? "inbound-observation:v2" : "inbound-observation:v1", group ? "group-epoch" : epoch, session, sequence), events };
        }
        private JsonObject Dispatch(string revision) => JsonSerializer.SerializeToNode(new
        {
            version = "dispatch-inspection:v1", status = "inactive", effectiveConfigRevision = revision,
            effectiveSnapshotProof = new { status = "valid", version = "effective-runtime-config:v1", revision = "base" },
            paths = new[] { new { family = "commands", status = "inactive", rawStatus = "inactive", effectiveStatus = "inactive", effectiveConfigRevision = "base" } }
        })!.AsObject();
        private object Bootstrap(string revision, string activeEpoch, string session, bool memberDm = false) => new
        {
            subsystemOperational = true, readerAvailable = true, journalHealth = "healthy", captureState = "armed",
            activeEpoch, controlAvailable = true, configSnapshotAvailable = true, dispatchInspectorAvailable = true,
            effectiveConfigRevision = revision, effectiveSnapshotProof = new { status = "valid", version = "effective-runtime-config:v1", revision = "base" },
            isolationGate = IsolationGate(session, memberDm),
            legacyRecovery = new { status = "clear", effectiveConfigRevision = "base", uncoveredOwnerCount = 0, activeOwnerCount = 0, pendingLegacyRecoveryCount = 0, quarantinedCount = 0 }
        };
        private object IsolationGate(string session, bool memberDm) => new
        {
            gateVersion = memberDm ? "native-dm-isolation:v1" : "native-group-isolation:v1",
            gateInstanceRevision = "G1", approvedEffectiveConfigRevision = "base", accountRef = account,
            sessionRef = session, coveredPathIds = new[] { "commands" }, placementProof = "placement", lifecycleState = "armed"
        };
        private static Dictionary<string, bool> Capabilities() => NativeObservationPreflight.RequiredCapabilities.ToDictionary(name => name, _ => true);
        private SessionKey DmSession() => new("runtime", "filomena", "whatsapp", account, peer);
        private string SelfRef(string scope) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope, "self", "999999@s.whatsapp.net" })));
        private static string ActorRef(string scope, string actor) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope, "actor", actor })));
        private async Task ListenAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(stop.Token); }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }
                try
                {
                    var result = JsonSerializer.Serialize(Respond(new HttpRequestMessage(HttpMethod.Get, context.Request.Url)));
                    var bytes = Encoding.UTF8.GetBytes(result);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, stop.Token);
                }
                finally { context.Response.Close(); }
            }
        }
        private static int ReservePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel(); listener.Close();
            try { await loop; } catch (OperationCanceledException) { }
            stop.Dispose();
        }
    }
}
