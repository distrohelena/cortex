using System.Diagnostics;
using System.Security.Cryptography;

namespace Cortex.Evaluation.Execution;

public sealed record PhysicalCallIntent(string Provider, string RequestSha256, int RequestBytes);
public sealed record PhysicalCallOutcome(string Status, TimeSpan Elapsed, ProviderObservation? Observation, string? ErrorCode);

/// <summary>The live implementation must reserve durably before returning and keep uncertain reservations.
/// No in-memory implementation is suitable for paid execution.</summary>
public interface IEvaluationCallJournal
{
    Task<Guid> BeginAsync(PhysicalCallIntent intent, CancellationToken cancellationToken);
    Task FinishAsync(Guid attempt, PhysicalCallOutcome outcome, CancellationToken cancellationToken);
}

public sealed class EvaluationHttpHandler(string provider, Uri endpoint, IEvaluationCallJournal journal) : DelegatingHandler
{
    private const int MaximumBodyBytes = 1_048_576;
    // LM Studio's adapter accepts a base URI; other adapters accept the full request URI.
    // OpenJev's base URI serves both its label tokenization and its decision chat completions.
    private readonly Uri[] requestEndpoints = provider switch
    {
        "lmstudio" => [new Uri(new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/"), "chat/completions")],
        "openjev" => [new Uri(endpoint, "v1/chat/completions"), new Uri(endpoint, "tokenize")],
        _ => [endpoint]
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || !requestEndpoints.Any(uri => uri == request.RequestUri) || request.Content is null ||
            provider is not ("openai" or "jev" or "lmstudio" or "laya" or "openjev"))
            throw new InvalidDataException("Evaluation request destination or provider is not authorized.");
        var payload = await ReadBoundedAsync(request.Content, cancellationToken);
        ReplaceRequestContent(request, payload);
        var intent = new PhysicalCallIntent(provider, Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length);
        var attempt = await journal.BeginAsync(intent, cancellationToken);
        if (attempt == Guid.Empty) throw new InvalidOperationException("Invalid durable attempt reference.");
        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage? response = null;
        PhysicalCallOutcome outcome;
        Exception? failure = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
            var original = response.Content;
            response.Content = CopyContent(bytes, original);
            original.Dispose();
            var observation = ProviderObservationParser.Parse(provider, (int)response.StatusCode, bytes);
            outcome = new("response_received", Stopwatch.GetElapsedTime(started), observation, null);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
        {
            var code = error switch
            {
                OperationCanceledException => "request_cancelled",
                InvalidDataException => "response_too_large",
                _ => "transport_failure"
            };
            // The provider may have accepted the call. Never release its reservation or retry here.
            outcome = new("indeterminate", Stopwatch.GetElapsedTime(started), null, code);
            failure = error switch
            {
                OperationCanceledException => new OperationCanceledException(code, cancellationToken),
                InvalidDataException => new InvalidDataException(code),
                _ => new HttpRequestException(code)
            };
        }
        try
        {
            // An already-cancelled request token must not suppress durable final accounting.
            // Store failure leaves the begun attempt uncertain; no second Finish or transport call.
            await journal.FinishAsync(attempt, outcome, CancellationToken.None);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
        if (failure is not null)
        {
            response?.Dispose();
            throw failure;
        }
        return response!;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumBodyBytes) throw new InvalidDataException("body_too_large");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16_384];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > MaximumBodyBytes) throw new InvalidDataException("body_too_large");
            buffer.Write(chunk, 0, read);
        }
    }

    private static ByteArrayContent CopyContent(byte[] bytes, HttpContent original)
    {
        var copy = new ByteArrayContent(bytes);
        foreach (var header in original.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    private static void ReplaceRequestContent(HttpRequestMessage request, byte[] payload)
    {
        var original = request.Content!;
        request.Content = CopyContent(payload, original);
        original.Dispose();
    }
}
