using System.Text.Json;
using Cortex.Core.Flows;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class JsonTypeTests
{
    [Fact]
    public void JsonTypeUsesUnrestrictedSchemaAndExplicitPortableKind()
    {
        var type = new JsonType();
        Assert.Equal("{}", type.ToJsonSchema().GetRawText());
        Assert.Equal("{\"kind\":\"json\"}", type.ToPortable().GetRawText());
        Assert.IsType<JsonType>(FlowType.ParseJsonSchema(type.ToJsonSchema()));
        Assert.IsType<JsonType>(FlowType.ParseJsonSchema(Json("{}")));
        Assert.IsType<JsonType>(FlowType.ParsePortable(type.ToPortable()));
        var nullable = new NullableType(type);
        Assert.Equal("{}", nullable.ToJsonSchema().GetRawText());
        Assert.IsType<NullableType>(FlowType.ParsePortable(nullable.ToPortable()));
        Assert.IsType<JsonType>(FlowType.ParseJsonSchema(nullable.ToJsonSchema()));
        Assert.Throws<FlowSchemaException>(() => FlowType.ParsePortable(Json("{\"kind\":\"future\"}")));
        Assert.Throws<FlowSchemaException>(() => FlowType.ParseJsonSchema(Json("{\"description\":\"anything\"}")));
    }

    [Fact]
    public void JsonTypeAcceptsNativeJsonValuesAndRejectsUndefined()
    {
        var type = new JsonType();
        foreach (var json in new[] { "{}", "[]", "\"text\"", "17", "true", "null" })
        {
            using var document = JsonDocument.Parse(json);
            FlowDomain.Check(document.RootElement, type);
            Assert.True(JsonElement.DeepEquals(document.RootElement, FlowDomain.ForModel(document.RootElement, type, new Dictionary<string, string>())));
        }
        Assert.Throws<InvalidDataException>(() => FlowDomain.Check(default(JsonElement), type));
    }

    [Fact]
    public void JsonTypeIsSafeWideningIncludingNestedAndNullableTypes()
    {
        var json = new JsonType();
        Assert.True(new StringType().IsCompatibleWith(json));
        Assert.True(new ObjectType(new Dictionary<string, FlowField>()).IsCompatibleWith(json));
        Assert.True(new DictionaryType(new IntegerType()).IsCompatibleWith(json));
        Assert.True(json.IsCompatibleWith(json));
        Assert.False(json.IsCompatibleWith(new StringType()));
        Assert.False(new NullableType(json).IsCompatibleWith(new StringType()));
        Assert.True(new NullableType(new IntegerType()).IsCompatibleWith(json));
        Assert.True(json.IsCompatibleWith(new NullableType(json)));
        Assert.True(new NullableType(json).IsCompatibleWith(json));
        Assert.True(new ArrayType(new StringType()).IsCompatibleWith(new ArrayType(json)));
        Assert.False(new ArrayType(json).IsCompatibleWith(new ArrayType(new StringType())));
    }

    [Fact]
    public void JsonTypeStaysOpaqueInsideTypedContainersAndPreservesReferencesAndKeys()
    {
        var raw = new JsonType();
        var type = new ObjectType(new Dictionary<string, FlowField>
        {
            ["payload"] = new(raw, true),
            ["items"] = new(new ArrayType(raw), true),
            ["models"] = new(new DictionaryType(raw), true),
        });
        using var document = JsonDocument.Parse("""{"payload":{"StableId":"person-1","unknown":{"x":[1,null]}},"items":[false,null],"models":{"Model A":{"id":"ref-looking"}}}""");
        FlowDomain.Check(document.RootElement, type);
        var references = new Dictionary<string, string>();
        var projected = FlowDomain.ForModel(document.RootElement, type, references);
        Assert.True(JsonElement.DeepEquals(document.RootElement, projected));
        Assert.Empty(references);
        Assert.Throws<InvalidDataException>(() => FlowDomain.Check(Json("{\"payload\":{}}"), type));
    }

    [Fact]
    public void RawJsonFieldsNamedLikeDomainReferencesRemainOpaqueInsideTypedContainers()
    {
        var json = new JsonType();
        var values = new[] { "\"original\"", "null", "{\"mensagem\":7}", "[1,null]", "17", "true" };
        foreach (var fieldName in new[] { "id", "stableId", "nativeId" })
        foreach (var fieldType in new FlowType[] { json, new NullableType(json) })
        foreach (var jsonValue in values)
        {
            var objectType = new ObjectType(new Dictionary<string, FlowField> { [fieldName] = new(fieldType, true) });
            var rawValue = Json(jsonValue);
            var source = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [fieldName] = rawValue });
            var references = new Dictionary<string, string>(StringComparer.Ordinal);

            var projected = FlowDomain.ForModel(source, objectType, references);

            Assert.True(JsonElement.DeepEquals(source, projected));
            Assert.Empty(references);
        }

        var nestedType = new ObjectType(new Dictionary<string, FlowField>
        {
            ["id"] = new(json, true),
            ["stableId"] = new(new NullableType(json), true),
            ["nativeId"] = new(json, true),
        });
        var nested = Json("""[{"id":"original","stableId":{"mensagem":7},"nativeId":null}]""");
        var outer = new ArrayType(nestedType);
        var referencesInArray = new Dictionary<string, string>(StringComparer.Ordinal);
        Assert.True(JsonElement.DeepEquals(nested, FlowDomain.ForModel(nested, outer, referencesInArray)));
        Assert.Empty(referencesInArray);

        var dictionary = new DictionaryType(nestedType);
        var mapped = Json("""{"Model A":{"id":[1,null],"stableId":false,"nativeId":"unchanged"}}""");
        var referencesInDictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        Assert.True(JsonElement.DeepEquals(mapped, FlowDomain.ForModel(mapped, dictionary, referencesInDictionary)));
        Assert.Empty(referencesInDictionary);
    }

    [Fact]
    public void ConcreteDomainReferenceFieldsKeepTheirExistingProjection()
    {
        var person = Json("""{"nome":"Ana","stableId":"person-1"}""");
        var references = new Dictionary<string, string>(StringComparer.Ordinal);

        var projected = FlowDomain.ForModel(person, FlowDomain.Person, references);

        Assert.Equal("Ana", projected.GetProperty("nome").GetString());
        Assert.Equal("ref1", projected.GetProperty("ref").GetString());
        Assert.Equal("ref1", references["stableId:person-1"]);
        Assert.False(projected.TryGetProperty("stableId", out _));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
