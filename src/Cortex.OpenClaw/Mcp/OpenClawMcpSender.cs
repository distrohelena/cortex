using Cortex.Core.Conversations;
using Cortex.Core.Outbound;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cortex.OpenClaw.Mcp;

/// <summary>One shared stdio connection; uncertain sends are never replayed.</summary>
public sealed class OpenClawMcpSender(
    Func<CancellationToken, Task<IMcpToolClient>> connect,
    ILogger<OpenClawMcpSender>? logger = null) : IOutboundTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private Connection? current;
    private bool disposed;

    // Evidence read on the same connection the send would use. It does not pin the route: the bridge resolves it again.
    public async Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var connection = await AcquireReadyAsync(cancellationToken);
        var failed = false;
        try
        {
            return await new OpenClawMcpTransport(connection.Client).DescribeRouteAsync(route, cancellationToken);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally { await ReleaseAsync(connection, failed); }
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await AcquireReadyAsync(cancellationToken);
        await ReleaseAsync(connection, false);
    }

    public async Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var connection = await AcquireReadyAsync(cancellationToken);
        var failed = false;
        var started = Stopwatch.GetTimestamp();
        logger?.LogInformation("MCP send started: session={SessionKey}", sessionKey);
        try
        {
            var receipt = await new OpenClawMcpTransport(connection.Client).SendTextAsync(sessionKey, text, cancellationToken);
            logger?.LogInformation("MCP send confirmed: session={SessionKey} elapsedMs={ElapsedMs:F0} messageId={MessageId}",
                sessionKey, Stopwatch.GetElapsedTime(started).TotalMilliseconds, receipt.ProviderMessageId);
            return receipt;
        }
        catch
        {
            failed = true;
            logger?.LogWarning("MCP send failed: session={SessionKey} elapsedMs={ElapsedMs:F0}. Delivery may be uncertain; no replay.",
                sessionKey, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        finally { await ReleaseAsync(connection, failed); }
    }

    private async Task<Connection> AcquireReadyAsync(CancellationToken cancellationToken)
    {
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            for (var attempt = 0; ; attempt++)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    if (current is null)
                    {
                        logger?.LogInformation("MCP connection starting");
                        current = new Connection(await connect(cancellationToken));
                        logger?.LogInformation("MCP connected: elapsedMs={ElapsedMs:F0}", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                    started = Stopwatch.GetTimestamp();
                    // Read-only round trip to the actual MCP server, not a cached local flag.
                    // This checks MCP liveness, not WhatsApp delivery readiness.
                    using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    healthTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await current.Client.ListToolsAsync(healthTimeout.Token);
                    logger?.LogInformation("MCP preflight ready: elapsedMs={ElapsedMs:F0}", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    current.Users++;
                    return current;
                }
                catch
                {
                    var broken = current;
                    current = null;
                    if (broken is not null)
                    {
                        broken.Retired = true;
                        if (broken.Users == 0) await broken.Client.DisposeAsync();
                    }
                    logger?.LogWarning("MCP preflight failed: attempt={Attempt} elapsedMs={ElapsedMs:F0}; no message dispatched.",
                        attempt + 1, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    if (cancellationToken.IsCancellationRequested || attempt >= 1) throw;
                    // Only the read-only preflight may retry. Never replay messages_send.
                }
            }
        }
        finally { connectionGate.Release(); }
    }

    private async Task ReleaseAsync(Connection connection, bool failed)
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
