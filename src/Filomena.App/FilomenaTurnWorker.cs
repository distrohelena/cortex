using Filomena.Core.Conversations;
using Filomena.OpenClaw.Bridge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filomena.App;

public sealed class FilomenaTurnWorker(
    ILogger<FilomenaTurnWorker> logger,
    ConversationTurnManager turnManager,
    OpenClawInboundBridgeReceiver inboundBridge,
    IOptions<ProbeOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(options.Value.Mode == "phase-c"
            ? "Filomena Phase C started: standalone outbound sending is enabled for configured conversations after Respond."
            : "Filomena Phase B started: inbound observation and arbitration are active; outbound sending is disabled.");
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
            await turnManager.DisposeAsync();
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
                "Turn decision: conversation={ConversationNamespace} decision={Decision} reason={Reason} anchor={AnchorRef} target={TargetRef} messages={MessageCount} responseKind={ResponseKind} recheckAfterSeconds={RecheckAfterSeconds} awaitingInbound={AwaitingInbound} outboundMessageId={OutboundMessageId}.",
                result.Conversation.Id.Namespace,
                result.Decision.Decision,
                result.Decision.Reason,
                result.Decision.AnchorRef,
                result.Decision.TargetRef,
                result.PendingMessageIds.Count,
                result.Decision.ResponseKind,
                result.Decision.RecheckAfterSeconds,
                result.AwaitingInbound,
                result.SentTurn?.ProviderMessageId ?? "not-sent");
            if (result.ExecutionError is not null)
                logger.LogWarning("Generation/send failed: conversation={ConversationNamespace} error={Error}. No automatic send retry; delivery may be uncertain.",
                    result.Conversation.Id.Namespace, result.ExecutionError);
        }
    }
}
