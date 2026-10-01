using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Cortex.Inference;
using Cortex.Inference.LmStudio;
using Cortex.Inference.OpenAi;
using Cortex.Memory;

namespace Filomena.App;

public sealed record ManualShadowConfig(string ProjectFile, string ProjectId, long ProjectVersion, string ExpectedProjectSha256,
    SessionKey Session, string ConversationNamespace, string[] SelfActorIds, string PeerActorId, string PeerDisplayName,
    OutboundBindings OutboundBindings, string JournalDirectory, string GraphitiEndpoint, ShadowGraphitiProbeRequest[] GraphitiProbes,
    SystemOneParticipationConfig? SystemOneParticipation = null);

/// <summary>Explicit local homologation entrypoint; never builds the legacy application host.</summary>
public static class ManualShadowCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken token)
    {
        ManualShadowConfig config;
        PinnedShadowProject pin;
        ShadowSessionBinding binding;
        string directory;
        try
        {
            var at = Array.IndexOf(args, "--manual-shadow");
            if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException();
            var file = Path.GetFullPath(args[at + 1]);
            var root = Path.GetDirectoryName(file)!;
            config = JsonSerializer.Deserialize<ManualShadowConfig>(await File.ReadAllTextAsync(file, token), Json) ?? throw new JsonException();
            pin = PinnedShadowProject.FromBytes(await File.ReadAllBytesAsync(Path.GetFullPath(config.ProjectFile, root), token),
                config.ProjectId, config.ProjectVersion, config.ExpectedProjectSha256);
            var groupParticipantsAt = Array.IndexOf(args, "--group-participants");
            if (config.Session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal))
            {
                // This entry only proves memory readiness. Group execution still requires
                // the native command's isolation, revision, epoch and cursor checks.
                if (!args.Contains("--preflight-only", StringComparer.Ordinal)
                    || groupParticipantsAt < 0 || groupParticipantsAt + 1 >= args.Length
                    || args[groupParticipantsAt + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new InvalidDataException();
                var participants = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(Path.GetFullPath(args[groupParticipantsAt + 1], root), token), Json)
                    ?? throw new JsonException();
                binding = NativeObservationHostBinding.Create(config, config.Session.NativeConversationId, participants, live: false);
                NativeObservationHostBinding.ValidateGroupSendWiring(pin, config.OutboundBindings);
            }
            else
            {
                if (groupParticipantsAt >= 0) throw new InvalidDataException();
                binding = new(config.Session, config.ConversationNamespace, config.SelfActorIds, [config.PeerActorId]);
                // Current pilot uses the existing native memory namespace derivation, not an arbitrary label.
                if (config.Session.ChannelId != "whatsapp"
                || config.Session.NativeConversationId != FilomenaIdentity.NormalizeWhatsAppActorNativeId(config.Session.NativeConversationId)
                || config.ConversationNamespace != FilomenaIdentity.WhatsAppConversationNamespace(config.Session.NativeConversationId)
                || config.PeerActorId != "whatsapp:" + config.Session.NativeConversationId
                || config.GraphitiProbes.Length == 0 || config.GraphitiProbes.Any(p => p.ConversationNamespace != config.ConversationNamespace || p.ParticipantStableId != config.PeerActorId))
                    throw new InvalidDataException();
            }
            ValidateRoles(pin, config.OutboundBindings);
            SystemOneParticipationHost.Validate(config.SystemOneParticipation, pin);
            directory = Path.GetFullPath(config.JournalDirectory, root);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
            var endpoint = new Uri(config.GraphitiEndpoint);
            if (!endpoint.IsLoopback || endpoint.Scheme is not ("http" or "https")) throw new InvalidDataException();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            await output.WriteLineAsync("{\"status\":\"FAIL\",\"diagnostic\":\"invalid_host_configuration\"}");
            return 2;
        }

        var evidence = Path.Combine(directory, "manual-shadow-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await using var report = new StreamWriter(new FileStream(evidence, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough));
        async Task Emit(object value)
        {
            var line = JsonSerializer.Serialize(value, Json);
            await report.WriteLineAsync(line); await report.FlushAsync(token);
            await output.WriteLineAsync(line); await output.FlushAsync(token);
        }
        await Emit(new { phase = "static", status = "PASS", projectId = pin.Project.ProjectId, revision = pin.Project.ProjectRevision,
            sessionRef = binding.OpaqueRef, source = "manual_shadow_harness", outboundMode = "shadow", transportAvailable = false, evidence });
        await using var graphiti = new GraphitiReadClient(new Uri(config.GraphitiEndpoint), TimeSpan.FromSeconds(30));
        foreach (var probe in config.GraphitiProbes)
        {
            var checks = await new ShadowGraphitiProbe(graphiti).RunAsync(probe, token);
            await Emit(new { phase = "graphiti_live_preflight", checks });
            if (checks.Any(c => !c.Passed)) { await Emit(new { status = "FAIL", diagnostic = "graphiti_preflight_failed", executions = 0 }); return 2; }
        }
        if (args.Contains("--preflight-only", StringComparer.Ordinal)) { await Emit(new { status = "PREFLIGHT_PASS", executions = 0 }); return 0; }

        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) { await Emit(new { status = "FAIL", diagnostic = "model_credential_unavailable", executions = 0 }); return 2; }
        var calls = new System.Collections.Concurrent.ConcurrentQueue<object>();
        var role = new AsyncLocal<string?>();
        using var meter = new ManualShadowUsage(role, calls);
        using var http = new HttpClient(meter) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        // Same settings as the approved real-runtime H runs; model choice still comes from each Instruction.
        var local = new LmStudioOptions { GenerationTemperature = 0.3, GenerationMaxTokens = 8192, RequestTimeoutSeconds = Timeout.Infinite };
        var providers = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openai"] = new OpenAiFlowCompletion(http, () => key),
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, local), local.Model)
        });
        var routed = SystemOneParticipationHost.Create(config.SystemOneParticipation, pin, providers, http,
            Environment.GetEnvironmentVariable, (node, result) => calls.Enqueue(new { kind = "systemone", node, provider = "jev",
                result.Model, result.InputTokens, result.OutputTokens, result.ProbabilityPolicy, milliseconds = result.Elapsed.TotalMilliseconds }));
        var completion = new RecordedCompletion(routed, role, calls);
        await using var run = new ManualShadowRun(pin, binding, config.PeerActorId, config.PeerDisplayName,
            directory, config.OutboundBindings, completion, new GraphitiMemoryReader(graphiti));
        await Emit(new { phase = "run_started", runId = run.RunId, state = pin.Project.Document.GetProperty("initialStateId").GetString() });
        var promptIds = pin.Project.Document.GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray())
            .Where(n => n.GetProperty("kind").GetString() == "prompt").Select(n => n.GetProperty("id").GetString()!).ToHashSet();
        while (true)
        {
            await output.WriteLineAsync("> mensagem (/quit encerra; aguarda batching real):"); await output.FlushAsync(token);
            var line = await input.ReadLineAsync(token);
            if (line is null || line == "/quit") break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var result = await run.InjectAsync(line, deadline.Token);
                var trace = result.Trace.Select(t => new { t.EventName, t.NodeId, t.Exit, t.StateId, t.CurrentSessionStateId, t.DurationMilliseconds, t.ErrorCode, t.MemoryMetadata });
                var outcomes = result.Outcomes.Select(o => new { o.ExecutionId, o.BatchId, o.StateId, o.Success, o.Error,
                    o.TransitionApplied, o.TransitionTargetStateId, outputs = o.Outputs.Where(p => promptIds.Contains(p.Key)).ToDictionary() });
                var records = new List<object>(); while (calls.TryDequeue(out var call)) records.Add(call);
                var sent = result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] == "shadowed");
                await Emit(new { phase = "interaction", result.Source, result.RunId, result.InboundId, input = line,
                    result.DurationMilliseconds, outcome = sent ? "WOULD_SEND" : result.Outcomes.Any(o => !o.Success) ? "ABORT" : "NO_SEND",
                    outcomes, trace, calls = records, outboundCalls = 0 });
                if (result.Outcomes.Any(o => !o.Success) || result.Trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] != "shadowed")) return 2;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                var records = new List<object>(); while (calls.TryDequeue(out var call)) records.Add(call);
                await Emit(new { phase = "interaction", status = "ABORT", diagnostic = e is OperationCanceledException ? "timeout_or_cancelled" : "runtime_failure", calls = records });
                return 2;
            }
        }
        await Emit(new { phase = "run_closed", runId = run.RunId, executions = run.Outcomes.Count, outboundCalls = 0 });
        return 0;
    }

    internal static void ValidateRoles(PinnedShadowProject pin, OutboundBindings roles)
    {
        var nodes = pin.Project.Document.GetProperty("states").EnumerateArray().SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray()).ToArray();
        bool Has(string id, string kind) => nodes.Any(n => n.GetProperty("id").GetString() == id && n.GetProperty("kind").GetString() == kind);
        if (!nodes.Any(n => n.GetProperty("kind").GetString() == "send") || roles.RecallNodes.Count == 0 || roles.CompilerNodes.Count == 0
            || roles.CandidatePorts.Count == 0 || roles.EnforcementNodes.Count == 0
            || !roles.RecallNodes.All(n => Has(n, "memory")) || !roles.CompilerNodes.All(n => Has(n, "interpretBoundaries"))
            || !roles.CandidatePorts.Keys.All(n => Has(n, "prompt")) || !roles.EnforcementNodes.All(n => Has(n, "prompt"))) throw new InvalidDataException();
        foreach (var node in nodes.Where(n => n.GetProperty("kind").GetString() == "prompt"))
        {
            if (node.TryGetProperty("provider", out var provider) && provider.GetString() == "openai"
                && (node.GetProperty("model").GetString() is not ("gpt-5.6-luna" or "gpt-6-luna")
                    || node.GetProperty("reasoningEffort").GetString() != "medium"))
                throw new InvalidDataException("This pilot permits only Luna medium remotely.");
        }
    }

    internal sealed class RecordedCompletion(IFlowCompletion inner, AsyncLocal<string?> role, System.Collections.Concurrent.ConcurrentQueue<object> calls) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            role.Value = request.NodeId;
            var watch = Stopwatch.StartNew();
            try
            {
                var result = await inner.CompleteAsync(request, token);
                calls.Enqueue(new { kind = "completion", request.NodeId, request.Provider, request.Model, request.ReasoningEffort, request.Correction,
                    response = result, milliseconds = watch.Elapsed.TotalMilliseconds });
                return result;
            }
            finally { role.Value = null; }
        }
    }
}

internal sealed class ManualShadowUsage(AsyncLocal<string?> role, System.Collections.Concurrent.ConcurrentQueue<object> calls)
    : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var remote = request.RequestUri?.Host == "api.openai.com";
        try
        {
            var response = await base.SendAsync(request, token);
            if (remote)
            {
                JsonElement? usage = null;
                try { using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); if (body.RootElement.TryGetProperty("usage", out var value)) usage = value.Clone(); }
                catch (JsonException) { }
                calls.Enqueue(new { kind = "usage", node = role.Value, status = (int)response.StatusCode, usage });
            }
            return response;
        }
        catch { if (remote) calls.Enqueue(new { kind = "usage", node = role.Value, status = "transport_failure", usage = (object?)null }); throw; }
    }
}
