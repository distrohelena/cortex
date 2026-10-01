using Cortex.OpenClaw.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Filomena.App;

/// <summary>Prepares the shared MCP process, never starts the Gateway or sends messages.</summary>
public sealed class OpenClawMcpWarmupService(
    OpenClawMcpSender sender,
    ILogger<OpenClawMcpWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await sender.EnsureReadyAsync(timeout.Token);
            logger.LogInformation("Shared MCP connection ready before inbound processing.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MCP warm-up unavailable ({ErrorType}); observation remains active. The next send will recheck/reconnect before dispatch.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
