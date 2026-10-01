using System.Globalization;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Boundaries;

internal static class ComposedBoundaryEnforcement
{
    // Shared across sessions: bounded HTTP concurrency, not a session/global execution lock.
    private static readonly SemaphoreSlim Requests = new(2);

    internal static async Task<Dictionary<string, TypedValue>> RunAsync(TypedNode node, JsonElement values, string candidate,
        JsonElement messages, BoundaryExecutionRegistry registry, IFlowCompletion completion, TimeProvider clock,
        Action<IReadOnlyDictionary<string, string>> trace, CancellationToken token, IBoundaryEnforcementPlan plan)
    {
        var boundaries = registry.ResolveBoundaries(values, clock.GetUtcNow());
        for (var index = 0; index < boundaries.Count; index++)
        {
            var boundary = boundaries[index];
            var references = new Dictionary<string, string>();
            var safeMessages = FlowDomain.ForModel(messages, new ArrayType(FlowDomain.Message), references);
            var safeBoundary = registry.ForModel(JsonSerializer.SerializeToElement(new[] { values[index] }), clock.GetUtcNow(), references)[0];
            var definitions = plan.CreateDetectors(boundary, node.Source.Config, node.Source.Id, clock);
            var result = await MultiDetectorComposition.ExecuteAsync(definitions, JsonSerializer.SerializeToElement(new
            {
                resposta = candidate,
                limite = safeBoundary,
                mensagens = safeMessages
            }), _ => completion, Requests, token);
            foreach (var call in result.Calls)
            {
                var definition = definitions.Single(d => d.Id == call.Id);
                var metadata = new Dictionary<string, string>
                {
                    ["sourceMemoryId"] = boundary.Source.MemoryId,
                    ["template"] = boundary.Compilation.TemplateId,
                    ["strategy"] = plan.Strategy,
                    ["aggregation"] = plan.AggregationVersion,
                    ["detector"] = call.Id,
                    ["result"] = call.Verdict.Outcome.ToString(),
                    ["evaluator"] = definition.EvaluatorVersion,
                    ["provider"] = definition.Provider,
                    ["model"] = definition.Model,
                    ["reasoningEffort"] = definition.Effort,
                    ["diagnosticCode"] = call.Verdict.Diagnostic,
                    ["aggregateResult"] = result.Verdict.Outcome.ToString(),
                    ["modelMilliseconds"] = call.ModelMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                    ["evaluatorMilliseconds"] = call.EvaluatorMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                    ["queueMilliseconds"] = call.QueueMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                    ["wallMilliseconds"] = result.WallMilliseconds.ToString("R", CultureInfo.InvariantCulture)
                };
                if (call.Features is { ValueKind: JsonValueKind.Object } features)
                    foreach (var field in features.EnumerateObject()) metadata[field.Name] = field.Value.ToString();
                trace(metadata);
            }
            registry.ResolveBoundaries(values, clock.GetUtcNow());
            if (result.Verdict.Outcome == DetectionOutcome.EnforcementFailure)
                throw new BoundaryCompilationException(boundary.Source.MemoryId, "unsupported", "enforcement_failure");
            if (result.Verdict.Outcome == DetectionOutcome.Block)
                return Output(false, result.Verdict.Diagnostic, result.Verdict.Feedback);
        }
        return Output(true, "all_boundaries_allow", "Todas as boundaries passaram por todos os detectors.");
    }

    private static Dictionary<string, TypedValue> Output(bool allow, string diagnostic, string feedback) => new()
    {
        ["resultado"] = new(BoundaryEnforcementProfile.Outputs[0].Type, JsonSerializer.SerializeToElement(allow ? "permitir" : "bloquear")),
        ["diagnostico"] = new(new StringType(), JsonSerializer.SerializeToElement(diagnostic)),
        ["explicacao"] = new(new StringType(), JsonSerializer.SerializeToElement(feedback))
    };
}
