using System.Text.Json;

namespace Filomena.Core.Flows;

// Domain aliases reuse the existing type algebra; provider objects are never reflected.
public static class FlowDomain
{
    public static JsonElement CountItems(JsonElement value, FlowType type)
    {
        if (type is not ArrayType) throw new InvalidDataException("Contar Itens exige Lista não opcional.");
        Check(value, type);
        return JsonSerializer.SerializeToElement(value.GetArrayLength());
    }
    private static ObjectType Object(params (string Name, FlowType Type)[] fields) =>
        new(fields.ToDictionary(f => f.Name, f => new FlowField(f.Type, true)));
    public static ObjectType Person { get; } = Object(("nome", new StringType()), ("stableId", new StringType()));
    public static ObjectType Channel { get; } = Object(("nome", new StringType()), ("nativeId", new StringType()));
    public static ObjectType Reply { get; } = Object(("id", new StringType()));
    public static ObjectType Message { get; } = Object(("texto", new StringType()), ("remetente", new NullableType(Person)),
        ("canal", Channel), ("reply", new NullableType(Reply)));
    public static ObjectType Memory { get; } = Object(("conteudo", new StringType()),
        ("categoria", new EnumType(["limite", "apelido", "relacao", "fato", "nao_classificada"])));
    public static ObjectType Boundary { get; } = Object(("status", new EnumType(["compiled"])),
        ("kind", new EnumType(["no_teasing_about_topic"])), ("originalText", new StringType()),
        ("topicEvidence", new StringType()), ("constraintEvidence", new StringType()));

    public static FlowType Parse(JsonElement field)
    {
        var name = field.GetProperty("type").GetString()!;
        if (string.IsNullOrEmpty(name) || name.Length > 128) throw new InvalidDataException("Tipo inválido.");
        return ParseName(name, field);
    }

    private static FlowType ParseName(string name, JsonElement field, int depth = 0)
    {
        if (depth > 8) throw new InvalidDataException("Tipo excede oito níveis de Lista/Opcional.");
        if (name.EndsWith("[]", StringComparison.Ordinal)) return new ArrayType(ParseName(name[..^2], field, depth + 1));
        if (name.EndsWith('?')) return new NullableType(ParseName(name[..^1], field, depth + 1));
        return name switch
        {
            "text" => new StringType(),
            "number" => new NumberType(),
            "boolean" => new BooleanType(),
            "enum" => ParseEnum(field),
            "message" => Message,
            "actor" => Person,
            "channel" => Channel,
            "memory" => Memory,
            "boundary" => Boundary,
            _ => throw new InvalidDataException("Tipo desconhecido."),
        };
    }

    private static EnumType ParseEnum(JsonElement field)
    {
        var values = field.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray();
        if (values.Length > 64 || values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Enum deve ter valores preenchidos, até 64 opções.");
        return new EnumType(values);
    }

    public static FlowType PropertyType(FlowType type, IReadOnlyList<string> path)
    {
        if (path.Count == 0) return type;
        if (type is ArrayType array) return new ArrayType(PropertyType(array.ElementType, path));
        if (type is NullableType nullable)
        {
            var inner = PropertyType(nullable.InnerType, path);
            return inner is NullableType ? inner : new NullableType(inner);
        }
        if (type is not ObjectType obj || !obj.Fields.TryGetValue(path[0], out var field))
            throw new InvalidDataException("Propriedade inexistente neste tipo.");
        return PropertyType(field.Type, path.Skip(1).ToArray());
    }

    public static JsonElement Project(JsonElement value, FlowType type, IReadOnlyList<string> path)
    {
        Check(value, type);
        _ = PropertyType(type, path);
        if (path.Count == 0 || value.ValueKind == JsonValueKind.Null) return value.Clone();
        if (type is NullableType nullable) return Project(value, nullable.InnerType, path);
        if (type is ArrayType array) return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(v => Project(v, array.ElementType, path)).ToArray());
        var obj = (ObjectType)type;
        return Project(value.GetProperty(path[0]), obj.Fields[path[0]].Type, path.Skip(1).ToArray());
    }

    public static void Check(JsonElement value, FlowType type)
    {
        if (type is NullableType nullable) { if (value.ValueKind != JsonValueKind.Null) Check(value, nullable.InnerType); return; }
        var valid = type switch
        {
            StringType => value.ValueKind == JsonValueKind.String,
            EnumType e => value.ValueKind == JsonValueKind.String && e.Values.Contains(value.GetString()),
            NumberType => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n),
            BooleanType => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ArrayType => value.ValueKind == JsonValueKind.Array,
            ObjectType => value.ValueKind == JsonValueKind.Object,
            _ => false,
        };
        if (!valid) throw new InvalidDataException("Valor incompatível com " + type.Kind);
        if (type is ArrayType a) foreach (var item in value.EnumerateArray()) Check(item, a.ElementType);
        if (type is ObjectType o) foreach (var (name, field) in o.Fields)
        {
            if (!value.TryGetProperty(name, out var item)) throw new InvalidDataException("Campo ausente: " + name);
            Check(item, field.Type);
        }
    }

    public static JsonElement ForModel(JsonElement value, FlowType type, IDictionary<string, string> references)
    {
        if (type == Boundary) throw new InvalidDataException("Boundary exige artefato confiável da execution.");
        Check(value, type);
        if (value.ValueKind == JsonValueKind.Null) return value.Clone();
        if (type is NullableType nullable) return ForModel(value, nullable.InnerType, references);
        if (type is ArrayType array) return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(v => ForModel(v, array.ElementType, references)).ToArray());
        if (type is ObjectType obj)
        {
            var result = new Dictionary<string, JsonElement>();
            if (type == Memory && value.TryGetProperty("_subjects", out var subjects))
            {
                result["sobre"] = JsonSerializer.SerializeToElement(subjects.EnumerateArray()
                    .Select(p => ForModel(p, Person, references)).ToArray());
            }
            foreach (var (name, field) in obj.Fields)
            {
                if (name is "stableId" or "nativeId" or "id")
                {
                    var key = name + ":" + value.GetProperty(name).GetString();
                    if (!references.TryGetValue(key, out var reference)) references[key] = reference = "ref" + (references.Count + 1);
                    result["ref"] = JsonSerializer.SerializeToElement(reference);
                }
                else result[name] = ForModel(value.GetProperty(name), field.Type, references);
            }
            return JsonSerializer.SerializeToElement(result);
        }
        return value.Clone();
    }
}
