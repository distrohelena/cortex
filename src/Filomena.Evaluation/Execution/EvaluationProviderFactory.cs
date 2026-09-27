using Filomena.Core.Flows;
using Filomena.Evaluation.Contracts;
using Filomena.Llm.OpenAi;
using Filomena.Llm.LmStudio;
using Filomena.Llm.SystemOne;
using System.Text.Json;

namespace Filomena.Evaluation.Execution;

public sealed record EvaluationEndpoint(string Alias, string Provider, Uri Endpoint, HttpClient Http, Func<string?> Credential);

/// <summary>Host-only bindings; a candidate can select an alias, never credentials or a URL.
/// Clients are dedicated to evaluation and must carry the durable accounting handler before live use.</summary>
public sealed class EvaluationProviderFactory
{
    private readonly IReadOnlyDictionary<string, EvaluationEndpoint> endpoints;

    public EvaluationProviderFactory(IEnumerable<EvaluationEndpoint> endpoints)
    {
        this.endpoints = endpoints.ToDictionary(endpoint => endpoint.Alias, StringComparer.Ordinal);
        foreach (var endpoint in this.endpoints.Values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Alias);
            ArgumentNullException.ThrowIfNull(endpoint.Http);
            ArgumentNullException.ThrowIfNull(endpoint.Credential);
            var uri = endpoint.Endpoint;
            if (!uri.IsAbsoluteUri || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
                endpoint.Provider switch
                {
                    "openai" => uri.AbsoluteUri != "https://api.openai.com/v1/responses",
                    "jev" => uri.AbsoluteUri != "https://api.typesafe.ai/v1/systemone",
                    "lmstudio" or "laya" => !uri.IsLoopback || uri.Scheme is not ("http" or "https"),
                    _ => true
                })
                throw new InvalidDataException("Invalid host evaluation endpoint.");
        }
    }

    public IFlowCompletion Create(ConfigurationProfile profile, EvaluationAccess access)
    {
        access.Demand(EvaluationOperation.Evaluate);
        if (ProfileValidator.Validate(profile).Count != 0)
            throw new InvalidDataException("Invalid evaluation configuration profile.");
        var prompt = ReadPrompt(profile.Prompt);
        var schema = ReadSchema(profile.Schema);
        var primary = CreateAdapter(profile.Provider, profile.Model, profile.EndpointAlias, profile.Parameters,
            profile.Threshold, profile.ChoiceQuestions);
        if (profile.SummaryProfile is { } summary)
        {
            var summaryPrompt = ReadPrompt(summary.Prompt);
            var summarySchema = ReadSchema(summary.Schema);
            if (summarySchema is null) throw new InvalidDataException("A summary schema is required.");
            var summaryAdapter = CreateAdapter(summary.Provider, summary.Model, summary.EndpointAlias,
                summary.Parameters, null, []);
            primary = new CompositeCompletion(primary,
                new SummaryCompletion(summaryAdapter, summary, summaryPrompt, summarySchema.Value), profile);
        }
        return new PinnedCompletion(primary, profile, prompt, schema);
    }

    private IFlowCompletion CreateAdapter(string provider, string model, string alias, JsonElement parameters,
        double? threshold, IReadOnlyList<ChoiceQuestionProfile> questions)
    {
        if (!endpoints.TryGetValue(alias, out var endpoint) || endpoint.Provider != provider)
            throw new InvalidDataException("Evaluation endpoint alias is not authorized for this provider.");
        try
        {
            // No implicit adapter defaults: the complete effective settings belong to the revision.
            return provider switch
            {
                "openai" => new OpenAiFlowCompletion(endpoint.Http, endpoint.Credential,
                    parameters.GetProperty("reasoningEffort").GetString()!, parameters.GetProperty("maxOutputTokens").GetInt32()),
                "lmstudio" => new FlowModelCompletion(new LmStudioClient(endpoint.Http, new LmStudioOptions
                {
                    Endpoint = endpoint.Endpoint,
                    Model = model,
                    ArbitrationTemperature = parameters.GetProperty("arbitrationTemperature").GetDouble(),
                    ArbitrationMaxTokens = parameters.GetProperty("arbitrationMaxTokens").GetInt32(),
                    GenerationTemperature = parameters.GetProperty("generationTemperature").GetDouble(),
                    GenerationMaxTokens = parameters.GetProperty("generationMaxTokens").GetInt32(),
                    RequestTimeoutSeconds = parameters.GetProperty("requestTimeoutSeconds").GetInt32()
                }), model),
                "jev" or "laya" => new SystemOneFlowCompletion(new SystemOneClient(endpoint.Http,
                    endpoint.Endpoint, endpoint.Credential, provider == "jev", provider == "jev"
                        ? SystemOneProbabilityPolicy.Jev113TwoDecimals : SystemOneProbabilityPolicy.LayaFourDecimals),
                    provider, threshold!.Value, choiceQuestions: questions.ToDictionary(q => q.Field,
                        q => new SystemOneChoiceQuestion(q.Instructions, q.Criteria), StringComparer.Ordinal)),
                _ => throw new InvalidDataException("Unsupported evaluation provider.")
            };
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Incomplete evaluation provider settings.");
        }
    }

    internal static string ReadPrompt(RevisionDocument document)
    {
        if (document.Payload.ValueKind != JsonValueKind.Object ||
            !document.Payload.TryGetProperty("system", out var system) || system.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(system.GetString()))
            throw new InvalidDataException("Evaluation prompt requires a system instruction.");
        return system.GetString()!;
    }

    internal static JsonElement? ReadSchema(RevisionDocument document)
    {
        if (!document.Payload.TryGetProperty("responseSchema", out var schema) ||
            schema.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            throw new InvalidDataException("Evaluation schema requires an explicit responseSchema (object or null).");
        return schema.ValueKind == JsonValueKind.Null ? null : schema.Clone();
    }

    private sealed class PinnedCompletion(IFlowCompletion inner, ConfigurationProfile profile,
        string prompt, JsonElement? schema) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var effort = profile.Parameters.TryGetProperty("reasoningEffort", out var value) ? value.GetString() : null;
            int? maxTokens = profile.Parameters.TryGetProperty("maxOutputTokens", out value) ? value.GetInt32() : null;
            if (request.Provider != profile.Provider || request.Model != profile.Model || request.System != prompt ||
                !SameSchema(request.ResponseSchema, schema) ||
                request.ReasoningEffort is not null && request.ReasoningEffort != effort ||
                request.MaxOutputTokens is not null && request.MaxOutputTokens != maxTokens)
                throw new InvalidDataException("Request differs from the pinned evaluation profile.");
            return inner.CompleteAsync(request with { ReasoningEffort = effort, MaxOutputTokens = maxTokens }, token);
        }
    }

    private sealed class SummaryCompletion(IFlowCompletion inner, SummaryModelProfile profile,
        string prompt, JsonElement schema) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            if (!SameSchema(request.ResponseSchema, schema))
                throw new InvalidDataException("Derived summary contract differs from its pinned profile.");
            return inner.CompleteAsync(request with
            {
                Provider = profile.Provider,
                Model = profile.Model,
                System = prompt,
                ReasoningEffort = profile.Parameters.TryGetProperty("reasoningEffort", out var effort) ? effort.GetString() : null,
                MaxOutputTokens = profile.Parameters.TryGetProperty("maxOutputTokens", out var count) ? count.GetInt32() : null
            }, token);
        }
    }

    private sealed class CompositeCompletion(IFlowCompletion typed, IFlowCompletion summary,
        ConfigurationProfile profile) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            new SystemOneParticipationCompletion(typed, summary, profile.Provider, profile.Model,
                new HashSet<string>(StringComparer.Ordinal) { request.NodeId }, new HashSet<string>(StringComparer.Ordinal))
                .CompleteAsync(request, token);
    }

    private static bool SameSchema(JsonElement? first, JsonElement? second) =>
        first is null ? second is null : second is { } value && JsonElement.DeepEquals(first.Value, value);
}
