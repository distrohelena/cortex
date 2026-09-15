using Filomena.Core.Conversations;
using Filomena.Core.Outbound;

namespace Filomena.OpenClaw.Mcp;

/// <summary>One shared stdio connection; uncertain sends are never replayed.</summary>
public sealed class OpenClawMcpSender(
    Func<CancellationToken, Task<IMcpToolClient>> connect) : IOutboundTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private Connection? current;
    private bool disposed;

    public Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return UseConnectionAsync(transport => transport.SendTextAsync(sessionKey, text, cancellationToken), cancellationToken);
    }

    // Evidence read on the same connection the send would use. It does not pin the route: the bridge resolves it again.
    public Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return UseConnectionAsync(transport => transport.DescribeRouteAsync(route, cancellationToken), cancellationToken);
    }

    private async Task<T> UseConnectionAsync<T>(Func<OpenClawMcpTransport, Task<T>> operation, CancellationToken cancellationToken)
    {
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
            return await operation(new OpenClawMcpTransport(connection.Client));
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
