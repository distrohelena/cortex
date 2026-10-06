using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cortex.Video.Editing;

public static class EditingJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        Converters = { new EditingEnumConverterFactory() }
    };

    public static EditingDocument Parse(JsonElement value)
    {
        try
        {
            RejectDuplicates(value);
            var type = value.GetProperty("schema").GetString() switch
            {
                EditingDocumentKinds.Script => typeof(EditScriptDocument),
                EditingDocumentKinds.Plan => typeof(EditPlanDocument),
                EditingDocumentKinds.Tasks => typeof(RecordingTasksDocument),
                EditingDocumentKinds.Resolved => typeof(ResolvedEditPlanDocument),
                _ => throw new InvalidDataException("Unsupported editorial document schema.")
            };
            return (EditingDocument)(value.Deserialize(type, Options) ?? throw new InvalidDataException("Editorial document is empty."));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidDataException("Invalid editorial document: " + error.Message, error); }
    }

    public static JsonElement Serialize(EditingDocument document) => JsonSerializer.SerializeToElement(document, document.GetType(), Options);

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}

internal sealed class EditingEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(EditingEnumConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class EditingEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> Codes = Enum.GetValues<T>().ToDictionary(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), StringComparer.Ordinal);
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !Codes.TryGetValue(reader.GetString()!, out var value)) throw new JsonException("Invalid " + typeof(T).Name + " code.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var code = Codes.FirstOrDefault(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Key;
        if (code is null) throw new JsonException("Invalid " + typeof(T).Name + " value.");
        writer.WriteStringValue(code);
    }
}
