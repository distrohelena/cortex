namespace Filomena.OpenClaw.Mcp;

public sealed class ObservedMessageRegistry(int capacity = 10_000)
{
    private readonly Lock gate = new();
    private readonly HashSet<string> keys = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    public bool TryRemember(string sessionKey, string providerMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        var key = $"{sessionKey}\0{providerMessageId}";
        lock (gate)
        {
            if (!keys.Add(key))
            {
                return false;
            }

            order.Enqueue(key);
            while (order.Count > capacity)
            {
                keys.Remove(order.Dequeue());
            }

            return true;
        }
    }
}
