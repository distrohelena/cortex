using System.Diagnostics;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Filomena.Core.Outbound;

namespace Filomena.Core.Shadow;

public sealed record ManualShadowInteraction(string Source, string RunId, string InboundId, double DurationMilliseconds,
    IReadOnlyList<SessionFlowOutcome> Outcomes, IReadOnlyList<SessionTraceEvent> Trace);

/// <summary>Host-only development ingress. No provider identity claim, transport, or alternative batching engine.</summary>
public sealed class ManualShadowRun : IAsyncDisposable
{
    private readonly ShadowSessionBinding binding;
    private readonly string actor;
    private readonly string displayName;
    private readonly string epoch = Guid.NewGuid().ToString("N");
    private readonly ShadowInboundJournal inbox;
    private readonly ShadowOutboundHost host;
    private readonly ShadowRuntimeHandoff handoff;
    private readonly SemaphoreSlim serial = new(1);
    private long ordinal;
    private bool stopped;
    public string RunId => inbox.ShadowRunId!;
    public OutboundMode OutboundMode => host.EffectiveMode;
    public IReadOnlyList<SessionFlowOutcome> Outcomes => handoff.Outcomes;
    public IReadOnlyList<SessionHistoryEntry> History => handoff.History;
    public IReadOnlyList<SessionTraceEvent> Trace => handoff.Trace;

    public ManualShadowRun(PinnedShadowProject project, ShadowSessionBinding binding, string actor, string displayName,
        string journalDirectory, OutboundBindings roles, IFlowCompletion completion, IMemoryReader memory)
    {
        this.binding = binding; this.actor = actor; this.displayName = displayName;
        if (binding.IsSelf(actor) || !binding.Accepts(Input("validation", 1, "validation", DateTimeOffset.UtcNow)))
            throw new ArgumentException("Manual actor is not the trusted peer.");
        host = new(Path.Combine(journalDirectory, "manual-shadow-outbound.jsonl"), project.Project.Document, roles,
            [binding.Session], TransportSafetyCapabilities.Unproven);
        try
        {
            host.Rearm(OutboundMode.Shadow);
            inbox = ShadowInboundJournal.StartNonResumableRun(Path.Combine(journalDirectory, "manual-shadow-inbound.jsonl"),
                binding, project.Project.ProjectRevision, epoch, 0);
            try { handoff = new(project, binding, inbox, host, completion, memory); }
            catch { inbox.Dispose(); throw; }
        }
        catch { host.Dispose(); throw; }
    }

    public async Task<ManualShadowInteraction> InjectAsync(string text, CancellationToken token, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Message required.");
        await serial.WaitAsync(token);
        try
        {
            if (stopped || host.EffectiveMode != OutboundMode.Shadow) throw new InvalidOperationException("Shadow run stopped.");
            var watch = Stopwatch.StartNew();
            var before = Outcomes.Count; var traceBefore = Trace.Count;
            var sequence = ++ordinal;
            var id = $"shadow:{RunId}:{sequence}";
            try
            {
                var decision = await handoff.AcceptAsync(Input(id, sequence, text, timestamp ?? DateTimeOffset.UtcNow), token);
                if (decision.Status != "admitted") throw new InvalidOperationException("Manual admission refused: " + decision.Diagnostic);
                // Uses real quiet/max-wait timers, not a fake time provider or a replacement flush.
                while (handoff.Snapshot is { OpenBufferCount: > 0 }) await Task.Delay(25, token);
                await handoff.DrainAsync(token);
            }
            catch { stopped = true; throw; }
            var outcomes = Outcomes.Skip(before).ToArray();
            var trace = Trace.Skip(traceBefore).ToArray();
            if (outcomes.Any(o => !o.Success) || trace.Any(t => t.EventName == "node-outbound" && t.MemoryMetadata?["result"] != "shadowed"))
                stopped = true;
            return new("manual_shadow_harness", RunId, id, watch.Elapsed.TotalMilliseconds, outcomes, trace);
        }
        finally { serial.Release(); }
    }

    private CanonicalShadowInbound Input(string id, long sequence, string text, DateTimeOffset at) =>
        new(epoch, sequence, binding.Session, SessionEntryKind.Dm, binding.ConversationNamespace, actor,
            new(id, binding.Session.ChannelId, binding.Session.AccountId, binding.Session.NativeConversationId, at,
                SessionMessageDirection.Inbound, SessionEntryKind.Dm,
                JsonSerializer.SerializeToElement(new { texto = text, remetente = new { nome = displayName }, source = "manual_shadow_harness" })));

    public async ValueTask DisposeAsync()
    {
        stopped = true;
        host.Rearm(OutboundMode.Disabled);
        try { await handoff.DisposeAsync(); }
        finally { inbox.Dispose(); host.Dispose(); serial.Dispose(); }
    }
}
