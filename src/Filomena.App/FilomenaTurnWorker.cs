using Filomena.Core.Conversations;
using Filomena.OpenClaw.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filomena.App;

public sealed class FilomenaTurnWorker(
    IOptions<ProbeOptions> configuredOptions,
    ILoggerFactory loggerFactory,
    ILogger<FilomenaTurnWorker> logger,
    ConversationTurnManager turnManager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Filomena Phase B started: inbound observation and arbitration are active; outbound sending is disabled.");
        var decisionLogger = LogDecisionsAsync(stoppingToken);
        var observedMessages = new ObservedMessageRegistry();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var options = configuredOptions.Value;
                    var clientOptions = new OpenClawMcpClientOptions(
                        options.OpenClawCommand,
                        options.EffectiveOpenClawArguments);
                    await using var client = await OpenClawMcpClient.ConnectAsync(
                        clientOptions,
                        loggerFactory,
                        stoppingToken);
                    var transport = new OpenClawMcpTransport(client, observedMessages);
                    await foreach (var message in transport.ListenAsync(
                        TimeSpan.FromSeconds(30),
                        stoppingToken))
                    {
                        if (message.Conversation is null)
                        {
                            logger.LogWarning(
                                "Skipped inbound provider message {MessageId}: canonical conversation metadata was unavailable.",
                                message.ProviderMessageId);
                            continue;
                        }

                        await turnManager.EnqueueAsync(message, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    var delay = TimeSpan.FromSeconds(configuredOptions.Value.ReconnectDelaySeconds);
                    logger.LogWarning(
                        exception,
                        "OpenClaw MCP connection failed; reconnecting in {Seconds} seconds.",
                        delay.TotalSeconds);
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }
        finally
        {
            try
            {
                await decisionLogger;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task LogDecisionsAsync(CancellationToken cancellationToken)
    {
        await foreach (var result in turnManager.Results.ReadAllAsync(cancellationToken))
        {
            logger.LogInformation(
                "Phase-B turn decision: conversation={ConversationNamespace} decision={Decision} reason={Reason} anchor={AnchorRef} target={TargetRef} messages={MessageCount}. No response was sent.",
                result.Conversation.Id.Namespace,
                result.Decision.Decision,
                result.Decision.Reason,
                result.Decision.AnchorRef,
                result.Decision.TargetRef,
                result.PendingMessageIds.Count);
        }
    }
}
