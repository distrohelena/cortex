using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.Core.Flows;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;
using Filomena.Llm;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Filomena.Memory;
using Filomena.OpenClaw.Observation;

namespace Filomena.App;

// Operator-only document, separate from the frozen project. No control credential or sender.
public sealed record NativeShadowConfig(string ManualConfigFile, string ObservationEndpoint,
    string ObservationTokenEnvironmentVariable, string ObservationSessionRef,
    string NativeConversationId, string ApprovedRevision, string JournalDirectory, int MaxInteractions = 5);

/// <summary>Bounded, operator-stepped reader handoff. Never constructs the legacy host.</summary>
public static class NativeShadowCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken token)
    {
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
            var manualFile = Path.GetFullPath(native.ManualConfigFile, Path.GetDirectoryName(file)!);
            config = JsonSerializer.Deserialize<ManualShadowConfig>(await File.ReadAllTextAsync(manualFile, token), Json) ?? throw new JsonException();
            pin = PinnedShadowProject.FromBytes(await File.ReadAllBytesAsync(Path.GetFullPath(config.ProjectFile, Path.GetDirectoryName(manualFile)!), token),
                config.ProjectId, config.ProjectVersion, config.ExpectedProjectSha256);
            ManualShadowCommand.ValidateRoles(pin, config.OutboundBindings);
            binding = new(config.Session, config.ConversationNamespace, config.SelfActorIds, [config.PeerActorId]);
            if (config.Session.ChannelId != "whatsapp" || config.SelfActorIds.Length != 1
                || config.Session.NativeConversationId != FilomenaIdentity.NormalizeWhatsAppActorNativeId(config.Session.NativeConversationId)
                || config.Session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal)
                || config.ConversationNamespace != FilomenaIdentity.WhatsAppConversationNamespace(config.Session.NativeConversationId)
                || config.PeerActorId != "whatsapp:" + config.Session.NativeConversationId
                || !System.Text.RegularExpressions.Regex.IsMatch(native.NativeConversationId, @"^\d+@(lid|s\.whatsapp\.net)$")
                || native.MaxInteractions is < 1 or > 20 || string.IsNullOrWhiteSpace(native.ApprovedRevision)
                || config.GraphitiProbes.Length != 2
                || config.GraphitiProbes.Any(p => p.ConversationNamespace != config.ConversationNamespace || p.ParticipantStableId != config.PeerActorId))
                throw new InvalidDataException();
            var graphEndpoint = new Uri(config.GraphitiEndpoint);
            if (!graphEndpoint.IsLoopback || graphEndpoint.Scheme is not ("http" or "https")) throw new InvalidDataException();
            credential = Environment.GetEnvironmentVariable(native.ObservationTokenEnvironmentVariable) ?? throw new InvalidDataException();
            directory = Path.GetFullPath(native.JournalDirectory, Path.GetDirectoryName(file)!);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            await output.WriteLineAsync("{\"status\":\"FAIL\",\"diagnostic\":\"invalid_host_configuration\"}");
            return 2;
        }

        var evidence = Path.Combine(directory, "native-shadow-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await using var report = new StreamWriter(new FileStream(evidence, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough));
        async Task Emit(object value)
        {
            var line = JsonSerializer.Serialize(value, Json);
            await report.WriteLineAsync(line); await report.FlushAsync();
            await output.WriteLineAsync(line); await output.FlushAsync();
        }
        try
        {
            using var transportHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            var reader = new NativeObservationReader(transportHttp, new Uri(native.ObservationEndpoint), credential, binding,
                native.ObservationSessionRef, config.SelfActorIds[0], config.PeerActorId,
                config.Session with { NativeConversationId = native.NativeConversationId });
            async Task CheckTransport()
            {
                var current = await reader.PreflightAsync(token);
                if (current.Revision != native.ApprovedRevision) throw new NativeObservationException("preflight_stale");
                if ((await reader.ProvisioningChecksAsync(false, token, current.Revision)).Any(c => !c.Passed))
                    throw new NativeObservationException("capture_or_bootstrap_not_ready");
            }
            await CheckTransport();
            await Emit(new { phase = "transport_preflight", status = "PASS", projectId = pin.Project.ProjectId,
                revision = pin.Project.ProjectRevision, sessionRef = binding.OpaqueRef, outboundMode = "shadow", transportSenderAvailable = false });
            await using var graphiti = new GraphitiReadClient(new Uri(config.GraphitiEndpoint), TimeSpan.FromSeconds(30));
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
            // Anchor only. Snapshot records are never offered to AcceptAsync or a model.
            await Emit(new { phase = "snapshot_anchor", cursor, snapshotRecords = opened.Records.Count, executions = 0 });
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("model_credential_unavailable");
            var calls = new System.Collections.Concurrent.ConcurrentQueue<object>();
            var role = new AsyncLocal<string?>();
            using var meter = new ManualShadowUsage(role, calls);
            using var http = new HttpClient(meter) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
            var local = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
            var completion = new ManualShadowCommand.RecordedCompletion(new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
            {
                ["openai"] = new OpenAiFlowCompletion(http, () => key),
                ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, local), local.Model)
            }), role, calls);
            await using var run = new ObservedShadowRun(pin, binding, directory, config.OutboundBindings, completion,
                new GraphitiMemoryReader(graphiti), cursor.Epoch, cursor.Sequence);
            await Emit(new { phase = "run_started", runId = run.RunId, source = "native_observation_v1", cursor, evidence,
                maxInteractions = native.MaxInteractions, instruction = "Send one real DM, then /poll. /quit closes. No automatic polling." });
            var admitted = 0;
            var prompts = pin.Project.Document.GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray())
                .Where(n => n.GetProperty("kind").GetString() == "prompt").Select(n => n.GetProperty("id").GetString()!).ToHashSet();
            while (admitted < native.MaxInteractions)
            {
                var command = await input.ReadLineAsync(token);
                if (command is null || command == "/quit") break;
                if (command != "/poll") { await Emit(new { diagnostic = "only_poll_or_quit_allowed" }); continue; }
                await CheckTransport();
                var batch = await reader.PollAsync(approval, cursor, token);
                if (batch.EpochClosed) throw new NativeObservationException("capture_epoch_closed");
                var mapped = batch.Records.Select(reader.ToInbound).ToArray();
                if (mapped.Count(x => !x.FromMe && !binding.IsSelf(x.ActorId)) + admitted > native.MaxInteractions)
                    throw new InvalidDataException("interaction_window_limit");
                await CheckTransport();
                var result = await run.AcceptAsync(mapped, token);
                admitted += result.Decisions.Count(d => d.Status == "admitted");
                cursor = batch.Cursor;
                var usage = new List<object>(); while (calls.TryDequeue(out var call)) usage.Add(call);
                await Emit(new { phase = "interaction", result.RunId, result.DurationMilliseconds, decisions = result.Decisions,
                    nativeEventRefs = batch.Records.Select(r => new { idRef = ShadowSessionBinding.Ref(r.NativeMessageId), r.Sequence, r.FromMe }), cursor,
                    outcomes = result.Outcomes.Select(o => new { o.ExecutionId, o.BatchId, o.StateId, o.Success, o.Error, o.TransitionApplied,
                        outputs = o.Outputs.Where(p => prompts.Contains(p.Key)).ToDictionary() }),
                    trace = result.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.StateId, t.CurrentSessionStateId, t.DurationMilliseconds, t.ErrorCode, t.MemoryMetadata }),
                    outcome = result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] == "shadowed") ? "WOULD_SEND" : "NO_SEND",
                    calls = usage, outboundCalls = 0 });
                if (result.Outcomes.Any(o => !o.Success)
                    || result.Decisions.Any(d => d.Status is not ("admitted" or "self" or "duplicate" or "backlog"))
                    || result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] != "shadowed"))
                    throw new InvalidDataException("runtime_execution_failed");
            }
            await Emit(new { phase = "run_closed", runId = run.RunId, executions = run.Outcomes.Count, outboundCalls = 0 });
            return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await Emit(new { status = "ABORT", diagnostic = e is NativeObservationException n ? n.Code
                : e is OperationCanceledException ? "cancelled" : "preflight_or_runtime_failed", outboundCalls = 0 });
            return 2;
        }
    }
}
