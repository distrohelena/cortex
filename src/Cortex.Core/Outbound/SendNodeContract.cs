using System.Text.Json;

namespace Cortex.Core.Outbound;

public static class SendNodeContract
{
    // Flat status/exit vocabulary. "sent" is the only continuation that may drive a speech-dependent transition.
    public static readonly string[] Statuses = ["disabled", "shadowed", "sent", "blocked", "failed", "indeterminate"];

    public static void Validate(JsonElement config)
    {
        foreach (var property in config.EnumerateObject())
            if (property.Name is not ("id" or "kind" or "title" or "x" or "y") &&
                !(property.Name == "settings" && property.Value.ValueKind == JsonValueKind.Object && !property.Value.EnumerateObject().Any()))
                throw new InvalidDataException("Enviar accepts no recipient, permission, or execution configuration.");
    }
}
