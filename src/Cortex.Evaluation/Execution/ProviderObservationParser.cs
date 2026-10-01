using System.Text.Json;

namespace Cortex.Evaluation.Execution;

public sealed record ProviderTokenUsage(long? InputTokens, long? OutputTokens,
    long? CachedInputTokens, long? ReasoningTokens);

public sealed record ProviderObservation(int HttpStatus, string? Model, string? ResponseId,
    string? ProviderStatus, string UsageStatus, ProviderTokenUsage? Usage, string? Diagnostic)
{
    public bool HttpSucceeded => HttpStatus is >= 200 and < 300;
    public string? ServiceTier { get; init; }
}

public static class ProviderObservationParser
{
    // Usage is observation, not a completion or billing guarantee. Never retain output/reasoning/error text.
    public static ProviderObservation Parse(string provider, int statusCode, ReadOnlyMemory<byte> body)
    {
        ProviderObservation result = new(statusCode, null, null, null, "unavailable", null, null);
        if (provider is not ("openai" or "lmstudio" or "jev" or "laya"))
            return result with { Diagnostic = "provider_unsupported" };
        if (body.Length > 1_048_576) return result with { Diagnostic = "body_too_large" };
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result with { Diagnostic = "response_invalid" };
            if (!UniqueProperties(root)) return result with { Diagnostic = "response_ambiguous" };
            result = result with
            {
                Model = OptionalIdentifier(root, "model"),
                ResponseId = OptionalIdentifier(root, "id"),
                ProviderStatus = OptionalIdentifier(root, "status"),
                ServiceTier = OptionalIdentifier(root, "service_tier")
            };
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
                return result with { Diagnostic = "usage_missing" };
            if (usage.ValueKind != JsonValueKind.Object) return result with { Diagnostic = "usage_invalid" };

            var chat = provider == "lmstudio";
            var input = Counter(usage, chat ? "prompt_tokens" : "input_tokens");
            var output = Counter(usage, chat ? "completion_tokens" : "output_tokens");
            var cached = DetailCounter(usage, chat ? "prompt_tokens_details" : "input_tokens_details", "cached_tokens");
            var reasoning = DetailCounter(usage, chat ? "completion_tokens_details" : "output_tokens_details", "reasoning_tokens");
            if (cached > input || reasoning > output) return result with { Diagnostic = "usage_invalid" };
            if (input is null && output is null && cached is null && reasoning is null)
                return result with { Diagnostic = "usage_missing" };
            return result with
            {
                Usage = new ProviderTokenUsage(input, output, cached, reasoning),
                UsageStatus = input is not null && output is not null ? "available" : "partial"
            };
        }
        catch (JsonException) { return result with { Diagnostic = "response_invalid" }; }
        catch (InvalidDataException) { return result with { Diagnostic = "usage_invalid" }; }
    }

    private static long? Counter(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count) || count < 0)
            throw new InvalidDataException("Invalid usage counter.");
        return count;
    }

    private static long? DetailCounter(JsonElement parent, string name, string field)
    {
        if (!parent.TryGetProperty(name, out var details) || details.ValueKind == JsonValueKind.Null) return null;
        if (details.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid usage details.");
        return Counter(details, field);
    }

    private static string? OptionalIdentifier(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String) return null;
        var text = field.GetString();
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 256 && !text.Any(char.IsControl) ? text : null;
    }

    private static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueProperties(property.Value)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) if (!UniqueProperties(item)) return false;
        }
        return true;
    }
}
