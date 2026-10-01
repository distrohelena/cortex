using System.Globalization;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Boundaries;

public static class BoundaryEnforcementProfile
{
    public const string Id = "boundary_enforcement_v1";
    public static bool Enabled(JsonElement config)
    {
        if (!config.TryGetProperty("executionProfile", out var profile)) return false;
        if (profile.ValueKind != JsonValueKind.String || profile.GetString() is not ("normal" or Id))
            throw new InvalidDataException("Unknown instruction execution profile.");
        return profile.GetString() == Id;
    }

    public static IReadOnlyList<TypedPort> Outputs { get; } = Array.AsReadOnly(new[]
    {
        new TypedPort("resultado", "resultado", new EnumType(["permitir", "bloquear"]), true, default),
        new TypedPort("diagnostico", "diagnostico", new StringType(), true, default),
        new TypedPort("explicacao", "explicacao", new StringType(), true, default)
    });

    public static void Validate(JsonElement config, IReadOnlyList<TypedPort> inputs)
    {
        BoundaryDetectionTemplates.Read(config);
        BoundaryEnforcementPlans.Read(config);
        var expected = new Dictionary<string, FlowType> { ["resposta"] = new StringType(), ["limites"] = new ArrayType(FlowDomain.Boundary), ["mensagens"] = new ArrayType(FlowDomain.Message) };
        if (inputs.Count != 3 || inputs.Select(p => p.Name).Distinct().Count() != 3 || inputs.Any(p =>
            !expected.TryGetValue(p.Name, out var type) || !p.Required || !type.IsCompatibleWith(p.Type) || !p.Type.IsCompatibleWith(type)))
            throw new InvalidDataException("Enforcement requires resposta:Texto, limites:Boundary[], mensagens:Mensagem[].");
        foreach (var flag in new[] { "usePersonalityCore", "useStateGuidance", "useInterestProfile" })
            if (InstructionContext.Enabled(config, flag)) throw new InvalidDataException("Enforcement cannot inject character context.");
        if (TypedFlowCompiler.IsTextOutput(config) || config.TryGetProperty("responseFields", out var fields) && fields.GetArrayLength() > 0)
            throw new InvalidDataException("Enforcement outputs are derived; remove custom responseFields.");
    }

    internal static async Task<Dictionary<string, TypedValue>> RunAsync(TypedNode node, JsonElement values, string candidate,
        JsonElement messages, BoundaryExecutionRegistry registry, IFlowCompletion completion, TimeProvider clock,
        Action<IReadOnlyDictionary<string, string>> trace, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(candidate)) throw new BoundaryCompilationException("", "unsupported", "invalid_candidate");
        var boundaries = registry.ResolveBoundaries(values, clock.GetUtcNow());
        var configuredTemplate = BoundaryDetectionTemplates.Read(node.Source.Config);
        var composition = BoundaryEnforcementPlans.Read(node.Source.Config);
        if (boundaries.Count == 0) return Output(true, "no_boundaries", "Nenhum limite ativo fornecido.");
        if (composition is not null)
            return await ComposedBoundaryEnforcement.RunAsync(node, values, candidate, messages, registry, completion, clock, trace, token, composition);
        for (var index = 0; index < boundaries.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var boundary = boundaries[index];
            if (boundary.Compilation.TemplateId != configuredTemplate.TemplateId)
                throw new BoundaryCompilationException(boundary.Source.MemoryId, "unsupported", "unconfigured_boundary_template");
            var template = BoundaryDetectionTemplates.Resolve(boundary.Compilation.TemplateId, configuredTemplate.SemanticVersion, configuredTemplate.EvaluatorVersion);
            JsonElement features = default;
            BoundaryPolicyDecision decision = new(false, "provider_failure");
            var references = new Dictionary<string, string>();
            // Seed the same reference map from the original typed message values.
            var safeMessages = FlowDomain.ForModel(messages, new ArrayType(FlowDomain.Message), references);
            var safeBoundary = registry.ForModel(JsonSerializer.SerializeToElement(new[] { values[index] }), clock.GetUtcNow(), references)[0];
            var start = clock.GetTimestamp();
            double modelMs = 0, evaluatorMs = 0;
            try
            {
                var config = node.Source.Config;
                var request = new FlowCompletionRequest(node.Source.Id, template.SemanticContractDefinition,
                    JsonSerializer.SerializeToElement(new { resposta = candidate, limite = safeBoundary, mensagens = safeMessages }),
                    template.SemanticSchema.ToJsonSchema(), config.GetProperty("model").GetString(), config.GetProperty("provider").GetString(), null,
                    config.TryGetProperty("reasoningEffort", out var effort) ? effort.GetString() : null,
                    config.TryGetProperty("maxOutputTokens", out var budget) ? budget.GetInt32() : null);
                var raw = await completion.CompleteAsync(request, token);
                modelMs = clock.GetElapsedTime(start).TotalMilliseconds;
                decision = new(false, "invalid_semantic_features");
                features = template.Parse(raw);
                registry.ResolveBoundaries(values, clock.GetUtcNow());
                var evaluationStart = clock.GetTimestamp();
                decision = template.Evaluate(boundary, features, clock.GetUtcNow());
                evaluatorMs = clock.GetElapsedTime(evaluationStart).TotalMilliseconds;
                if (decision.DiagnosticCode is "invalid_boundary" or "invalid_semantic_features" or "ambiguous_relevance" or "ambiguous_semantics")
                    throw new BoundaryCompilationException(boundary.Source.MemoryId, "unsupported", decision.DiagnosticCode);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is JsonException or InvalidDataException or HttpRequestException or TimeoutException or OperationCanceledException or InvalidOperationException)
            {
                throw new BoundaryCompilationException(boundary.Source.MemoryId, "unsupported", decision.DiagnosticCode);
            }
            finally
            {
                if (modelMs == 0) modelMs = clock.GetElapsedTime(start).TotalMilliseconds;
                var metadata = new Dictionary<string, string>
                {
                    ["sourceMemoryId"] = boundary.Source.MemoryId,
                    ["template"] = boundary.Compilation.TemplateId,
                    ["evaluator"] = template.EvaluatorVersion,
                    ["semanticContract"] = template.SemanticVersion,
                    ["result"] = decision.Allow ? "Allow" : "Block",
                    ["diagnosticCode"] = decision.DiagnosticCode,
                    ["modelMilliseconds"] = modelMs.ToString("R", CultureInfo.InvariantCulture),
                    ["evaluatorMilliseconds"] = evaluatorMs.ToString("R", CultureInfo.InvariantCulture)
                };
                if (features.ValueKind == JsonValueKind.Object)
                    foreach (var field in features.EnumerateObject()) metadata[field.Name] = field.Value.ToString();
                trace(metadata);
            }
            token.ThrowIfCancellationRequested();
            if (!decision.Allow)
                return Output(false, decision.DiagnosticCode, template.BuildRegenerationFeedback(features, decision));
        }
        registry.ResolveBoundaries(values, clock.GetUtcNow());
        return Output(true, "all_boundaries_allow", "Todas as boundaries fornecidas passaram pelo evaluator.");
    }

    private static Dictionary<string, TypedValue> Output(bool allow, string diagnostic, string explanation) => new()
    {
        ["resultado"] = new(Outputs[0].Type, JsonSerializer.SerializeToElement(allow ? "permitir" : "bloquear")),
        ["diagnostico"] = new(new StringType(), JsonSerializer.SerializeToElement(diagnostic)),
        ["explicacao"] = new(new StringType(), JsonSerializer.SerializeToElement(explanation))
    };
}
