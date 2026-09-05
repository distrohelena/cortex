using Filomena.Core.Conversations;

namespace Filomena.OpenClaw.Mcp;

/// <summary>One shared stdio connection; uncertain sends are never replayed.</summary>
public sealed class OpenClawMcpSender(
    Func<CancellationToken, Task<IMcpToolClient>> connect) : IConversationSender, IAsyncDisposable
{
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private Connection? current;
    private bool disposed;

    public async Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Connection connection;
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            current ??= new Connection(await connect(cancellationToken));
            connection = current;
            connection.Users++;
        }
        finally { connectionGate.Release(); }

        var failed = false;
        try
        {
            return await new OpenClawMcpTransport(connection.Client).SendTextAsync(sessionKey, text, cancellationToken);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            // Retire failed connections without disrupting other in-flight sends.
            await connectionGate.WaitAsync(CancellationToken.None);
            bool disposeClient;
            try
            {
                connection.Users--;
                connection.Retired |= failed;
                if (connection.Retired && ReferenceEquals(current, connection)) current = null;
                disposeClient = connection.Retired && connection.Users == 0;
            }
            finally { connectionGate.Release(); }
            if (disposeClient) await connection.Client.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connectionGate.WaitAsync();
        IMcpToolClient? client = null;
        try
        {
            disposed = true;
            if (current is not null)
            {
                current.Retired = true;
                if (current.Users == 0) client = current.Client;
                current = null;
            }
        }
        finally { connectionGate.Release(); }
        if (client is not null) await client.DisposeAsync();
    }

    private sealed class Connection(IMcpToolClient client)
    {
        public IMcpToolClient Client { get; } = client;
        public int Users { get; set; }
        public bool Retired { get; set; }
    }
}
