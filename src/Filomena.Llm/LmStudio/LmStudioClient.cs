using System.Net.Http.Json;
using System.Text.Json;

namespace Filomena.Llm.LmStudio;

public sealed class LmStudioClient : ILmStudioChatClient, IResponseChatClient
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
                        },
                    },
                },
                required = new[] { "decision", "anchorRef", "targetRef", "reason" },
            },
        },
    };

    private readonly HttpClient httpClient;
    private readonly LmStudioOptions options;

    public LmStudioClient(HttpClient httpClient, LmStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.httpClient = httpClient;
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        return CompleteAsync(new
        {
            model = options.Model,
            temperature = options.GenerationTemperature,
            max_tokens = options.GenerationMaxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        }, cancellationToken);
    }

    private async Task<string> CompleteAsync(object payload, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
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
