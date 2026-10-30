using Cortex.Core.Flows;
using Cortex.Inference.SystemOne;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cortex.Inference.OpenJev;

public enum OpenJevOptionOrder { Fixed, AverageRotations }

/// <summary>
/// Enum/boolean-only flow completion over OpenJev's per-question method. One decision per field, in schema order,
/// returned in Jev's answer shape so it is observed like any System 1 provider. Free text is deliberately not emulated.
/// </summary>
public sealed class OpenJevFlowCompletion(OpenJevClient client, double booleanThreshold,
    Action<string, SystemOneResult>? observe = null,
    IReadOnlyDictionary<string, SystemOneChoiceQuestion>? choiceQuestions = null,
    Action<string, string, OpenJevDecision>? observeDecision = null,
    OpenJevOptionOrder optionOrder = OpenJevOptionOrder.Fixed) : IFlowCompletion
{
    private const int MaximumOptions = 20;
    // OpenJev passes state untouched: no \u escapes for non-ASCII, quotes or HTML-sensitive characters.
    private static readonly JsonSerializerOptions StateJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly Dictionary<string, SystemOneChoiceQuestion> profiles = choiceQuestions?.ToDictionary(p => p.Key,
        p => new SystemOneChoiceQuestion(p.Value.Instructions, p.Value.Criteria.ToDictionary()), StringComparer.Ordinal) ?? [];

    public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
    {
        if (request.Provider != "openjev" || string.IsNullOrWhiteSpace(request.Model) ||
            !double.IsFinite(booleanThreshold) || booleanThreshold is <= 0 or >= 1)
            throw new InvalidDataException("OpenJev requires the openjev provider, a model and a boolean threshold in (0,1).");
        if (request.Inputs.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("OpenJev requires a state.");
        var questions = TypedDecisionSchema.Questions(request, profiles, MaximumOptions);
        var state = request.Inputs.ValueKind == JsonValueKind.String ? request.Inputs.GetString()! : JsonSerializer.Serialize(request.Inputs, StateJson);
        var values = new JsonObject();
        var answers = new JsonObject();
        string? model = null;
        long inputTokens = 0, outputTokens = 0;
        var elapsed = TimeSpan.Zero;
        foreach (var field in questions.EnumerateObject())
        {
            var definition = field.Value;
            var instructions = definition.GetProperty("instructions");
            // Task, then the field's own description (or its name when it has none). No "Field:" header: it measurably
            // shifts small models' answers away from what the task alone yields.
            var description = instructions.GetProperty("description").GetString();
            var question = $"{request.System}\n\n{(string.IsNullOrWhiteSpace(description) ? field.Name : description)}";
            if (instructions.TryGetProperty("choiceGuidance", out var guidance)) question += "\n\n" + guidance.GetString();
            var choice = definition.GetProperty("type").GetString() == "choice";
            var keys = choice ? definition.GetProperty("criteria").EnumerateObject().Select(p => p.Name).ToArray() : [];
            // Booleans are asked as A. Yes / B. No; noul stays P(A).
            var options = choice
                ? definition.GetProperty("criteria").EnumerateObject()
                    .Select(p => p.Value.GetString() is var text && text != p.Name ? $"{p.Name}: {text}" : p.Name).ToArray()
                : ["Yes", "No"];
            var decision = optionOrder == OpenJevOptionOrder.AverageRotations
                ? await AverageRotationsAsync(request, state, question, options, inputTokens, outputTokens, token)
                : await client.DecideAsync(request.Model, state, question, options, request.Diagnostics, token, true, inputTokens, outputTokens);
            inputTokens += decision.InputTokens;
            outputTokens += decision.OutputTokens;
            elapsed += decision.Elapsed;
            model ??= decision.Model;
            observeDecision?.Invoke(request.NodeId, field.Name, decision);
            if (choice)
            {
                var probabilities = new JsonObject();
                for (var i = 0; i < keys.Length; i++) probabilities[keys[i]] = decision.Options[i].Probability;
                // First maximum in enum order: deterministic and never below another option.
                var selected = 0;
                for (var i = 1; i < keys.Length; i++) if (decision.Options[i].Probability > decision.Options[selected].Probability) selected = i;
                answers[field.Name] = new JsonObject { ["type"] = "choice", ["choice"] = keys[selected], ["probabilities"] = probabilities,
                    ["confidence"] = decision.Options[selected].Probability };
                values[field.Name] = keys[selected];
            }
            else
            {
                var probability = decision.Options[0].Probability;
                answers[field.Name] = new JsonObject { ["type"] = "noul", ["noul"] = probability };
                values[field.Name] = probability >= booleanThreshold;
            }
        }
        var result = new SystemOneResult(model!, JsonSerializer.SerializeToElement(answers), checked((int)inputTokens), checked((int)outputTokens), elapsed)
            { ProbabilityPolicy = optionOrder == OpenJevOptionOrder.AverageRotations ? "OpenJevLabelSoftmaxRotationAverage" : "OpenJevLabelSoftmax" };
        observe?.Invoke(request.NodeId, result);
        return values.ToJsonString();
    }

    /// <summary>
    /// Asks every cyclic rotation of the options (labels stay A, B, ...) and averages each original option's probability,
    /// cancelling small models' label-position bias at n calls per question. The synthesized decision lists the options in
    /// their original order with summed usage and elapsed time.
    /// </summary>
    private async Task<OpenJevDecision> AverageRotationsAsync(FlowCompletionRequest request, string state, string question,
        string[] options, long priorInputTokens, long priorOutputTokens, CancellationToken token)
    {
        var n = options.Length;
        var sums = new double[n];
        string? model = null;
        int inputTokens = 0, outputTokens = 0;
        var elapsed = TimeSpan.Zero;
        for (var shift = 0; shift < n; shift++)
        {
            var rotated = new string[n];
            for (var i = 0; i < n; i++) rotated[i] = options[(i + shift) % n];
            var decision = await client.DecideAsync(request.Model!, state, question, rotated, request.Diagnostics, token, true,
                priorInputTokens + inputTokens, priorOutputTokens + outputTokens);
            // Label i carried original option (i + shift) mod n.
            for (var i = 0; i < n; i++) sums[(i + shift) % n] += decision.Options[i].Probability;
            inputTokens = checked(inputTokens + decision.InputTokens);
            outputTokens = checked(outputTokens + decision.OutputTokens);
            elapsed += decision.Elapsed;
            model ??= decision.Model;
        }
        var averaged = new OpenJevOption[n];
        for (var i = 0; i < n; i++)
        {
            var mean = sums[i] / n;
            averaged[i] = new(((char)('A' + i)).ToString(), options[i], Math.Log(mean), mean);
        }
        return new(model!, averaged, inputTokens, outputTokens, elapsed);
    }
}
