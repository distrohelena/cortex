using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Inference;

public sealed record CompletionAttempt(string Provider, string Model, int Attempt, string Outcome, double DurationMilliseconds, string? Error);
public sealed record ValidatedResult<T>(T Value, IReadOnlyList<CompletionAttempt> Attempts);
public sealed class ValidatedCompletionException(IReadOnlyList<CompletionAttempt> attempts)
    : Exception("No provider returned a valid result.")
{
    public IReadOnlyList<CompletionAttempt> Attempts { get; } = attempts;
}

/// <summary>Read-only inference policy: local first, one validation repair, then one remote attempt.
/// Caller cancellation never escalates. The caller's deterministic validator is authoritative.</summary>
public sealed class ValidatedCompletion(IFlowCompletion providers, string localModel, string fallbackModel, bool allowFallback = true)
{
    public async Task<ValidatedResult<T>> CompleteAsync<T>(FlowCompletionRequest request, Func<string, T> validate, CancellationToken token)
    {
        var attempts = new List<CompletionAttempt>();
        string? correction = null;
        for (var attempt = 0; attempt < (allowFallback ? 3 : 2); attempt++)
        {
            token.ThrowIfCancellationRequested();
            var remote = attempt == 2;
            var provider = remote ? "openai" : "lmstudio";
            var model = remote ? fallbackModel : localModel;
            var started = Stopwatch.GetTimestamp();
            string text;
            try
            {
                text = await providers.CompleteAsync(request with { Provider = provider, Model = model, Correction = correction }, token);
            }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or JsonException or InvalidOperationException or OperationCanceledException && !token.IsCancellationRequested)
            {
                attempts.Add(new(provider, model, attempt, "model_failure", Stopwatch.GetElapsedTime(started).TotalMilliseconds, error.GetType().Name));
                if (remote || !allowFallback) break;
                attempt = 1; // A transport failure is not repaired with another local request.
                correction = null;
                continue;
            }
            token.ThrowIfCancellationRequested();
            try
            {
                var value = validate(text);
                attempts.Add(new(provider, model, attempt, "accepted", Stopwatch.GetElapsedTime(started).TotalMilliseconds, null));
                return new(value, attempts);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                correction = error is JsonException ? "Return valid JSON matching the schema." : error.Message;
                if (correction.Length > 400) correction = correction[..400];
                attempts.Add(new(provider, model, attempt, "invalid_output", Stopwatch.GetElapsedTime(started).TotalMilliseconds, correction));
            }
        }
        throw new ValidatedCompletionException(attempts);
    }
}
