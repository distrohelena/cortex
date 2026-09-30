using System.Text.Json;
using Filomena.Core.Conversations;
using Filomena.Core.Identity;

namespace Filomena.App.FlowRuntime;

public sealed record DmFlowObservation(string Outcome, string? OutboundMessageId = null);

// Process-local admission and activation; no persistent session state or memory.
public sealed class DmFlowLiveSession : IDisposable
{
    private readonly object gate = new();
    private readonly DmFlowExecutor executor;
    private readonly SemaphoreSlim executionSlots = new(4);
    private readonly HashSet<(string Channel, string Account, string Conversation, string Message)> observed = [];
    private readonly HashSet<CancellationTokenSource> runs = [];
    private CompiledDmFlow? active;
    private const string AdmissionKey = "filomena_flow_admission";
    private long activationVersion;
    private bool disposed;

    public DmFlowLiveSession(DmFlowLiveOptions options, DmFlowExecutor executor)
    {
        options.Validate();
        Channel = options.Channel;AccountId = options.AccountId;NativeId = options.AllowedDmNativeId;
        this.executor = executor;
    }
    public string Channel { get; }
    public string AccountId { get; }
    public string NativeId { get; }
    public bool Active { get { lock (gate) return active is not null; } }

    public void Activate(JsonElement document)
    {
        var compiled = DmFlowCompiler.Compile(document);
        CancellationTokenSource[] previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previous = runs.ToArray();active = compiled;activationVersion++;
        }
        Cancel(previous);
    }

    public void Deactivate()
    {
        CancellationTokenSource[] previous;
        lock (gate) { active = null;previous = runs.ToArray(); }
        Cancel(previous);
    }

    public Task<DmFlowObservation> ObserveAsync(InboundConversationEvent message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Accepts(message)) return Task.FromResult(new DmFlowObservation("ignored"));
        CompiledDmFlow snapshot;
        CancellationTokenSource run;
        lock (gate)
        {
            if (disposed || active is null) return Task.FromResult(new DmFlowObservation("inactive"));
            if (message.StructuralMetadata.TryGetValue(AdmissionKey, out var admission)
                && (admission is not long version || version != activationVersion))
                return Task.FromResult(new DmFlowObservation("inactive"));
            var key = (Channel, AccountId, NativeId, message.ProviderMessageId);
            if (observed.Contains(key)) return Task.FromResult(new DmFlowObservation("duplicate"));
            // Fail closed instead of evicting IDs and making old messages executable again.
            if (observed.Count >= 10000 || runs.Count >= 64) return Task.FromResult(new DmFlowObservation("capacity_reached"));
            observed.Add(key);snapshot = active;
            run = CancellationTokenSource.CreateLinkedTokenSource(token);runs.Add(run);
        }
        return ExecuteAsync(snapshot, message, run);
    }

    // Called at bridge admission, before queueing. This is an in-process activation
    // version, not a timestamp, session identity, or persisted provider metadata.
    public InboundConversationEvent PrepareInbound(InboundConversationEvent message)
    {
        lock (gate)
        {
            var metadata = new Dictionary<string, object?>(message.StructuralMetadata)
            {
                [AdmissionKey] = !disposed && active is not null ? activationVersion : null
            };
            return message with { StructuralMetadata = metadata };
        }
    }

    private bool Accepts(InboundConversationEvent message)
    {
        if (message.Conversation is not { Kind: ConversationKind.DirectMessage } conversation || message.Sender is not { } actor
            || conversation.Id.Channel != Channel || conversation.Id.AccountId != AccountId || conversation.Id.NativeId != NativeId
            || actor.Channel != Channel || actor.AccountId != AccountId
            || string.IsNullOrWhiteSpace(message.ProviderMessageId) || string.IsNullOrWhiteSpace(message.SessionKey)
            || message.SessionKey != conversation.SessionKey || message.Timestamp is null
            || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 16000) return false;
        try
        {
            return FilomenaIdentity.NormalizeWhatsAppActorNativeId(actor.NativeId) == NativeId
                && actor.StableId == FilomenaIdentity.WhatsAppActorStableId(NativeId)
                && conversation.Id.Namespace == FilomenaIdentity.WhatsAppConversationNamespace(NativeId);
        }
        catch (ArgumentException) { return false; }
    }

    private async Task<DmFlowObservation> ExecuteAsync(CompiledDmFlow snapshot, InboundConversationEvent message, CancellationTokenSource run)
    {
        var entered = false;
        try
        {
            await executionSlots.WaitAsync(run.Token);entered = true;
            var result = await executor.ExecuteAsync(snapshot, message, send: true, run.Token);
            return new("sent", result.OutboundMessageId);
        }
        catch (OperationCanceledException) { return new("cancelled"); }
        catch (Exception error) { return new("failed:" + error.GetType().Name); }
        finally
        {
            if (entered) executionSlots.Release();
            lock (gate) runs.Remove(run);
            run.Dispose();
        }
    }

    private static void Cancel(IEnumerable<CancellationTokenSource> sources)
    {
        foreach (var source in sources)
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { } // Completion raced with activation/deactivation.
        }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return;disposed = true; }
        Deactivate();
        // Outstanding cancelled runs must still be able to release slots (no OS handle allocated).
    }
}
