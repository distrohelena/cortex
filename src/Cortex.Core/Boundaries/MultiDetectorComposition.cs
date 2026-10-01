using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Boundaries;

// Bounded execution of a template-owned detector composition; no character-specific rules.
public enum DetectionOutcome { Allow, Block, EnforcementFailure }
public sealed record DetectorVerdict(DetectionOutcome Outcome, string Diagnostic, string Feedback);
public sealed record DetectorDefinition(string Id, string Contract, ObjectType Schema,
    Func<JsonElement, DetectorVerdict> Evaluate, string Provider, string Model, string Effort, int OutputBudget, string? RequestNodeId = null)
{
    public string EvaluatorVersion { get; init; } = "";
}
public sealed record DetectorObservation(string Id, DetectorVerdict Verdict, JsonElement? Features,
    double QueueMilliseconds, double ModelMilliseconds, double EvaluatorMilliseconds);
public sealed record ComposedDetection(DetectorVerdict Verdict, IReadOnlyList<DetectorObservation> Calls, double WallMilliseconds);

public static class MultiDetectorComposition
{
    public const string Strategy = "multiple_detectors_v1";
    public const string Aggregation = "any_block_all_allow:v1";

    public static DetectorVerdict Aggregate(IReadOnlyList<DetectorVerdict> results)
    {
        if (results.Count == 0 || results.Any(x => !Enum.IsDefined(x.Outcome) || x.Outcome == DetectionOutcome.EnforcementFailure))
            return new(DetectionOutcome.EnforcementFailure, "enforcement_failure", "");
        var blocked = results.Where(x => x.Outcome == DetectionOutcome.Block).ToArray();
        return blocked.Length == 0
            ? new(DetectionOutcome.Allow, "all_detectors_allow", "")
            : new(DetectionOutcome.Block, "detector_block", string.Join(" ", blocked.Select(x => x.Feedback)
                .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal)));
    }

    public static async Task<ComposedDetection> ExecuteAsync(IReadOnlyList<DetectorDefinition> definitions,
        JsonElement inputs, Func<DetectorDefinition, IFlowCompletion> completion, SemaphoreSlim sharedLimit, CancellationToken token)
    {
        var wall = Stopwatch.StartNew();
        if (definitions.Count == 0 || definitions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            return new(new(DetectionOutcome.EnforcementFailure, "invalid_composition", ""), [], wall.Elapsed.TotalMilliseconds);
        var calls = await Task.WhenAll(definitions.Select(Invoke));
        token.ThrowIfCancellationRequested();
        return new(Aggregate(calls.Select(x => x.Verdict).ToArray()), calls, wall.Elapsed.TotalMilliseconds);

        async Task<DetectorObservation> Invoke(DetectorDefinition definition)
        {
            var queue = Stopwatch.StartNew();
            await sharedLimit.WaitAsync(token);
            var queued = queue.Elapsed.TotalMilliseconds;
            var model = Stopwatch.StartNew();
            double modelMs = 0;
            try
            {
                var raw = await completion(definition).CompleteAsync(new(definition.RequestNodeId ?? definition.Id, definition.Contract, inputs.Clone(),
                    definition.Schema.ToJsonSchema(), definition.Model, definition.Provider, null, definition.Effort, definition.OutputBudget), token);
                modelMs = model.Elapsed.TotalMilliseconds;
                var evaluator = Stopwatch.StartNew();
                using var parsed = JsonDocument.Parse(raw);
                FlowDomain.Check(parsed.RootElement, definition.Schema);
                var names = parsed.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
                var expected = definition.Schema.ToJsonSchema().GetProperty("properties").EnumerateObject()
                    .Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
                if (!names.SequenceEqual(expected)) throw new InvalidDataException();
                var features = parsed.RootElement.Clone();
                return new(definition.Id, definition.Evaluate(features), features, queued, modelMs, evaluator.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                var diagnostic = error switch
                {
                    JsonException or InvalidDataException => "invalid_output",
                    HttpRequestException or TimeoutException or OperationCanceledException => "provider_failure",
                    _ => "internal_error"
                };
                return new(definition.Id, new(DetectionOutcome.EnforcementFailure, diagnostic, ""), null,
                    queued, modelMs == 0 ? model.Elapsed.TotalMilliseconds : modelMs, 0);
            }
            finally { sharedLimit.Release(); }
        }
    }
}
