using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Filomena.Core.Flows;

public abstract record FlowType
{
    public abstract string Kind { get; }

    public JsonElement ToJsonSchema() => FlowSchemaCodec.ToJsonSchema(this);

    public bool IsCompatibleWith(FlowType target) => FlowTypeCompatibility.IsCompatible(this, target);

    public static bool AreCompatible(FlowType source, FlowType target) => source.IsCompatibleWith(target);

    public static FlowType ParseJsonSchema(JsonElement schema) => FlowSchemaCodec.ParseJsonSchema(schema);

    public static FlowType ParsePortable(JsonElement type) => FlowSchemaCodec.ParsePortable(type);
}

public sealed record StringType : FlowType
{
    public override string Kind => "string";
}

public sealed record NumberType : FlowType
{
    public override string Kind => "number";
}

public sealed record IntegerType : FlowType
{
    public override string Kind => "integer";
}

public sealed record BooleanType : FlowType
{
    public override string Kind => "boolean";
}

public sealed record EnumType : FlowType
{
    public EnumType(IEnumerable<string> values)
    {
        var copied = values.ToArray();
        if (copied.Length == 0 || copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new FlowSchemaException(new FlowIssue("schema.enum_invalid", "/enum", "Enum values must be non-empty and unique."));
        }

        Values = copied;
    }

    public IReadOnlyList<string> Values { get; }
    public override string Kind => "enum";
}

public sealed record FlowField(FlowType Type, bool Required);

public sealed record ObjectType : FlowType
{
    public ObjectType(IReadOnlyDictionary<string, FlowField> fields)
    {
        var copied = new Dictionary<string, FlowField>(StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                throw new FlowSchemaException(new FlowIssue("schema.field_invalid", "/fields", "Field names must not be empty."));
            }

            copied.Add(pair.Key, pair.Value);
        }

        Fields = new ReadOnlyDictionary<string, FlowField>(copied);
    }

    public IReadOnlyDictionary<string, FlowField> Fields { get; }
    public override string Kind => "object";
}

public sealed record ArrayType : FlowType
{
    public ArrayType(FlowType elementType) => ElementType = elementType;

    public FlowType ElementType { get; }
    public override string Kind => "array";
}

public sealed record NullableType : FlowType
{
    public NullableType(FlowType innerType) => InnerType = innerType;

    public FlowType InnerType { get; }
    public override string Kind => "nullable";
}

public sealed class FlowSchemaException : Exception
{
    public FlowSchemaException(FlowIssue issue)
        : base(issue.Message)
    {
        Issue = issue;
    }

    public FlowIssue Issue { get; }
}

internal static class FlowTypeCompatibility
{
    public static bool IsCompatible(FlowType source, FlowType target)
    {
        if (source is NullableType nullableSource)
        {
            return target is NullableType nullableTarget
                ? IsCompatible(nullableSource.InnerType, nullableTarget.InnerType)
                : false;
        }

        if (target is NullableType nullableTargetOnly)
        {
            return IsCompatible(source, nullableTargetOnly.InnerType);
        }

        return (source, target) switch
        {
            (StringType, StringType) => true,
            (IntegerType, IntegerType) => true,
            (IntegerType, NumberType) => true,
            (NumberType, NumberType) => true,
            (BooleanType, BooleanType) => true,
            (EnumType sourceEnum, EnumType targetEnum) => sourceEnum.Values.All(targetEnum.Values.Contains),
            (EnumType, StringType) => true,
            (ArrayType sourceArray, ArrayType targetArray) => IsCompatible(sourceArray.ElementType, targetArray.ElementType),
            (ObjectType sourceObject, ObjectType targetObject) => CompatibleObjects(sourceObject, targetObject),
            _ => false,
        };
    }

    private static bool CompatibleObjects(ObjectType source, ObjectType target)
    {
        foreach (var (name, targetField) in target.Fields)
        {
            if (!source.Fields.TryGetValue(name, out var sourceField))
            {
                return false;
            }

            if (targetField.Required && !sourceField.Required)
            {
                return false;
            }

            if (!IsCompatible(sourceField.Type, targetField.Type))
            {
                return false;
            }
        }

        return true;
    }
}

internal static class FlowSchemaCodec
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    public static JsonElement ToJsonSchema(FlowType type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteSchema(writer, type, 0, new SchemaBudget());
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static FlowType ParseJsonSchema(JsonElement schema)
    {
        EnsureNoDuplicateProperties(schema);
        return ParseSchema(schema, 0, new SchemaBudget(), "/");
    }

    public static FlowType ParsePortable(JsonElement type)
    {
        EnsureNoDuplicateProperties(type);
        return ParsePortableType(type, 0, new SchemaBudget(), "/");
    }

    private static void WriteSchema(Utf8JsonWriter writer, FlowType type, int depth, SchemaBudget budget)
    {
        Enter(type, depth, budget, "/");
        switch (type)
        {
            case NullableType nullable:
                writer.WriteStartObject();
                writer.WritePropertyName("type");
                writer.WriteStartArray();
                writer.WriteStringValue(GetJsonType(nullable.InnerType));
                writer.WriteStringValue("null");
                writer.WriteEndArray();
                WriteComplexMembers(writer, nullable.InnerType, depth, budget);
                writer.WriteEndObject();
                return;
            case StringType:
                writer.WriteStartObject();
                writer.WriteString("type", "string");
                writer.WriteEndObject();
                return;
            case EnumType enumType:
                writer.WriteStartObject();
                writer.WriteString("type", "string");
                writer.WritePropertyName("enum");
                writer.WriteStartArray();
                foreach (var value in enumType.Values)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            case NumberType:
                writer.WriteStartObject();
                writer.WriteString("type", "number");
                writer.WriteEndObject();
                return;
            case IntegerType:
                writer.WriteStartObject();
                writer.WriteString("type", "integer");
                writer.WriteEndObject();
                return;
            case BooleanType:
                writer.WriteStartObject();
                writer.WriteString("type", "boolean");
                writer.WriteEndObject();
                return;
            case ArrayType array:
                writer.WriteStartObject();
                writer.WriteString("type", "array");
                writer.WritePropertyName("items");
                WriteSchema(writer, array.ElementType, depth + 1, budget);
                writer.WriteEndObject();
                return;
            case ObjectType objectType:
                writer.WriteStartObject();
                writer.WriteString("type", "object");
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var (name, field) in objectType.Fields)
                {
                    budget.AddField();
                    writer.WritePropertyName(name);
                    WriteSchema(writer, field.Type, depth + 1, budget);
                }

                writer.WriteEndObject();
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                foreach (var (name, field) in objectType.Fields.Where(pair => pair.Value.Required))
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                writer.WriteBoolean("additionalProperties", false);
                writer.WriteEndObject();
                return;
            default:
                throw Unsupported("/", "Type kind is not supported.");
        }
    }

    private static void WriteComplexMembers(Utf8JsonWriter writer, FlowType innerType, int depth, SchemaBudget budget)
    {
        if (innerType is ObjectType objectType)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var (name, field) in objectType.Fields)
            {
                budget.AddField();
                writer.WritePropertyName(name);
                WriteSchema(writer, field.Type, depth + 1, budget);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var (name, field) in objectType.Fields.Where(pair => pair.Value.Required))
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
        }
        else if (innerType is ArrayType array)
        {
            writer.WritePropertyName("items");
            WriteSchema(writer, array.ElementType, depth + 1, budget);
        }
        else if (innerType is EnumType enumType)
        {
            writer.WritePropertyName("enum");
            writer.WriteStartArray();
            foreach (var value in enumType.Values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }
    }

    private static FlowType ParseSchema(JsonElement schema, int depth, SchemaBudget budget, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(path, "Schema must be an object.");
        }

        Enter(schema, depth, budget, path);
        var typeElement = Required(schema, "type", path);
        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            var typeNames = ParseStringArray(typeElement, Join(path, "type"));
            if (typeNames.Length != 2 || typeNames.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !typeNames.Contains("null", StringComparer.Ordinal))
            {
                throw Unsupported(Join(path, "type"), "Nullable schemas must contain exactly one supported type and null.");
            }

            var innerName = typeNames.Single(name => name != "null");
            return new NullableType(ParseSchemaBody(schema, innerName, depth, budget, path));
        }

        return ParseSchemaBody(schema, RequiredString(schema, "type", path), depth, budget, path);
    }

    private static FlowType ParseSchemaBody(JsonElement schema, string type, int depth, SchemaBudget budget, string path)
    {
        var properties = GetAllowedProperties(schema, path);
        if (type == "string")
        {
            EnsureOnly(properties, path, "type", "enum");
            return schema.TryGetProperty("enum", out var enumElement)
                ? new EnumType(ParseStringArray(enumElement, Join(path, "enum")))
                : new StringType();
        }

        if (type == "number")
        {
            EnsureOnly(properties, path, "type");
            return new NumberType();
        }

        if (type == "integer")
        {
            EnsureOnly(properties, path, "type");
            return new IntegerType();
        }

        if (type == "boolean")
        {
            EnsureOnly(properties, path, "type");
            return new BooleanType();
        }

        if (type == "array")
        {
            EnsureOnly(properties, path, "type", "items");
            if (!schema.TryGetProperty("items", out var items))
            {
                throw Unsupported(Join(path, "items"), "Array schemas require items.");
            }

            return new ArrayType(ParseSchema(items, depth + 1, budget, Join(path, "items")));
        }

        if (type == "object")
        {
            EnsureOnly(properties, path, "type", "properties", "required", "additionalProperties");
            return ParseObjectSchema(schema, depth, budget, path);
        }

        throw Unsupported(Join(path, "type"), "Schema type is not supported.");
    }

    private static FlowType ParsePortableType(JsonElement type, int depth, SchemaBudget budget, string path)
    {
        if (type.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(path, "Portable type must be an object.");
        }

        Enter(type, depth, budget, path);
        var properties = GetAllowedProperties(type, path);
        var kind = RequiredString(type, "kind", path);
        switch (kind)
        {
            case "string":
                EnsureOnly(properties, path, "kind");
                return new StringType();
            case "number":
                EnsureOnly(properties, path, "kind");
                return new NumberType();
            case "integer":
                EnsureOnly(properties, path, "kind");
                return new IntegerType();
            case "boolean":
                EnsureOnly(properties, path, "kind");
                return new BooleanType();
            case "enum":
                EnsureOnly(properties, path, "kind", "values");
                return new EnumType(ParseStringArray(Required(type, "values", path), Join(path, "values")));
            case "array":
                EnsureOnly(properties, path, "kind", "elementType");
                return new ArrayType(ParsePortableType(Required(type, "elementType", path), depth + 1, budget, Join(path, "elementType")));
            case "nullable":
                EnsureOnly(properties, path, "kind", "innerType");
                return new NullableType(ParsePortableType(Required(type, "innerType", path), depth + 1, budget, Join(path, "innerType")));
            case "object":
                EnsureOnly(properties, path, "kind", "fields");
                var fieldsElement = Required(type, "fields", path);
                if (fieldsElement.ValueKind != JsonValueKind.Object)
                {
                    throw Unsupported(Join(path, "fields"), "Object fields must be an object.");
                }

                var fields = new Dictionary<string, FlowField>(StringComparer.Ordinal);
                foreach (var field in fieldsElement.EnumerateObject())
                {
                    budget.AddField();
                    var fieldPath = Join(Join(path, "fields"), field.Name);
                    if (field.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw Unsupported(fieldPath, "Object field must be an object.");
                    }

                    var fieldProperties = GetAllowedProperties(field.Value, fieldPath);
                    EnsureOnly(fieldProperties, fieldPath, "type", "required");
                    var required = field.Value.TryGetProperty("required", out var requiredElement)
                        ? RequiredBoolean(requiredElement, Join(fieldPath, "required"))
                        : false;
                    fields.Add(field.Name, new FlowField(ParsePortableType(Required(field.Value, "type", fieldPath), depth + 1, budget, Join(fieldPath, "type")), required));
                }

                return new ObjectType(fields);
            default:
                throw new FlowSchemaException(new FlowIssue("type.kind_unsupported", Join(path, "kind"), "Portable type kind is not supported."));
        }
    }

    private static ObjectType ParseObjectSchema(JsonElement schema, int depth, SchemaBudget budget, string path)
    {
        var propertiesElement = Required(schema, "properties", path);
        if (propertiesElement.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(Join(path, "properties"), "Object properties must be an object.");
        }

        if (!schema.TryGetProperty("additionalProperties", out var additionalProperties) ||
            additionalProperties.ValueKind != JsonValueKind.False)
        {
            throw Unsupported(Join(path, "additionalProperties"), "Object schemas must be closed.");
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredElement))
        {
            foreach (var value in ParseStringArray(requiredElement, Join(path, "required")))
            {
                if (!required.Add(value))
                {
                    throw Unsupported(Join(path, "required"), "Required fields must be unique.");
                }
            }
        }

        var fields = new Dictionary<string, FlowField>(StringComparer.Ordinal);
        foreach (var property in propertiesElement.EnumerateObject())
        {
            budget.AddField();
            fields.Add(property.Name, new FlowField(
                ParseSchema(property.Value, depth + 1, budget, Join(Join(path, "properties"), property.Name)),
                required.Contains(property.Name)));
        }

        if (required.Any(name => !fields.ContainsKey(name)))
        {
            throw Unsupported(Join(path, "required"), "Required fields must be declared in properties.");
        }

        return new ObjectType(fields);
    }

    private static HashSet<string> GetAllowedProperties(JsonElement element, string path)
    {
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            properties.Add(property.Name);
        }

        return properties;
    }

    private static void EnsureOnly(HashSet<string> properties, string path, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        if (properties.Any(property => !allowedSet.Contains(property)))
        {
            throw new FlowSchemaException(new FlowIssue("schema.unsupported", path, "Schema contains an unsupported keyword."));
        }
    }

    private static string RequiredString(JsonElement element, string name, string path)
    {
        var value = Required(element, name, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
        {
            throw Unsupported(Join(path, name), "Schema string value is invalid.");
        }

        return value.GetString()!;
    }

    private static bool RequiredBoolean(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            throw Unsupported(path, "Schema boolean value is invalid.");
        }

        return element.GetBoolean();
    }

    private static JsonElement Required(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw Unsupported(Join(path, name), "Schema property is required.");
        }

        return value;
    }

    private static string[] ParseStringArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Unsupported(path, "Schema value must be an array of strings.");
        }

        var values = new List<string>();
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } stringValue)
            {
                throw Unsupported(path, "Schema value must be an array of strings.");
            }

            values.Add(stringValue);
        }

        return values.ToArray();
    }

    private static string GetJsonType(FlowType type) => type switch
    {
        StringType or EnumType => "string",
        NumberType => "number",
        IntegerType => "integer",
        BooleanType => "boolean",
        ObjectType => "object",
        ArrayType => "array",
        _ => throw Unsupported("/", "Nullable type inner kind is not supported."),
    };

    private static void Enter(object value, int depth, SchemaBudget budget, string path)
    {
        if (depth > FlowLimits.MaxSchemaDepth)
        {
            throw new FlowSchemaException(new FlowIssue("schema.depth_exceeded", path, "Schema depth exceeds the supported limit."));
        }
    }

    private static FlowSchemaException Unsupported(string path, string message) =>
        new(new FlowIssue("schema.unsupported", path, message));

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(element.GetRawText()), new JsonReaderOptions
        {
            MaxDepth = FlowLimits.MaxJsonParseDepth,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var objects = new Stack<HashSet<string>>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                }
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var name = reader.GetString()!;
                    if (objects.Count > 0 && !objects.Peek().Add(name))
                    {
                        throw new FlowSchemaException(new FlowIssue("schema.property_duplicate", "/", "Schema contains a duplicate property."));
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    objects.Pop();
                }
            }
        }
        catch (JsonException)
        {
            throw new FlowSchemaException(new FlowIssue("schema.depth_exceeded", "/", "Schema nesting exceeds the supported limit."));
        }
    }

    private static string Join(string path, string segment) =>
        path == "/" ? "/" + Escape(segment) : path + "/" + Escape(segment);

    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed class SchemaBudget
    {
        private int _fields;

        public void AddField()
        {
            _fields++;
            if (_fields > FlowLimits.MaxSchemaFields)
            {
                throw new FlowSchemaException(new FlowIssue("schema.fields_limit", "/", "Schema contains too many fields."));
            }
        }
    }
}
