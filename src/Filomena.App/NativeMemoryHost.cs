using Cortex.Core.Shadow;

namespace Filomena.App;

public sealed class NativeMemoryHost : IAsyncDisposable
{
    private readonly NativeMemoryIngestClient client;
    private readonly NativeMemoryOutbox outbox;
    private DateTimeOffset nextReconciliation;
    private readonly CancellationTokenSource lifetime = new();
    private Task? maintenance;
    public int PendingCount => outbox.Pending.Count;
    public static void ValidateAuthority(NativeMemoryIngestConfig config, ShadowSessionBinding binding, bool operationalLive)
    {
        if (!operationalLive || config.Session != binding.Session || config.ConversationNamespace != binding.ConversationNamespace)
            throw new InvalidDataException("native_memory_writer_authority_unavailable");
    }
    public NativeMemoryHost(NativeMemoryIngestConfig config, ShadowSessionBinding binding, bool operationalLive)
        : this(config, binding, operationalLive, CreateClient(config)) { }

    internal NativeMemoryHost(NativeMemoryIngestConfig config, ShadowSessionBinding binding, bool operationalLive, NativeMemoryIngestClient client)
    {
        this.client = client;
        try { ValidateAuthority(config, binding, operationalLive); outbox = new(config, binding); }
        catch { client.Dispose(); lifetime.Dispose(); throw; }
    }
    private static NativeMemoryIngestClient CreateClient(NativeMemoryIngestConfig config)
    {
        var gateway = Environment.GetEnvironmentVariable(config.GatewayTokenEnvironmentVariable);
        var capability = Environment.GetEnvironmentVariable(config.CapabilityEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(gateway) || string.IsNullOrWhiteSpace(capability))
            throw new InvalidDataException("native_memory_host_credentials_missing");
        return new(new Uri(config.Endpoint), gateway, capability);
    }
    public void StageBatch(IReadOnlyList<CanonicalShadowInbound> inputs)
    {
        if (maintenance?.IsFaulted == true) maintenance.GetAwaiter().GetResult();
        outbox.StageBatch(inputs);
    }
    public void ScheduleReconciliation()
    {
        if (maintenance?.IsFaulted == true) maintenance.GetAwaiter().GetResult();
        // Called by the single host poll loop; one maintenance task, never on the brain path.
        if (maintenance is { IsCompleted: false } || DateTimeOffset.UtcNow < nextReconciliation) return;
        nextReconciliation = DateTimeOffset.UtcNow.AddSeconds(30);
        maintenance = Task.Run(() => outbox.ReconcileAsync(client, lifetime.Token));
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        try { if (maintenance is not null) await maintenance; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { outbox.Dispose(); client.Dispose(); lifetime.Dispose(); }
    }
}
