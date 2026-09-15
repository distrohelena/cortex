using System.Text.Json;
using Filomena.Core.Flows;

namespace Filomena.Core.Boundaries;

public interface IBoundaryDetectionTemplate
{
    string TemplateId { get; }
    string SemanticVersion { get; }
    string EvaluatorVersion { get; }
    string PolicyDescription { get; }
    string DisclosurePolicy { get; }
    string SemanticContractDefinition { get; }
    ObjectType SemanticSchema { get; }
    JsonElement Parse(string raw);
    BoundaryPolicyDecision Evaluate(Boundary? boundary, JsonElement features, DateTimeOffset now);
    string BuildRegenerationFeedback(JsonElement features, BoundaryPolicyDecision decision);
}

public static class BoundaryDetectionTemplates
{
    private static readonly IBoundaryDetectionTemplate[] Registered = [new LegacyTopicDetectionV1(), new NoTeasingTopicDetectionV2()];
    public static IBoundaryDetectionTemplate Resolve(string template, string semantic, string evaluator) =>
        Registered.SingleOrDefault(t => t.TemplateId == template && t.SemanticVersion == semantic && t.EvaluatorVersion == evaluator)
        ?? throw new InvalidDataException("Unsupported pinned boundary detection versions.");

    public static IBoundaryDetectionTemplate Read(JsonElement config) => Resolve(
        config.GetProperty("boundaryTemplateVersion").GetString()!, config.GetProperty("semanticContractVersion").GetString()!,
        config.GetProperty("boundaryEvaluatorVersion").GetString()!);

    // Historical snapshots stay pinned. New configurations explicitly select semantic-v2/evaluator-v2.
    private sealed class LegacyTopicDetectionV1 : IBoundaryDetectionTemplate
    {
        public string TemplateId => "no_teasing_about_topic:v1";
        public string SemanticVersion => BoundarySemanticFeatures.Version;
        public string EvaluatorVersion => BoundaryPolicyEvaluator.Version;
        public string PolicyDescription => BoundaryPolicies.TopicV1;
        public string DisclosurePolicy => BoundaryPolicies.DisclosureV1;
        public string SemanticContractDefinition => BoundarySemanticFeatures.Prompt;
        public ObjectType SemanticSchema => BoundarySemanticFeatures.Contract;
        public JsonElement Parse(string raw) => BoundarySemanticFeatures.Parse(raw).ToJson();
        public BoundaryPolicyDecision Evaluate(Boundary? boundary, JsonElement features, DateTimeOffset now) =>
            BoundaryPolicyEvaluator.Evaluate(boundary, BoundarySemanticFeatures.Parse(features.GetRawText()), now);
        public string BuildRegenerationFeedback(JsonElement features, BoundaryPolicyDecision decision) =>
            $"Policy {TemplateId}: {decision.DiagnosticCode}; alvo={features.GetProperty("alvo").GetString()}; ato={features.GetProperty("ato").GetString()}; dimensão={features.GetProperty("dimensao_protegida").GetString()}. Reescreva respeitando o limite fornecido, sem expô-lo.";
    }
}
