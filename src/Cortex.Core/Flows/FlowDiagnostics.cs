using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cortex.Core.Flows;

public enum FlowFailureCategory { Unknown, Provider, Parsing, Validation, Runtime, Cancellation }
public enum FlowFailureOrigin { Runtime, HostNode, Adapter, HostValidation }

/// <summary>Safe envelope. Exception is trusted, process-local data and must not be logged indiscriminately.</summary>
public sealed record FlowFailureDiagnostic(string NodeId, string? Port, FlowFailureCategory Category,
    FlowFailureOrigin Origin, string Code, string ExceptionType, int? HttpStatusCode)
{
    [JsonIgnore] public Exception? Exception { get; init; }
    public IReadOnlyList<string> ExceptionTypes { get; init; } = [];
    public override string ToString() => $"{Code} ({Category}, {Origin}, node={NodeId})";

    /// <summary>Rethrows the original object with its original stack, if still available in this process.</summary>
    public void ThrowOriginal()
    {
        if (Exception is null) throw new InvalidOperationException("Original exception is unavailable after serialization.");
        ExceptionDispatchInfo.Capture(Exception).Throw();
    }
}

/// <summary>Provider/host-reported values, never estimates. Null means unknown; CostUsd uses the host's effective tariff.</summary>
public sealed record CompletionUsage(long? InputTokens = null, long? OutputTokens = null, decimal? CostUsd = null,
    string? ObservedModel = null, string? ObservedProvider = null);

public sealed record FlowInferenceAttempt(string AttemptId, string NodeId, string? RequestedProvider,
    string? RequestedModel, TimeSpan Duration, string Outcome, CompletionUsage Usage, FlowFailureDiagnostic? Failure)
{
    [JsonIgnore] public string? CapturedResponse { get; init; }
    public string CaptureStatus { get; init; } = "disabled";
    public override string ToString() => $"Inference {AttemptId}: {Outcome} (node={NodeId})";
}

public sealed class FlowDiagnosticOptions
{
    public bool CaptureRawResponses { get; init; }
    /// <summary>Required when capture is enabled. Return null to discard. Must remove domain-sensitive data.</summary>
    public Func<string, string?>? RedactResponse { get; init; }
    public int MaxResponseCharacters { get; init; } = 4096;

    internal void Validate()
    {
        if (CaptureRawResponses && RedactResponse is null)
            throw new ArgumentException("Response capture requires a host-owned redactor.");
        if (MaxResponseCharacters is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(MaxResponseCharacters));
    }
}

/// <summary>Per-invocation reporting channel. Adapters should report usage before parsing/validation can fail.</summary>
public sealed class CompletionDiagnostics
{
    private readonly object gate = new();
    private readonly FlowDiagnosticOptions options;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly string nodeId;
    private readonly string? requestedProvider;
    private readonly string? requestedModel;
    private CompletionUsage usage = new();
    private FlowFailureDiagnostic? failure;
    private string? captured;
    private string captureStatus;
    private bool responseSeen;
    private readonly List<string> sensitiveValues = [];
    private FlowInferenceAttempt? finished;
    public bool CaptureEnabled => options.CaptureRawResponses;
    internal string AttemptId { get; } = Guid.NewGuid().ToString("N");

    internal CompletionDiagnostics(FlowCompletionRequest request, FlowDiagnosticOptions options)
    {
        nodeId = request.NodeId;
        requestedProvider = request.Provider;
        requestedModel = request.Model;
        this.options = options;
        captureStatus = options.CaptureRawResponses ? "not_available" : "disabled";
    }

    /// <summary>Reports cumulative totals for this one invocation; partial reports merge without adding them twice.</summary>
    public void ReportUsage(CompletionUsage value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.InputTokens < 0 || value.OutputTokens < 0 || value.CostUsd < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        lock (gate)
        {
            if (finished is not null) return;
            usage = new(value.InputTokens ?? usage.InputTokens, value.OutputTokens ?? usage.OutputTokens,
                value.CostUsd ?? usage.CostUsd, value.ObservedModel ?? usage.ObservedModel,
                value.ObservedProvider ?? usage.ObservedProvider);
        }
    }

    /// <summary>Registers a known credential for literal removal during this invocation's optional capture.</summary>
    public void ProtectSensitiveValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (gate)
            if (options.CaptureRawResponses && !responseSeen && finished is null) sensitiveValues.Add(value);
    }

    /// <summary>Never captures headers or requests. Redaction fails closed and cannot change execution semantics.</summary>
    public void CaptureResponse(string response)
    {
        lock (gate)
        {
            if (finished is not null || responseSeen) return;
            responseSeen = true;
            if (!options.CaptureRawResponses) return;
            try
            {
                foreach (var secret in sensitiveValues) response = response.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                var safe = options.RedactResponse!(response);
                if (safe is null) { captureStatus = "discarded"; return; }
                foreach (var secret in sensitiveValues) safe = safe.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                // Defense in depth, not a substitute for the host's domain-specific redactor.
                safe = Regex.Replace(safe, @"(?i)\b(Bearer|Basic)\s+[^\s""<>]+", "$1 [REDACTED]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                safe = Regex.Replace(safe, @"\bsk-[A-Za-z0-9_-]+", "[REDACTED]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                safe = Regex.Replace(safe, @"(?i)(""(?:api[_-]?key|access[_-]?token|password|secret|authorization)""\s*:\s*)""[^""\r\n]*""", "$1\"[REDACTED]\"", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                captureStatus = safe.Length > options.MaxResponseCharacters ? "truncated" : "captured";
                captured = safe[..Math.Min(safe.Length, options.MaxResponseCharacters)];
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { captureStatus = "redaction_failed"; captured = null; }
            finally { sensitiveValues.Clear(); }
        }
    }

    public void ReportFailure(Exception error, FlowFailureCategory category, FlowFailureOrigin origin = FlowFailureOrigin.Adapter)
    {
        lock (gate)
        {
            if (finished is null) failure = FlowDiagnostics.Create(error, nodeId, null, origin, category);
        }
    }

    internal FlowFailureDiagnostic FailureFor(Exception error)
    {
        lock (gate) return failure ?? FlowDiagnostics.Create(error, nodeId, null, FlowFailureOrigin.Adapter);
    }

    internal void RecordValidation(Exception? error)
    {
        lock (gate)
        {
            if (finished is null) return;
            finished = finished with { Outcome = error is null ? "completed" : "invalid_output",
                Failure = error is null ? null : FlowDiagnostics.Create(error, nodeId, null, FlowFailureOrigin.Runtime) };
            FlowDiagnosticContext.Record(finished);
        }
    }

    internal FlowInferenceAttempt Finish(string outcome, Exception? error = null, FlowFailureOrigin origin = FlowFailureOrigin.Adapter)
    {
        lock (gate)
        {
            if (finished is not null) return finished;
            sensitiveValues.Clear();
            if (error is not null) failure ??= FlowDiagnostics.Create(error, nodeId, null, origin);
            finished = new(AttemptId, nodeId, requestedProvider, requestedModel,
                Stopwatch.GetElapsedTime(started), outcome, usage, failure)
                { CapturedResponse = captured, CaptureStatus = captureStatus };
            FlowDiagnosticContext.Record(finished);
            return finished;
        }
    }
}

public static class FlowDiagnostics
{
    private sealed record Evidence(FlowFailureDiagnostic Failure, TypedRunResult? Result);
    private static readonly ConditionalWeakTable<Exception, Evidence> Failures = new();
    public static FlowFailureDiagnostic? GetFailure(Exception error) => Failures.TryGetValue(error, out var value) ? value.Failure : null;
    public static TypedRunResult? GetRunResult(Exception error) => Failures.TryGetValue(error, out var value) ? value.Result : null;

    internal static void Attach(Exception error, FlowFailureDiagnostic failure, TypedRunResult? result = null) =>
        Failures.AddOrUpdate(error, new(failure, result));

    internal static FlowFailureDiagnostic Create(Exception error, string nodeId, string? port, FlowFailureOrigin origin,
        FlowFailureCategory? category = null)
    {
        for (Exception? candidate = error; candidate is not null; candidate = candidate.InnerException)
            if (GetFailure(candidate) is { } known) return known with { NodeId = nodeId, Port = port ?? known.Port };
        category ??= error switch
        {
            OperationCanceledException => FlowFailureCategory.Cancellation,
            JsonException => FlowFailureCategory.Parsing,
            HttpRequestException or TimeoutException when origin == FlowFailureOrigin.Adapter => FlowFailureCategory.Provider,
            InvalidDataException when origin is FlowFailureOrigin.Runtime or FlowFailureOrigin.HostValidation => FlowFailureCategory.Validation,
            _ => origin == FlowFailureOrigin.Runtime ? FlowFailureCategory.Runtime : FlowFailureCategory.Unknown
        };
        var types = new List<string>();
        void Visit(Exception current, int depth)
        {
            if (depth > 32 || types.Count >= 64) return;
            types.Add(current.GetType().FullName ?? current.GetType().Name);
            if (current is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) Visit(inner, depth + 1);
            else if (current.InnerException is { } inner) Visit(inner, depth + 1);
        }
        Visit(error, 0);
        return new(nodeId, port, category.Value, origin, category.Value.ToString().ToLowerInvariant() + "_failure",
            error.GetType().FullName ?? error.GetType().Name, error is HttpRequestException http ? (int?)http.StatusCode : null)
            { Exception = error, ExceptionTypes = types.ToArray() };
    }
}

internal sealed class FlowDiagnosticContext : IDisposable
{
    private static readonly AsyncLocal<FlowDiagnosticContext?> Current = new();
    private readonly FlowDiagnosticContext? previous;
    private readonly List<FlowInferenceAttempt> attempts;
    internal FlowDiagnosticOptions Options { get; }

    internal FlowDiagnosticContext(List<FlowInferenceAttempt> attempts, FlowDiagnosticOptions options)
    {
        previous = Current.Value;
        this.attempts = attempts;
        Options = options;
        Current.Value = this;
    }
    internal static CompletionDiagnostics Begin(FlowCompletionRequest request, FlowDiagnosticOptions? options = null) =>
        request.Diagnostics ?? new(request, Current.Value?.Options ?? options ?? new());
    internal static void Record(FlowInferenceAttempt attempt)
    {
        if (Current.Value is not { } context) return;
        lock (context.attempts)
        {
            var index = context.attempts.FindIndex(a => a.AttemptId == attempt.AttemptId);
            if (index < 0) context.attempts.Add(attempt);
            else context.attempts[index] = attempt;
        }
    }
    public void Dispose() => Current.Value = previous;

    internal static async Task<(string Text, CompletionDiagnostics Diagnostics)> CompleteAsync(IFlowCompletion completion, FlowCompletionRequest request, CancellationToken token)
    {
        var diagnostics = Begin(request);
        try
        {
            var output = await completion.CompleteAsync(request with { Diagnostics = diagnostics }, token);
            diagnostics.CaptureResponse(output);
            diagnostics.Finish("returned_pending_validation");
            return (output, diagnostics);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var attempt = diagnostics.Finish("failed", error);
            FlowDiagnostics.Attach(error, attempt.Failure!);
            throw;
        }
    }
}

internal sealed class DiagnosticFlowCompletion(IFlowCompletion inner) : IFlowCompletion
{
    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
        (await FlowDiagnosticContext.CompleteAsync(inner, request, token)).Text;
}
