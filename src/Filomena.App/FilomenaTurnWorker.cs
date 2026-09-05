using Filomena.Core.Conversations;
using Filomena.OpenClaw.Bridge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Filomena.App;

public sealed class FilomenaTurnWorker(
    ILogger<FilomenaTurnWorker> logger,
    ConversationTurnManager turnManager,
    OpenClawInboundBridgeReceiver inboundBridge) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Filomena Phase B started: inbound observation and arbitration are active; outbound sending is disabled.");
        var decisionLogger = LogDecisionsAsync(stoppingToken);
        try
        {
            await foreach (var message in inboundBridge.Events.ReadAllAsync(stoppingToken))
            {
                if (message.Conversation is null)
                {
                    logger.LogWarning(
                        "Skipped bridged provider message {MessageId}: canonical conversation metadata was unavailable.",
                        message.ProviderMessageId);
                    continue;
                }

                logger.LogInformation(
                    "Observed inbound bridge event: conversation={ConversationNamespace} messageId={MessageId} sender={SenderStableId}",
                    message.Conversation.Id.Namespace,
                    message.ProviderMessageId,
                    message.Sender?.StableId ?? "unresolved");
                await turnManager.EnqueueAsync(message, stoppingToken);
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
