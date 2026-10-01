using Cortex.OpenClaw.Bridge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Filomena.App.FlowRuntime;

public sealed class DmFlowLiveWorker(OpenClawInboundBridgeReceiver receiver, DmFlowLiveSession session, ILogger<DmFlowLiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<Task>();
        try
        {
            await foreach (var message in receiver.Events.ReadAllAsync(stoppingToken))
            {
                pending.RemoveAll(task => task.IsCompleted);
                // Admission captures the snapshot before any semaphore wait.
                var observation = session.ObserveAsync(message, stoppingToken);
                pending.Add(LogAsync(observation, message.ProviderMessageId));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { session.Deactivate();await Task.WhenAll(pending); }
    }
    private async Task LogAsync(Task<DmFlowObservation> task, string messageId)
    {
        var result = await task;
        logger.LogInformation("Fluxo DM: mensagem={MessageId} resultado={Outcome} envio={OutboundMessageId}", messageId, result.Outcome, result.OutboundMessageId ?? "não enviado");
    }
}
