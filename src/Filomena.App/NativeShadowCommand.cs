using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Filomena.Core.Flows;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.Llm;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Filomena.Memory;
using Filomena.OpenClaw.Observation;
using Filomena.OpenClaw.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Filomena.App;

// Narrow boundary for deterministic composition tests. Production defaults below
// preserve the existing provider, Graphiti, MCP and local-model construction.
internal sealed record NativeShadowExternalFactories(
    Func<Uri, TimeSpan, (IGraphitiReadClient Client, IAsyncDisposable Lifetime)> CreateGraphiti,
    Func<OpenClawMcpClientOptions, ILoggerFactory, CancellationToken, Task<IMcpToolClient>> ConnectMcp,
    Func<AsyncLocal<string?>, ConcurrentQueue<object>, HttpClient> CreateModelHttpClient,
    Func<HttpClient, Func<string?>, IFlowCompletion> CreateCompletion)
{
    internal static NativeShadowExternalFactories Production { get; } = new(
        (endpoint, timeout) =>
        {
            var client = new GraphitiReadClient(endpoint, timeout);
            return (client, client);
        },
        async (options, loggerFactory, token) => (IMcpToolClient)await OpenClawMcpClient.ConnectAsync(options, loggerFactory, token),
        (role, calls) => new HttpClient(new ManualShadowUsage(role, calls))
        {
            BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan
        },
        (http, key) =>
        {
            var local = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
            return new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
            {
                ["openai"] = new OpenAiFlowCompletion(http, key),
                ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, local), local.Model)
            });
        });
}

// Operator-only document, separate from the frozen project. No control credential or sender.
public sealed record NativeShadowConfig(string ManualConfigFile, string ObservationEndpoint,
    string ObservationTokenEnvironmentVariable, string ObservationSessionRef,
    string NativeConversationId, string ApprovedRevision, string JournalDirectory, int MaxInteractions = 5,
    NativeReplayPlan? Recovery = null, Dictionary<string, string>? GroupParticipants = null,
    NativeSelfMentionIdentity? SelfMentionIdentity = null, string? NativeSourceGroupJid = null,
    NativeMemberDmMemoryReadinessTemplate? NewDmMemoryReadiness = null,
    NativeMemoryIngestConfig? MemoryIngest = null, ContextInterpretationHostConfig? ContextInterpretation = null);

/// <summary>Bounded, operator-stepped reader handoff. Never constructs the legacy host.</summary>
public static class NativeShadowCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken token)
        => await RunCoreAsync(args, input, output, token, null, null);

    internal static async Task<int> RunCoreAsync(string[] args, TextReader input, TextWriter output, CancellationToken token,
        NativeLiveConfig? live, NativeMemberDmExecutionContext? memberDmExecutionContext = null,
        NativeShadowExternalFactories? externalFactories = null)
    {
        externalFactories ??= NativeShadowExternalFactories.Production;
        NativeShadowConfig native;
        ManualShadowConfig config;
        PinnedShadowProject pin;
        ShadowSessionBinding binding;
        string directory, credential;
        try
        {
            var at = Array.IndexOf(args, "--native-shadow");
            if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException();
            var file = Path.GetFullPath(args[at + 1]);
            native = JsonSerializer.Deserialize<NativeShadowConfig>(await File.ReadAllTextAsync(file, token), Json) ?? throw new JsonException();
            if (live?.ManagedMemberDm == true && memberDmExecutionContext is null)
                throw new InvalidDataException("member_dm_generation_authority_unavailable");
            if (live is not null && native.MaxInteractions != live.MaxInteractions)
                throw new InvalidDataException("live_window_budget_mismatch");
            if (native.Recovery is not null)
            {
                if (live is null) throw new InvalidDataException("shadow_snapshot_execution_forbidden");
                native.Recovery.Validate(native.ObservationSessionRef,
                    live.AllowExactGroupDelivery ? "inbound-observation:v2" : "inbound-observation:v1");
            }
            var manualFile = Path.GetFullPath(native.ManualConfigFile, Path.GetDirectoryName(file)!);
            config = JsonSerializer.Deserialize<ManualShadowConfig>(await File.ReadAllTextAsync(manualFile, token), Json) ?? throw new JsonException();
            pin = PinnedShadowProject.FromBytes(await File.ReadAllBytesAsync(Path.GetFullPath(config.ProjectFile, Path.GetDirectoryName(manualFile)!), token),
                config.ProjectId, config.ProjectVersion, config.ExpectedProjectSha256);
            ManualShadowCommand.ValidateRoles(pin, config.OutboundBindings);
            live?.ValidateReplyWiring(config.OutboundBindings, managedMemberDm: live.ManagedMemberDm);
            SystemOneParticipationHost.Validate(config.SystemOneParticipation, pin);
            if ((live?.ManagedMemberDm != true && native.NewDmMemoryReadiness is not null)
                || (live?.ManagedMemberDm == true && (native.NewDmMemoryReadiness is null || config.GraphitiProbes.Length != 0)))
                throw new InvalidDataException("member_dm_memory_readiness_binding_mismatch");
            binding = live?.ManagedMemberDm == true
                ? NativeObservationHostBinding.CreateManagedMemberDm(config)
                : NativeObservationHostBinding.Create(config, native.NativeConversationId, native.GroupParticipants,
                    live is not null, live?.AllowExactGroupDelivery == true);
            ContextInterpretationHost.Validate(native.ContextInterpretation, pin, binding, live is not null);
            if (binding.Kind == Filomena.Core.Flows.Sessions.SessionEntryKind.Group)
                NativeObservationHostBinding.ValidateGroupSendWiring(pin, config.OutboundBindings);
            if (live?.ContinuousGroup == true && (binding.Kind != Filomena.Core.Flows.Sessions.SessionEntryKind.Group
                || native.Recovery is not null)) throw new InvalidDataException("invalid_continuous_group_scope");
            if (live?.ContinuousDm == true && (binding.Kind != Filomena.Core.Flows.Sessions.SessionEntryKind.Dm
                || live.AllowExactGroupDelivery || live.AllowNativeGroupParticipants || live.AllowNativeGroupReplies
                || native.Recovery is not null)) throw new InvalidDataException("invalid_continuous_dm_scope");
            if (live is not null && (live.Evidence is not { } proof || proof.ApprovedRevision != native.ApprovedRevision
                || proof.ObservationSessionRef != native.ObservationSessionRef
                || proof.RouteBindingRef != NativeObservationReader.RouteBindingRef(config.Session with { NativeConversationId = native.NativeConversationId })))
                throw new InvalidDataException("live_capability_proof_mismatch");
            if (live?.ManagedMemberDm == true && (live.Evidence is not { } memberProof
                || memberProof.BoundSendContract != "whatsapp-bound-member-dm-text:v1"
                || memberProof.NativeConversationId != native.NativeConversationId
                || memberProof.NativeAccountId != config.Session.AccountId
                || memberProof.NativeSourceGroupJid != native.NativeSourceGroupJid
                || string.IsNullOrWhiteSpace(memberProof.MembershipLeaseRef)
                || memberDmExecutionContext!.ScopeFacts.PinnedProjectRevision != pin.Project.ProjectRevision
                || memberDmExecutionContext.ScopeFacts.AccountId != config.Session.AccountId
                || memberDmExecutionContext.ScopeFacts.SourceGroupJid != native.NativeSourceGroupJid
                || memberDmExecutionContext.ScopeFacts.NativeConversationId != native.NativeConversationId
                || memberDmExecutionContext.ScopeFacts.ScopeRef != native.ObservationSessionRef
                || memberDmExecutionContext.ScopeFacts.ReaderEndpoint is not { } factsEndpoint
                || !NativeConversationSupervisor.SameReaderEndpoint(new Uri(native.ObservationEndpoint), factsEndpoint)
                || memberDmExecutionContext.SendBinding.NativeConversationId != memberProof.NativeConversationId
                || memberDmExecutionContext.SendBinding.ScopeRef != memberProof.ObservationSessionRef
                || memberDmExecutionContext.SendBinding.RouteBindingRef != memberProof.RouteBindingRef
                || memberDmExecutionContext.SendBinding.EffectiveConfigRevision != memberProof.ApprovedRevision
                || memberDmExecutionContext.SendBinding.MembershipLeaseRef != memberProof.MembershipLeaseRef
                || memberDmExecutionContext.ScopeFacts.NativeAliases.SequenceEqual(memberProof.NativeAliases ?? [], StringComparer.Ordinal) is false
                || memberDmExecutionContext.ScopeFacts.ContinueAfter != memberProof.ContinueAfter
                || config.Session.NativeConversationId != memberProof.NativeConversationId
                || config.ConversationNamespace != Filomena.Core.Identity.FilomenaIdentity.WhatsAppConversationNamespace(memberProof.NativeConversationId!)
                || config.PeerActorId != "whatsapp:" + memberProof.NativeConversationId))
                throw new InvalidDataException("managed_member_dm_authority_mismatch");
            if (live?.AllowNativeGroupParticipants == true) binding.EnableNativeGroupParticipants();
            if (native.MaxInteractions is < 1 or > 20 || string.IsNullOrWhiteSpace(native.ApprovedRevision))
                throw new InvalidDataException();
            var graphEndpoint = new Uri(config.GraphitiEndpoint);
            if (!graphEndpoint.IsLoopback || graphEndpoint.Scheme is not ("http" or "https")) throw new InvalidDataException();
            credential = Environment.GetEnvironmentVariable(native.ObservationTokenEnvironmentVariable) ?? throw new InvalidDataException();
            var configuredJournalDirectory = Path.GetFullPath(native.JournalDirectory, Path.GetDirectoryName(file)!);
            directory = binding.Kind == Filomena.Core.Flows.Sessions.SessionEntryKind.Group
                ? NativeConversationSupervisor.ResolveSessionDirectory(configuredJournalDirectory, binding, configuredJournalDirectory)
                : configuredJournalDirectory;
            var manualConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(native.ManualConfigFile, Path.GetDirectoryName(file)!))!;
            if (live?.ManagedMemberDm == true
                && Path.GetFullPath(config.JournalDirectory, manualConfigDirectory) != directory)
                throw new InvalidDataException("managed_member_dm_state_path_mismatch");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            await output.WriteLineAsync("{\"status\":\"FAIL\",\"diagnostic\":\"invalid_host_configuration\"}");
            return 2;
        }

        var evidence = Path.Combine(directory, (live is null ? "native-shadow-" : "native-live-") + Guid.NewGuid().ToString("N") + ".jsonl");
        var sendBoundaryAttempts = 0;
        await using var report = new StreamWriter(new FileStream(evidence, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough));
        async Task Emit(object value)
        {
            var line = JsonSerializer.Serialize(value, Json);
            await report.WriteLineAsync(line); await report.FlushAsync();
            await output.WriteLineAsync(line); await output.FlushAsync();
        }
        try
        {
            await using var memoryHost = native.MemoryIngest is { } memoryConfig ? new NativeMemoryHost(memoryConfig, binding, live is not null) : null;
            using var transportHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            var reader = new NativeObservationReader(transportHttp, new Uri(native.ObservationEndpoint), credential, binding,
                native.ObservationSessionRef, config.SelfActorIds[0], config.PeerActorId,
                config.Session with { NativeConversationId = native.NativeConversationId }, native.GroupParticipants, native.SelfMentionIdentity,
                live?.ManagedMemberDm == true ? memberDmExecutionContext!.ScopeFacts.NativeAliases : null);
            var approvedRevision = native.ApprovedRevision;
            NativeObservationApproval? windowApproval = null;
            NativeObservationCursor? windowCursor = null;
            async Task CheckTransport()
            {
                if (live?.ManagedMemberDm == true)
                    await memberDmExecutionContext!.EnsureCurrentGenerationAsync(token);
                var current = live?.RevalidateReceiverWithinWindow == true && windowApproval is not null && windowCursor is not null
                    ? await reader.RevalidateAsync(windowApproval, windowCursor, token)
                    : await reader.PreflightAsync(token);
                if (current.Revision != approvedRevision) throw new NativeObservationException("preflight_stale");
                if ((await reader.ProvisioningChecksAsync(false, token, current.Revision)).Any(c => !c.Passed))
                    throw new NativeObservationException("capture_or_bootstrap_not_ready");
            }
            await CheckTransport();
            await Emit(new { phase = "transport_preflight", status = "PASS", projectId = pin.Project.ProjectId,
                revision = pin.Project.ProjectRevision, sessionRef = binding.OpaqueRef,
                outboundMode = live is null ? "shadow" : "disabled_pending_preflight", transportSenderAvailable = live is not null });
            var graphitiResources = externalFactories.CreateGraphiti(new Uri(config.GraphitiEndpoint), TimeSpan.FromSeconds(30));
            await using var graphitiLifetime = graphitiResources.Lifetime;
            var graphiti = graphitiResources.Client;
            if (live?.ManagedMemberDm == true)
            {
                var proof = native.NewDmMemoryReadiness!;
                var checks = await new ShadowGraphitiProbe(graphiti).RunNewDmAsync(
                    new(config.ConversationNamespace, config.PeerActorId, proof.ForeignNamespace, proof.ForeignCanary), token);
                await Emit(new { phase = "graphiti_live_preflight", checks });
                if (checks.Any(c => !c.Passed)) throw new InvalidDataException("graphiti_preflight_failed");
            }
            else
                foreach (var probe in config.GraphitiProbes)
                {
                    var checks = await new ShadowGraphitiProbe(graphiti).RunAsync(probe, token);
                    await Emit(new { phase = "graphiti_live_preflight", checks });
                    if (checks.Any(c => !c.Passed)) throw new InvalidDataException("graphiti_preflight_failed");
                }
            await CheckTransport();
            var approval = await reader.PreflightAsync(token);
            if (approval.Revision != native.ApprovedRevision) throw new NativeObservationException("preflight_stale");
            var opened = await reader.OpenAsync(approval, token);
            var cursor = opened.Cursor;
            if (live?.RevalidateReceiverWithinWindow == true)
            {
                approval = await reader.RevalidateAsync(approval, cursor, token);
                // Initial authorization still requires the explicitly pinned revision.
                if (approval.Revision != approvedRevision) throw new NativeObservationException("preflight_stale");
            }
            windowApproval = approval;
            windowCursor = cursor;
            // Anchor only. Snapshot records are never offered to AcceptAsync or a model.
            await Emit(new { phase = "snapshot_anchor", cursor, snapshotRecords = opened.Records.Count, executions = 0 });
            var readPosition = live?.ContinuousGroup == true || live?.ContinuousDm == true
                ? new NativeLiveReadCursorStore(directory, binding.OpaqueRef, pin.Project.ProjectRevision) : null;
            if (readPosition is not null)
            {
                cursor = readPosition.Resolve(opened.Cursor, live!.ContinueAfter);
                windowCursor = cursor;
                await Emit(new { phase = "pending_inbound_resume", cursor, currentTail = opened.Cursor.Sequence,
                    pendingNativeRecords = opened.Cursor.Sequence - cursor.Sequence,
                    diagnostic = "durable_processed_position_not_whatsapp_read_receipt" });
            }
            else if (live?.ContinueAfter is { } after)
            {
                cursor = NativeLiveCommand.ValidateContinuation(after, opened.Cursor);
                windowCursor = cursor;
                await Emit(new { phase = "live_continuation_authorized", cursor,
                    diagnostic = "operator_approved_same_epoch_cursor_durable_dedupe_required" });
            }
            if (native.Recovery is { } recovery)
            {
                cursor = recovery.After;
                await Emit(new { phase = "native_recovery_authorized", cursor, recovery.ThroughSequence,
                    diagnostic = "original_native_events_only_current_authorization_required" });
            }
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("model_credential_unavailable");
            var calls = new System.Collections.Concurrent.ConcurrentQueue<object>();
            var role = new AsyncLocal<string?>();
            using var http = externalFactories.CreateModelHttpClient(role, calls);
            var providers = externalFactories.CreateCompletion(http, () => key);
            var routed = SystemOneParticipationHost.Create(config.SystemOneParticipation, pin, providers, http,
                Environment.GetEnvironmentVariable, (node, result) => calls.Enqueue(new { kind = "systemone", node, provider = "jev",
                    result.Model, result.InputTokens, result.OutputTokens, result.ProbabilityPolicy, milliseconds = result.Elapsed.TotalMilliseconds }));
            var completion = new ManualShadowCommand.RecordedCompletion(routed, role, calls);
            await using var mcp = live is null ? null : await externalFactories.ConnectMcp(live.Mcp, NullLoggerFactory.Instance, token);
            var sender = mcp is null ? null : new OpenClawBoundMcpTransport(mcp, live?.AllowExactGroupDelivery == true);
            var liveRoute = live?.BindRoute(binding);
            if (live is not null)
            {
                var route = await sender!.DescribeRouteAsync(live.TransportRoute, token);
                if (route.Channel != binding.Session.ChannelId || route.AccountId != binding.Session.AccountId
                    || route.Recipient != liveRoute!.Recipient) throw new InvalidDataException("live_route_mismatch");
                await CheckTransport();
            }
            await using var shadowRun = live is null ? new ObservedShadowRun(pin, binding, directory, config.OutboundBindings, completion,
                new GraphitiMemoryReader(graphiti), cursor.Epoch, cursor.Sequence) : null;
            if (shadowRun is not null) ContextInterpretationHost.Configure(native.ContextInterpretation, pin, binding, shadowRun, completion);
            await using var liveRun = live is null ? null : new ObservedLiveRun(pin, binding, directory, config.OutboundBindings, completion,
                new GraphitiMemoryReader(graphiti), cursor.Epoch, cursor.Sequence,
                liveRoute!,
                sender!, live.Capabilities, async cancellation =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    await CheckTransport();
                    Interlocked.Increment(ref sendBoundaryAttempts);
                }, live.MaxInteractions, live.ContinuousGroup, live.ContinuousDm,
                live.ManagedMemberDm ? new Filomena.Core.Outbound.MemberDmBoundTextBinding(
                    live.Evidence!.NativeConversationId!, live.Evidence.ObservationSessionRef,
                    live.Evidence.RouteBindingRef, live.Evidence.ApprovedRevision, live.Evidence.MembershipLeaseRef!) : null,
                participation: ContextInterpretationHost.CreateLiveOptions(native.ContextInterpretation, pin, binding, completion));
            var runId = shadowRun?.RunId ?? liveRun!.RunId;
            readPosition?.Save(cursor);
            var historyBeforeRecovery = liveRun?.History.ToArray();
            await Emit(new { phase = "run_started", runId,
                source = binding.Kind == Filomena.Core.Flows.Sessions.SessionEntryKind.Group ? "native_observation_v2" : "native_observation_v1", cursor, evidence,
                restoredHistoryCount = liveRun?.History.Count ?? 0,
                maxInteractions = live?.ContinuousGroup == true || live?.ContinuousDm == true ? (int?)null : native.MaxInteractions,
                continuousGroup = live?.ContinuousGroup == true,
                continuousDm = live?.ContinuousDm == true,
                instruction = "Send one message in the approved conversation, then /poll. /quit closes. No automatic polling." });
            var admitted = 0;
            var prompts = pin.Project.Document.GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray())
                .Where(n => n.GetProperty("kind").GetString() == "prompt").Select(n => n.GetProperty("id").GetString()!).ToHashSet();
            while (live?.ContinuousGroup == true || live?.ContinuousDm == true || admitted < native.MaxInteractions)
            {
                var command = await input.ReadLineAsync(token);
                if (command is null || command == "/quit") break;
                if (command != "/poll") { await Emit(new { diagnostic = "only_poll_or_quit_allowed" }); continue; }
                NativeObservationBatch batch;
                var memberDmEventLeases = new Dictionary<long, string?>();
                try
                {
                if (live?.RevalidateReceiverWithinWindow == true)
                {
                    // Only at the idle boundary. Never retry an execution/send, reopen
                    // the snapshot, rearm capture, or reset the run/budget on reconnect.
                    var renewed = await reader.TryRevalidateForPollingAsync(approval, windowCursor!, token)
                        ?? throw new NativeObservationException("dispatch_not_ready");
                    if ((await reader.ProvisioningChecksAsync(false, token, renewed.Revision)).Any(c => !c.Passed))
                        throw new NativeObservationException("capture_or_bootstrap_not_ready");
                    var route = await sender!.DescribeRouteAsync(live.TransportRoute, token);
                    if (route.Channel != binding.Session.ChannelId || route.AccountId != binding.Session.AccountId
                        || route.Recipient != liveRoute!.Recipient) throw new InvalidDataException("live_route_mismatch");
                    if (renewed.Revision != approval.Revision)
                        await Emit(new { phase = "receiver_revalidated", runId,
                            previousRevisionRef = ShadowSessionBinding.Ref(approval.Revision),
                            revisionRef = ShadowSessionBinding.Ref(renewed.Revision), cursor,
                            admitted, sendBoundaryAttempts, maxInteractions = native.MaxInteractions,
                            diagnostic = "same_window_new_receiver_no_replay" });
                    approval = renewed;
                    windowApproval = renewed;
                    approvedRevision = renewed.Revision;
                }
                await CheckTransport();
                batch = await reader.PollAsync(approval, cursor, token);
                if (live?.ManagedMemberDm == true)
                {
                    // Recheck current scope immediately after poll, before resolving or
                    // enriching any event that may have crossed a lease boundary.
                    await CheckTransport();
                    foreach (var observed in batch.Records)
                    {
                        var eventLease = await memberDmExecutionContext!.ResolveEventLeaseAsync(observed, token);
                        memberDmEventLeases.Add(observed.Sequence, eventLease);
                    }
                }
                }
                catch (NativeObservationException e) when ((live?.ContinuousGroup == true || live?.ContinuousDm == true)
                    && NativeObservationReader.IsPollingUnavailable(e))
                {
                    await Emit(new { phase = "transport_wait", runId, diagnostic = e.Code,
                        cursor, executions = 0, sends = 0 });
                    continue;
                }
                if (native.Recovery is { } replay) batch = replay.Next(batch, cursor);
                else if (batch.EpochClosed) throw new NativeObservationException("capture_epoch_closed");
                var mapped = batch.Records.Select(observed =>
                {
                    var inbound = reader.ToInbound(observed);
                    return live?.ManagedMemberDm == true
                        ? NativeConversationSupervisor.SuppressUnlessCapturedMemberDmEventGeneration(observed,
                            inbound, live.Evidence!.ObservationSessionRef, memberDmEventLeases[observed.Sequence],
                            live.Evidence.MembershipLeaseRef!)
                        : inbound;
                }).ToArray();
                // Persist memory intent before enrichment/participation and before advancing the read cursor.
                // Snapshot context is never passed through this seam. Retries cannot re-execute the brain.
                if (memoryHost is not null)
                {
                    await CheckTransport();
                    memoryHost.StageBatch(mapped);
                }
                var participants = new GraphitiParticipantReader(graphiti);
                for (var index = 0; index < mapped.Length; index++)
                    mapped[index] = await NativeInboundContext.EnrichAsync(mapped[index], binding, participants, token);
                if (native.Recovery is not null && historyBeforeRecovery is { Length: > 0 })
                    foreach (var item in mapped.Where(x => !x.FromMe && !binding.IsSelf(x.ActorId)))
                        if (!historyBeforeRecovery.Any(h => h.ProviderMessageId == item.Message.ProviderMessageId)
                            && item.Message.Timestamp < historyBeforeRecovery.Max(h => h.Timestamp))
                            throw new InvalidDataException("native_recovery_requires_chronology_reconciliation");
                if (live?.ContinuousGroup != true && live?.ContinuousDm != true
                    && mapped.Count(x => !x.FromMe && !x.SkipNonText && !binding.IsSelf(x.ActorId)) + admitted > native.MaxInteractions)
                    throw new InvalidDataException("interaction_window_limit");
                await CheckTransport();
                var result = shadowRun is not null ? await shadowRun.AcceptAsync(mapped, token) : await liveRun!.AcceptAsync(mapped, token);
                if (memoryHost is not null)
                {
                    memoryHost.ScheduleReconciliation();
                    await Emit(new { phase = "memory_ingestion", pending = memoryHost.PendingCount,
                        diagnostic = memoryHost.PendingCount == 0 ? "no_pending_ingestion" : "ingestion_not_yet_confirmed" });
                }
                var runtimeFailed = result.Outcomes.Any(o => !o.Success)
                    || result.Decisions.Any(d => d.Status is not ("admitted" or "self" or "duplicate" or "backlog" or "ignored"))
                    || result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] != (live is null ? "shadowed" : "sent"));
                if (!runtimeFailed && batch.Cursor != cursor) readPosition?.Save(batch.Cursor);
                admitted += result.Decisions.Count(d => d.Status == "admitted");
                cursor = batch.Cursor;
                if (native.Recovery is null) windowCursor = cursor;
                var usage = new List<object>(); while (calls.TryDequeue(out var call)) usage.Add(call);
                await Emit(new { phase = "interaction", result.RunId, result.DurationMilliseconds, decisions = result.Decisions,
                    nativeEventRefs = batch.Records.Select(r => new { idRef = ShadowSessionBinding.Ref(r.NativeMessageId), r.Sequence, r.FromMe }), cursor,
                    outcomes = result.Outcomes.Select(o => new { o.ExecutionId, o.BatchId, o.StateId, o.Success, o.Error, o.TransitionApplied,
                        purpose = o.ReassessmentOfMessageId is null ? "inbound" : "contextual_reassessment",
                        reassessmentTargetRef = o.ReassessmentOfMessageId is null ? null : ShadowSessionBinding.Ref(o.ReassessmentOfMessageId),
                        outputs = o.Outputs.Where(p => prompts.Contains(p.Key)).ToDictionary() }),
                    trace = result.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.StateId, t.CurrentSessionStateId, t.DurationMilliseconds, t.ErrorCode, t.MemoryMetadata }),
                    initialInterpretations = result.InitialInterpretations.Select(r => ContextInterpretationHost.Report(r, prompts)),
                    participationReviews = result.ParticipationReviews.Select(r => ContextInterpretationHost.Report(r, prompts)),
                    outcome = result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] == "sent") ? "SENT"
                        : result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] == "shadowed") ? "WOULD_SEND" : "NO_CONFIRMED_SEND",
                    calls = usage, sendBoundaryAttempts });
                if (runtimeFailed)
                    throw new InvalidDataException("runtime_execution_failed");
                if (native.Recovery is { } finished && cursor.Sequence == finished.ThroughSequence) break;
            }
            await Emit(new { phase = "run_closed", runId, executions = shadowRun?.Outcomes.Count ?? liveRun!.Outcomes.Count,
                sendBoundaryAttempts, outboundMode = "disabled_on_close" });
            return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await Emit(new { status = "ABORT", diagnostic = e is NativeObservationException n ? n.Code
                : e is OperationCanceledException ? "cancelled" : "preflight_or_runtime_failed", sendBoundaryAttempts,
                exceptionType = e.GetType().Name, exceptionSite = e.StackTrace,
                reconciliationRequired = sendBoundaryAttempts > 0 });
            return 2;
        }
    }
}
