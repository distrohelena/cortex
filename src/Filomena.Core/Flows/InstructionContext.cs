using System.Text.Json;

namespace Filomena.Core.Flows;

// Immutable, administrative text captured with the compiled project and batch.
internal sealed record InstructionContext(string PersonalityCore, string ParticipationGuidance)
{
    internal static string ReadText(JsonElement document, string name)
    {
        if (!document.TryGetProperty(name, out var value)) return "";
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 32000)
            throw new InvalidDataException(name + " must be text of at most 32000 characters.");
        return value.GetString()!;
    }

    internal static bool Enabled(JsonElement config, string name)
    {
        if (!config.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(name + " must be boolean.");
        return value.GetBoolean();
    }

    internal static string Compose(JsonElement config, InstructionContext? context)
    {
        var core = Enabled(config, "usePersonalityCore");
        var guidance = Enabled(config, "useStateGuidance");
        if ((core || guidance) && context is null)
            throw new InvalidDataException("Instruction references require a project/state execution context.");
        var sections = new List<string>();
        if (core && !string.IsNullOrEmpty(context!.PersonalityCore))
            sections.Add("[Personality core]\n" + context.PersonalityCore);
        if (guidance && !string.IsNullOrEmpty(context!.ParticipationGuidance))
            sections.Add("[State participation guidance]\n" + context.ParticipationGuidance);
        sections.Add(config.GetProperty("prompt").GetString()!);
        return string.Join("\n\n", sections);
    }
}
