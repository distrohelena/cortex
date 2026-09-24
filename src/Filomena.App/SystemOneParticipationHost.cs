using Filomena.Core.Flows;
using Filomena.Core.Shadow;
using Filomena.Llm.SystemOne;

namespace Filomena.App;

// Host-owned and pinned with the run; never obtained from model output or project content.
public sealed record SystemOneParticipationConfig(string Version, string Model, string[] SituationNodeIds,
    string[] PresenceNodeIds, Dictionary<string, SystemOneChoiceQuestion> ChoiceQuestions, string SummaryField = "resumo");

public static class SystemOneParticipationHost
{
    public static IFlowCompletion Create(SystemOneParticipationConfig? config, PinnedShadowProject pin,
        IFlowCompletion baseline, HttpClient http, Func<string, string?> environment,
        Action<string, SystemOneResult>? observe = null)
    {
        if (config is null) return baseline;
        Validate(config, pin);
        var key = environment("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("systemone_credential_unavailable");
        var typed = new SystemOneFlowCompletion(new(http, new("https://api.typesafe.ai/v1/systemone"), () => key, true,
            SystemOneProbabilityPolicy.Jev113TwoDecimals), "jev", 0.5, observe, config.ChoiceQuestions);
        return new SystemOneParticipationCompletion(typed, baseline, "jev", config.Model,
            config.SituationNodeIds.ToHashSet(StringComparer.Ordinal), config.PresenceNodeIds.ToHashSet(StringComparer.Ordinal), config.SummaryField);
    }

    public static void Validate(SystemOneParticipationConfig? config, PinnedShadowProject pin)
    {
        if (config is null) return;
        if (config.Version != "systemone-participation:v1" || config.Model != "jev-1.13.0" ||
            config.SituationNodeIds is not { Length: > 0 } || config.PresenceNodeIds is not { Length: > 0 } ||
            config.ChoiceQuestions is null || string.IsNullOrWhiteSpace(config.SummaryField)) throw Invalid();
        var ids = config.SituationNodeIds.Concat(config.PresenceNodeIds).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw Invalid();
        var nodes = pin.Project.Document.GetProperty("states").EnumerateArray()
            .SelectMany(s => s.GetProperty("graph").GetProperty("nodes").EnumerateArray()).ToArray();
        foreach (var (field, question) in config.ChoiceQuestions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Instructions) || question.Criteria is null ||
                question.Criteria.Any(p => string.IsNullOrWhiteSpace(p.Value))) throw Invalid();
            var matches = nodes.Where(n => ids.Contains(n.GetProperty("id").GetString(), StringComparer.Ordinal))
                .Where(n => n.TryGetProperty("responseFields", out _))
                .SelectMany(n => n.GetProperty("responseFields").EnumerateArray())
                .Where(f => f.GetProperty("name").GetString() == field).ToArray();
            if (matches.Length == 0 || matches.Any(f => f.GetProperty("type").GetString() != "enum" ||
                !question.Criteria.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(f.GetProperty("values").EnumerateArray().Select(v => v.GetString() ?? throw Invalid())))) throw Invalid();
        }
        foreach (var id in ids)
        {
            var matches = nodes.Where(n => n.GetProperty("id").GetString() == id).ToArray();
            if (matches.Length == 0) throw Invalid();
            foreach (var node in matches)
            {
                if (node.GetProperty("kind").GetString() != "prompt" || !node.TryGetProperty("responseFields", out var fields)) throw Invalid();
                var definitions = fields.EnumerateArray().ToArray();
                var summary = definitions.Where(f => f.GetProperty("name").GetString() == config.SummaryField).ToArray();
                if (config.SituationNodeIds.Contains(id, StringComparer.Ordinal))
                {
                    if (summary.Length != 1 || summary[0].GetProperty("type").GetString() != "text") throw Invalid();
                    definitions = definitions.Except(summary).ToArray();
                }
                if (definitions.Length == 0 || definitions.Any(f => f.GetProperty("type").GetString() is not ("enum" or "boolean"))) throw Invalid();
            }
        }
    }

    private static InvalidDataException Invalid() => new("invalid_systemone_participation_binding");
}
