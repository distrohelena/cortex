using System.Text;
using System.Text.Json;
using Xunit;

namespace Cortex.Temporal.Tests;

public static class PayloadPrivacyTests
{
    public static void AssertPrivateMarkersAbsent(string json)
    {
        Assert.DoesNotContain("SECRET-", json);
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement);
    }
    private static void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "data" && property.Value.ValueKind == JsonValueKind.String)
                {
                    var raw = Encoding.UTF8.GetString(Convert.FromBase64String(property.Value.GetString()!));
                    Assert.DoesNotContain("SECRET-", raw);
                }
                Visit(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Visit(item);
    }
}
