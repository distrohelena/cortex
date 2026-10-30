using Cortex.Core.Flows;
using System.Text.Json.Nodes;

namespace Cortex.Inference.SystemOne;

public sealed record SystemOneChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Criteria);

/// <summary>Enum/boolean-only flow completion. Free text is deliberately not emulated.</summary>
public sealed class SystemOneFlowCompletion(SystemOneClient client, string provider, double booleanThreshold,
    Action<string, SystemOneResult>? observe = null,
    IReadOnlyDictionary<string, SystemOneChoiceQuestion>? choiceQuestions = null) : IFlowCompletion
{
    private readonly Dictionary<string, SystemOneChoiceQuestion> profiles = choiceQuestions?.ToDictionary(p => p.Key,
        p => new SystemOneChoiceQuestion(p.Value.Instructions, p.Value.Criteria.ToDictionary()), StringComparer.Ordinal) ?? [];

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (provider is not ("jev" or "laya") || request.Provider != provider || string.IsNullOrWhiteSpace(request.Model) ||
            !double.IsFinite(booleanThreshold) || booleanThreshold is <= 0 or >= 1)
            throw new InvalidDataException("System 1 requires an explicit provider, model and boolean threshold in (0,1).");
        if (provider == "laya" && request.Model is not ("english" or "multilingual" or "typed-decisions" or
            "convaiinnovations/laya" or "convaiinnovations/laya-multilingual" or "convaiinnovations/laya-typed-decisions"))
            throw new InvalidDataException("Unsupported Laya model; implicit auto-routing is forbidden.");
        var questions = TypedDecisionSchema.Questions(request, profiles, maximumOptions: 255);
        // Laya 0.4.18 treats the bundle id as "auto-route by detected language", not as a checkpoint. Cortex means its
        // English checkpoint by it, so the wire carries the canonical name. The other two published ids route explicitly.
        var model = provider == "laya" && request.Model == "convaiinnovations/laya" ? "english" : request.Model;
        var result = request.Diagnostics is { } diagnostics
            ? await client.DecideAsync(model, request.Inputs, questions, diagnostics, token)
            : await client.DecideAsync(model, request.Inputs, questions, token);
        var output = new JsonObject();
        foreach (var question in questions.EnumerateObject())
        {
            var answer = result.Answers.GetProperty(question.Name);
            output[question.Name] = question.Value.GetProperty("type").GetString() == "choice"
                ? JsonValue.Create(answer.GetProperty("choice").GetString())
                : JsonValue.Create(answer.GetProperty("noul").GetDouble() >= booleanThreshold);
        }
        observe?.Invoke(request.NodeId, result);
        return output.ToJsonString();
    }
}
