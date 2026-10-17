using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class FlowTypeTests
{
    [Fact]
    public void ConvertsSupportedTypesToClosedJsonSchemas()
    {
        var type = new ObjectType(new Dictionary<string, FlowField>
        {
            ["name"] = new(new StringType(), Required: true),
            ["age"] = new(new IntegerType(), Required: false),
        });

        var schema = type.ToJsonSchema();

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["name"], schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("age").GetProperty("type").GetString());
    }

    [Fact]
    public void NullableObjectFieldUsesNullableSchemaWithoutChangingRequiredness()
    {
        var type = new ObjectType(new Dictionary<string, FlowField>
        {
            ["profile"] = new(new NullableType(new ObjectType(new Dictionary<string, FlowField>
            {
                ["displayName"] = new(new StringType(), Required: true),
            })), Required: true),
        });

        var schema = type.ToJsonSchema();
        var profile = schema.GetProperty("properties").GetProperty("profile");

        Assert.Contains("object", profile.GetProperty("type").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("null", profile.GetProperty("type").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["profile"], schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void ParsesOnlyTheSupportedClosedSchemaSubset()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"kind":{"type":"string","enum":["a","b"]}},"required":["kind"],"additionalProperties":false}
            """);

        var parsed = FlowType.ParseJsonSchema(document.RootElement);

        var objectType = Assert.IsType<ObjectType>(parsed);
        var kind = Assert.IsType<EnumType>(objectType.Fields["kind"].Type);
        Assert.Equal(["a", "b"], kind.Values);
    }

    [Fact]
    public void ParsesNullableObjectSchemaWithRequiredNullableField()
    {
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{"profile":{"type":["object","null"],"properties":{"displayName":{"type":"string"}},"required":["displayName"],"additionalProperties":false}},"required":["profile"],"additionalProperties":false}
            """);

        var parsed = Assert.IsType<ObjectType>(FlowType.ParseJsonSchema(document.RootElement));
        var profile = Assert.IsType<NullableType>(parsed.Fields["profile"].Type);

        Assert.True(parsed.Fields["profile"].Required);
        Assert.IsType<ObjectType>(profile.InnerType);
    }

    [Fact]
    public void RejectsDuplicateSchemaProperties()
    {
        using var document = JsonDocument.Parse("{\"type\":\"string\",\"type\":\"number\"}");

        var exception = Assert.Throws<FlowSchemaException>(() => FlowType.ParseJsonSchema(document.RootElement));

        Assert.Equal("schema.property_duplicate", exception.Issue.Code);
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":true}")]
    [InlineData("{\"type\":\"string\",\"description\":\"not supported\"}")]
    [InlineData("{\"$ref\":\"https://example.test/schema\"}")]
    [InlineData("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"integer\"}]}")]
    public void RejectsUnknownKeywordsRemoteReferencesAndUnsupportedConstructs(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);

        var exception = Assert.Throws<FlowSchemaException>(() => FlowType.ParseJsonSchema(document.RootElement));

        Assert.Equal("schema.unsupported", exception.Issue.Code);
        Assert.DoesNotContain("https://", exception.Issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksDirectionalScalarAndEnumCompatibility()
    {
        Assert.True(new IntegerType().IsCompatibleWith(new NumberType()));
        Assert.False(new NumberType().IsCompatibleWith(new IntegerType()));
        Assert.True(new EnumType(["draft"]).IsCompatibleWith(new EnumType(["draft", "released"])));
        Assert.False(new EnumType(["draft", "other"]).IsCompatibleWith(new EnumType(["draft", "released"])));
    }

    [Fact]
    public void ChecksClosedObjectDirectionRequirednessAndNestedArrays()
    {
        var source = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
            {
                ["count"] = new(new IntegerType(), Required: true),
            })), Required: true),
        });
        var target = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
            {
                ["count"] = new(new NumberType(), Required: true),
                ["label"] = new(new StringType(), Required: false),
            })), Required: true),
        });

        Assert.True(source.IsCompatibleWith(target));
        Assert.False(target.IsCompatibleWith(source));

        var sourceMissingRequiredField = new ObjectType(new Dictionary<string, FlowField>());
        Assert.False(sourceMissingRequiredField.IsCompatibleWith(target));
    }

    [Fact]
    public void NullableEnumSchemaIncludesAndParsesNullEnumMember()
    {
        var type = new NullableType(new EnumType(["draft", "released"]));

        var schema = type.ToJsonSchema();
        var enumValues = schema.GetProperty("enum").EnumerateArray().ToArray();
        Assert.Contains(enumValues, value => value.ValueKind == JsonValueKind.Null);
        Assert.Equal(["draft", "released"], enumValues.Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()));

        var parsed = Assert.IsType<NullableType>(FlowType.ParseJsonSchema(schema));
        var parsedEnum = Assert.IsType<EnumType>(parsed.InnerType);
        Assert.Equal(["draft", "released"], parsedEnum.Values);
    }

    [Fact]
    public void RejectsNullableEnumSchemaThatOmitsNullMember()
    {
        using var document = JsonDocument.Parse("""
            {"type":["string","null"],"enum":["draft"]}
            """);

        var exception = Assert.Throws<FlowSchemaException>(() => FlowType.ParseJsonSchema(document.RootElement));

        Assert.Equal("schema.nullable_enum_invalid", exception.Issue.Code);
        Assert.Equal("/enum", exception.Issue.Path);
    }

    [Fact]
    public void AcceptsTargetOptionalFieldWhenSourceOmitsIt()
    {
        var source = new ObjectType(new Dictionary<string, FlowField>
        {
            ["id"] = new(new StringType(), Required: true),
        });
        var target = new ObjectType(new Dictionary<string, FlowField>
        {
            ["id"] = new(new StringType(), Required: true),
            ["optional"] = new(new StringType(), Required: false),
        });

        Assert.True(source.IsCompatibleWith(target));
    }

    [Fact]
    public void RejectsSourceExtraFieldForClosedTarget()
    {
        var source = new ObjectType(new Dictionary<string, FlowField>
        {
            ["id"] = new(new StringType(), Required: true),
            ["extra"] = new(new StringType(), Required: true),
        });
        var target = new ObjectType(new Dictionary<string, FlowField>
        {
            ["id"] = new(new StringType(), Required: true),
        });

        Assert.False(source.IsCompatibleWith(target));
    }

    [Fact]
    public void RejectsOptionalSourceForRequiredTarget()
    {
        var optionalSource = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new StringType(), Required: false),
        });
        var requiredTarget = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new StringType(), Required: true),
        });

        Assert.False(optionalSource.IsCompatibleWith(requiredTarget));
    }

    [Fact]
    public void RetainsNestedIntegerToNumberCompatibility()
    {
        var source = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
            {
                ["count"] = new(new IntegerType(), Required: true),
            })), Required: true),
        });
        var target = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
            {
                ["count"] = new(new NumberType(), Required: true),
            })), Required: true),
        });

        Assert.True(source.IsCompatibleWith(target));
    }

    [Fact]
    public void EnforcesSchemaDepthAndFieldLimits()
    {
        FlowType deep = new StringType();
        for (var index = 0; index <= FlowLimits.MaxSchemaDepth; index++)
        {
            deep = new ArrayType(deep);
        }

        Assert.Throws<FlowSchemaException>(() => _ = deep.ToJsonSchema());

        var fields = Enumerable.Range(0, FlowLimits.MaxSchemaFields + 1)
            .ToDictionary(index => $"field-{index}", _ => new FlowField(new StringType(), Required: true));

        Assert.Throws<FlowSchemaException>(() => _ = new ObjectType(fields).ToJsonSchema());
    }

    [Fact]
    public void RejectsUnknownPortableTypeKind()
    {
        using var document = JsonDocument.Parse("{\"kind\":\"future\"}");

        var exception = Assert.Throws<FlowSchemaException>(() => FlowType.ParsePortable(document.RootElement));

        Assert.Equal("type.kind_unsupported", exception.Issue.Code);
    }
    [Fact]
    public void TypedDictionarySchemaAndPortableTypePreserveNestedValues()
    {
        var valueType = new NullableType(new ArrayType(new ObjectType(new Dictionary<string, FlowField>
        {
            ["count"] = new(new IntegerType(), true),
        })));
        var type = new DictionaryType(valueType);
        var schema = type.ToJsonSchema();
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("array", schema.GetProperty("additionalProperties").GetProperty("type").EnumerateArray().First(item => item.GetString() != "null").GetString());
        Assert.Equal(schema.GetRawText(), FlowType.ParseJsonSchema(schema).ToJsonSchema().GetRawText());
        Assert.Equal(schema.GetRawText(), FlowType.ParsePortable(type.ToPortable()).ToJsonSchema().GetRawText());

        using var portable = JsonDocument.Parse("""{"kind":"dictionary","valueType":{"kind":"nullable","innerType":{"kind":"integer"}}}""");
        Assert.Equal(new DictionaryType(new NullableType(new IntegerType())), FlowType.ParsePortable(portable.RootElement));
    }

    [Fact]
    public void TypedDictionaryValidationAndPropertyProjectionPreserveKeys()
    {
        var type = new DictionaryType(new IntegerType());
        using var empty = JsonDocument.Parse("{}");
        FlowDomain.Check(empty.RootElement, type);
        using var value = JsonDocument.Parse("""{"Model A":8192,"gpt-6.1-sol":4096}""");
        FlowDomain.Check(value.RootElement, type);
        Assert.Equal("8192", FlowDomain.Project(value.RootElement, type, ["Model A"]).GetRawText());
        using var invalid = JsonDocument.Parse("""{"bad":"8192"}""");
        Assert.Throws<InvalidDataException>(() => { FlowDomain.Check(invalid.RootElement, type); });
        Assert.Throws<InvalidDataException>(() => { _ = FlowDomain.Project(value.RootElement, type, ["missing"]); });
        using var duplicate = JsonDocument.Parse("""{"model":1,"model":2}""");
        Assert.Throws<InvalidDataException>(() => { FlowDomain.Check(duplicate.RootElement, type); });
    }

    [Fact]
    public void DictionaryCompatibilityIsRecursiveAndDoesNotImplyObjectCompatibility()
    {
        Assert.True(new DictionaryType(new IntegerType()).IsCompatibleWith(new DictionaryType(new NumberType())));
        Assert.False(new DictionaryType(new NumberType()).IsCompatibleWith(new DictionaryType(new IntegerType())));
        Assert.True(new DictionaryType(new NullableType(new ArrayType(new IntegerType())))
            .IsCompatibleWith(new DictionaryType(new NullableType(new ArrayType(new NumberType())))));
        Assert.False(new DictionaryType(new IntegerType()).IsCompatibleWith(new ObjectType(new Dictionary<string, FlowField>())));
    }

    [Fact]
    public void ModelProjectionRecursesThroughDictionaryValuesAndPreservesKeys()
    {
        var type = new DictionaryType(FlowDomain.Person);
        using var value = JsonDocument.Parse("""{"Model A":{"nome":"Ana","stableId":"person-1"}}""");
        var references = new Dictionary<string, string>(StringComparer.Ordinal);

        var projected = FlowDomain.ForModel(value.RootElement, type, references);

        Assert.Equal("Ana", projected.GetProperty("Model A").GetProperty("nome").GetString());
        Assert.Equal("ref1", projected.GetProperty("Model A").GetProperty("ref").GetString());
        Assert.Equal("ref1", references["stableId:person-1"]);
    }

    [Theory]
    [InlineData("""{"type":"object","additionalProperties":true}""")]
    [InlineData("""{"type":"object","properties":{"fixed":{"type":"string"}},"additionalProperties":{"type":"integer"}}""")]
    public void RejectsUntypedAndMixedDictionarySchemas(string json)
    {
        using var schema = JsonDocument.Parse(json);
        Assert.Throws<FlowSchemaException>(() => FlowType.ParseJsonSchema(schema.RootElement));
    }
}
