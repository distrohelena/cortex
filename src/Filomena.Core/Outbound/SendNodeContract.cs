using System.Text.Json;

namespace Filomena.Core.Outbound;

public static class SendNodeContract
{
    public static void Validate(JsonElement config)
    {
        foreach (var property in config.EnumerateObject())
            if (property.Name is not ("id" or "kind" or "title" or "x" or "y") &&
                !(property.Name == "settings" && property.Value.ValueKind == JsonValueKind.Object && !property.Value.EnumerateObject().Any()))
                throw new InvalidDataException("Enviar accepts no recipient, permission, or execution configuration.");
    }
}
