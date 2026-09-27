using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Filomena.Evaluation.Contracts;

public static class RevisionHasher
{
    public static string Hash(ConfigurationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Hash(profile.ToRevisionDocument());
    }

    public static string Hash(SummaryModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Hash(profile.ToRevisionDocument());
    }

    public static string Hash(RevisionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.ContractVersion != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(document), "Unsupported revision contract version.");
        }

        if (string.IsNullOrWhiteSpace(document.Kind) || document.Kind.Length > 128 ||
            document.Kind.Any(char.IsControl))
        {
            throw new ArgumentException("Revision kind is invalid.", nameof(document));
        }

        if (document.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("Revision payload is missing.", nameof(document));
        }

        ValidateJson(document.Payload, 0);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contractVersion", document.ContractVersion);
            writer.WriteString("kind", document.Kind);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, document.Payload);
            writer.WriteEndObject();
        }

        if (stream.Length > MaximumDocumentBytes)
        {
            throw new ArgumentException("Revision document exceeds the supported size.", nameof(document));
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    internal const int MaximumDocumentBytes = 1_048_576;

    internal static void ValidateJson(JsonElement element, int depth)
    {
        if (depth > 64)
        {
            throw new ArgumentException("Revision document exceeds the supported nesting depth.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new ArgumentException("Revision document contains a duplicate property.");
                    }

                    ValidateJson(property.Value, depth + 1);
                }

                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateJson(item, depth + 1);
                }

                break;
            case JsonValueKind.Number:
                _ = element.GetRawText();
                break;
            case JsonValueKind.String:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                throw new ArgumentException("Revision document contains an invalid JSON value.");
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentException("Revision document contains an invalid JSON value.");
        }
    }
}
