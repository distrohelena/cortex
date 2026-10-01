using System.Diagnostics;
using Cortex.Inference.LmStudio;
using Microsoft.Extensions.Logging;

namespace Filomena.App;

/// <summary>Stage timings without exposing prompts, reasoning, or generated text.</summary>
public sealed class TimedLmStudioClient(
    ILmStudioChatClient arbitration,
    IResponseChatClient generation,
    ILogger<TimedLmStudioClient> logger) : ILmStudioChatClient, IResponseChatClient
{
    public Task<string> CompleteArbitrationJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
        MeasureAsync("arbitration", () => arbitration.CompleteArbitrationJsonAsync(systemPrompt, userPrompt, cancellationToken));

    public Task<string> CompleteArbitrationJsonAsync(string systemPrompt, string userPrompt,
        System.Text.Json.JsonElement schema, CancellationToken cancellationToken) =>
        MeasureAsync("arbitration", () => arbitration.CompleteArbitrationJsonAsync(systemPrompt, userPrompt, schema, cancellationToken));

    public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
        MeasureAsync("generation", () => generation.CompleteResponseAsync(systemPrompt, userPrompt, cancellationToken));

    private async Task<string> MeasureAsync(string stage, Func<Task<string>> operation)
    {
        var callId = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("LM Studio {Stage} started: callId={CallId}", stage, callId);
        try
        {
            var result = await operation();
            logger.LogInformation("LM Studio {Stage} completed: callId={CallId} elapsedMs={ElapsedMs:F0}",
                stage, callId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogWarning("LM Studio {Stage} failed: callId={CallId} elapsedMs={ElapsedMs:F0} errorType={ErrorType}",
                stage, callId, Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().Name);
            throw;
        }
    }
}
