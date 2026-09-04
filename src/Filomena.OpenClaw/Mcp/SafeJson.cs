using System.Text.Json;

namespace Filomena.OpenClaw.Mcp;

public static class SafeJson
{
    private static readonly string[] SecretNameFragments =
    [
        "token",
        "secret",
        "password",
        "authorization",
        "credential",
        "cookie",
        "api_key",
        "apikey",
        "authheader",
    ];

    public static object? Sanitize(
        JsonElement value,
        int maxStringLength = 512,
        int maxArrayItems = 16,
        int maxObjectProperties = 64,
        int maxDepth = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStringLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArrayItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxObjectProperties);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);

        return SanitizeValue(
            value,
            depth: 0,
            maxStringLength,
            maxArrayItems,
            maxObjectProperties,
            maxDepth);
    }

    private static object? SanitizeValue(
        JsonElement value,
        int depth,
        int maxStringLength,
        int maxArrayItems,
        int maxObjectProperties,
        int maxDepth)
    {
        if (depth > maxDepth)
        {
            return "[max depth]";
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(
                value,
                depth,
                maxStringLength,
                maxArrayItems,
                maxObjectProperties,
                maxDepth),
            JsonValueKind.Array => SanitizeArray(
                value,
                depth,
                maxStringLength,
                maxArrayItems,
                maxObjectProperties,
                maxDepth),
            JsonValueKind.String => Bound(value.GetString() ?? string.Empty, maxStringLength),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => Bound(value.GetRawText(), maxStringLength),
        };
    }

    private static IReadOnlyDictionary<string, object?> SanitizeObject(
        JsonElement value,
        int depth,
        int maxStringLength,
        int maxArrayItems,
        int maxObjectProperties,
        int maxDepth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var properties = value.EnumerateObject().ToArray();
        foreach (var property in properties.Take(maxObjectProperties))
        {
            result[property.Name] = IsSecretName(property.Name)
                ? "[REDACTED]"
                : SanitizeValue(
                    property.Value,
                    depth + 1,
                    maxStringLength,
                    maxArrayItems,
                    maxObjectProperties,
                    maxDepth);
        }

        if (properties.Length > maxObjectProperties)
        {
            result["__truncated_properties"] = properties.Length - maxObjectProperties;
        }

        return result;
    }

    private static IReadOnlyList<object?> SanitizeArray(
        JsonElement value,
        int depth,
        int maxStringLength,
        int maxArrayItems,
        int maxObjectProperties,
        int maxDepth)
    {
        var items = value.EnumerateArray().ToArray();
        var result = items
            .Take(maxArrayItems)
            .Select(item => SanitizeValue(
                item,
                depth + 1,
                maxStringLength,
                maxArrayItems,
                maxObjectProperties,
                maxDepth))
            .ToList();
        if (items.Length > maxArrayItems)
        {
            result.Add($"[{items.Length - maxArrayItems} more items]");
        }

        return result;
    }

    private static bool IsSecretName(string name) => SecretNameFragments.Any(fragment =>
        name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string Bound(string value, int maxLength) => value.Length <= maxLength
        ? value
        : $"{value[..maxLength]}[truncated]";
}
