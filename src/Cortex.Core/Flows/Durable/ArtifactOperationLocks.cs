namespace Cortex.Core.Flows.Durable;

// Shared by drivers of one store. Entries exist only while an operation has holders/waiters.
internal sealed class ArtifactOperationLocks
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private sealed class Entry { internal SemaphoreSlim Semaphore { get; } = new(1, 1); internal int Users; }
    internal async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken token)
    {
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(key, out entry!)) entries.Add(key, entry = new());
            entry.Users++;
        }
        try { await entry.Semaphore.WaitAsync(token).ConfigureAwait(false); }
        catch { Drop(key, entry, false); throw; }
        return new Lease(() => Drop(key, entry, true));
    }
    private void Drop(string key, Entry entry, bool acquired)
    {
        lock (gate)
        {
            if (acquired) entry.Semaphore.Release();
            if (--entry.Users == 0) { entries.Remove(key); entry.Semaphore.Dispose(); }
        }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? callback = release;
        public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
    }
}
