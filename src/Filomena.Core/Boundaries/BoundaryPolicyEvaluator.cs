using Filomena.Core.Flows;
using Filomena.Core.Memory;

namespace Filomena.Core.Boundaries;

public sealed record BoundaryPolicyDecision(bool Allow, string DiagnosticCode);

public static class BoundaryPolicyEvaluator
{
    public const string Version = "no_teasing_about_topic:evaluator-v1";

    public static BoundaryPolicyDecision Evaluate(Boundary? boundary, BoundarySemanticFeatures? features, DateTimeOffset now)
    {
        if (!ValidBoundary(boundary, now)) return Block("invalid_boundary");
        if (features is null || !Coherent(features)) return Block("invalid_semantic_features");
        if (features.ExpoeLimite) return Block("boundary_disclosure");
        if (features.Relevancia == "ambigua") return Block("ambiguous_relevance");
        if (features.Relevancia == "nenhuma") return new(true, "outside_boundary_scope");
        if (features.Alvo == "ambiguo" || features.Ato == "ambiguo" || features.DimensaoProtegida == "ambigua")
            return Block("ambiguous_semantics");
        if (features.Alvo is "sujeito" or "experiencia_do_sujeito" &&
            features.Ato is "provocacao" or "ridicularizacao" or "desqualificacao" or "minimizacao")
            return Block("protected_personal_depreciation");
        return new(true, "no_prohibited_act_identified");
    }

    public static bool Coherent(BoundarySemanticFeatures features)
    {
        try { FlowDomain.Check(features.ToJson(), BoundarySemanticFeatures.Contract); }
        catch (InvalidDataException) { return false; }
        var dimension = features.DimensaoProtegida;
        if (features.Relevancia == "nenhuma" && (dimension != "nenhuma" || features.Alvo == "experiencia_do_sujeito")) return false;
        if (dimension is not ("nenhuma" or "ambigua") && features.Alvo is not ("sujeito" or "experiencia_do_sujeito")) return false;
        return !(features.Relevancia == "presente" && features.Alvo == "experiencia_do_sujeito" && dimension == "nenhuma");
    }

    private static bool ValidBoundary(Boundary? boundary, DateTimeOffset now)
    {
        if (boundary is null || boundary.Kind != "no_teasing_about_topic" || boundary.Status != "compiled" ||
            boundary.Compilation.TemplateId != "no_teasing_about_topic:v1" || boundary.Compilation.TemplateVersion != "v1" ||
            boundary.Compilation.DisclosurePolicyVersion != "personal_boundary_disclosure:silent:v1") return false;
        var source = boundary.Source;
        if (source.ExpiredAt is not null || source.InvalidAt <= now || source.ValidAt > now) return false;
        try
        {
            return source.CanonicalSubject.ParticipantUuid == MemoryIdentity.Person(source.ConversationNamespace,
                source.CanonicalSubject.ActorStableId, source.CanonicalSubject.DisplayName).ParticipantUuid;
        }
        catch (InvalidDataException) { return false; }
    }

    private static BoundaryPolicyDecision Block(string diagnostic) => new(false, diagnostic);
}
