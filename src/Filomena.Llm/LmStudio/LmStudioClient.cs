using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Filomena.Llm.LmStudio;

public sealed class LmStudioClient : ILmStudioChatClient, IModelResponseChatClient
{
    private static readonly object ResponseFormat = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "filomena_turn_decision",
            strict = true,
            schema = new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    decision = new { type = "string", @enum = new[] { "respond", "wait", "ignore" } },
                    anchorRef = new { type = new[] { "string", "null" } },
                    targetRef = new { type = new[] { "string", "null" } },
                    recheckAfterSeconds = new { type = new[] { "integer", "null" }, minimum = 1 },
                    responseKind = new { type = "string", @enum = new[] { "reply", "follow_up" } },
                    reason = new
                    {
                        type = "string",
                        @enum = new[]
                        {
                            "direct_address",
                            "structural_reply",
                            "useful_contribution",
                            "natural_social_opportunity",
                            "conversation_still_flowing",
                            "recently_participated",
                            "no_useful_contribution",
                            "thought_complete",
                            "thought_incomplete",
                            "waiting_for_continuation",
                            "incomplete_thought_follow_up",
                            "conversation_closed",
                        },
                    },
                },
                required = new[] { "decision", "anchorRef", "targetRef", "reason", "recheckAfterSeconds", "responseKind" },
            },
        },
    };

    private readonly HttpClient httpClient;
    private readonly LmStudioOptions options;

    public static JsonElement CreateArbitrationSchema(IEnumerable<string> messages, IEnumerable<string> actors)
    {
        var template = JsonSerializer.SerializeToNode(ResponseFormat)!["json_schema"]!["schema"]!;
        var branches = new JsonArray();
        foreach (var action in new[] { "respond", "wait", "ignore" })
        {
            var branch = template.DeepClone();
            var properties = branch["properties"]!;
            properties["decision"] = JsonSerializer.SerializeToNode(new { type = "string", @enum = new[] { action } });
            properties["anchorRef"] = action == "respond" ? ReferenceSchema(messages) : NullSchema();
            properties["targetRef"] = action == "respond" ? ReferenceSchema(actors) : NullSchema();
            properties["recheckAfterSeconds"] = action == "wait"
                ? JsonSerializer.SerializeToNode(new { type = "integer", minimum = 1 }) : NullSchema();
            if (action != "respond")
                properties["responseKind"] = JsonSerializer.SerializeToNode(new { type = "string", @enum = new[] { "reply" } });
            branches.Add(branch);
        }
        return JsonSerializer.SerializeToElement(new JsonObject { ["anyOf"] = branches });
    }

    private static JsonNode NullSchema() => JsonSerializer.SerializeToNode(new { type = "null" })!;
    private static JsonNode ReferenceSchema(IEnumerable<string> references) => JsonSerializer.SerializeToNode(new
    {
        type = new[] { "string", "null" },
        @enum = references.Cast<string?>().Append(null).ToArray(),
    })!;

    public Task<string> CompleteArbitrationJsonAsync(string systemPrompt, string userPrompt,
        JsonElement schema, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        return CompleteAsync(new
        {
            model = options.Model,
            temperature = options.ArbitrationTemperature,
            max_tokens = options.ArbitrationMaxTokens,
            messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userPrompt } },
            response_format = new { type = "json_schema", json_schema = new { name = "filomena_turn_decision", strict = true, schema } },
        }, cancellationToken);
    }

    public LmStudioClient(HttpClient httpClient, LmStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.httpClient = httpClient;
        // CompleteAsync owns the configurable timeout and shutdown cancellation.
        this.httpClient.Timeout = Timeout.InfiniteTimeSpan;
        this.options = options;
        this.httpClient.BaseAddress ??= EnsureTrailingSlash(options.Endpoint);
    }

    public async Task<string> CompleteArbitrationJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var payload = new
        {
            model = options.Model,
            temperature = options.ArbitrationTemperature,
            max_tokens = options.ArbitrationMaxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            response_format = ResponseFormat,
        };

        return await CompleteAsync(payload, cancellationToken);
    }

    public Task<string> CompleteResponseAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        => CompleteResponseAsync(systemPrompt, userPrompt, options.Model, cancellationToken);

    public Task<string> CompleteResponseAsync(
        string systemPrompt, string userPrompt, string model, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        return CompleteAsync(new
        {
            model,
            temperature = options.GenerationTemperature,
            max_tokens = options.GenerationMaxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        }, cancellationToken);
    }

    public Task<string> CompleteStructuredAsync(string systemPrompt, string userPrompt, string model, JsonElement schema, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return CompleteAsync(new
        {
            model,
            temperature = options.GenerationTemperature,
            max_tokens = options.GenerationMaxTokens,
            messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = userPrompt } },
            response_format = new { type = "json_schema", json_schema = new { name = "flow_output", strict = true, schema } },
        }, cancellationToken);
    }

    private async Task<string> CompleteAsync(object payload, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.RequestTimeoutSeconds != Timeout.Infinite) timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        using var response = await httpClient.PostAsJsonAsync(
            "chat/completions",
            payload,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(content.GetString()))
        {
            return content.GetString()!;
        }

        throw new InvalidDataException("LM Studio response did not contain assistant content.");
    }

    private static Uri EnsureTrailingSlash(Uri endpoint) =>
        endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);
}
