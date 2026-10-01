using System.Text.Json;
using Cortex.Core.Memory;

namespace Cortex.Core.Flows;

internal sealed record MemoryNodeSettings(MemoryMode Mode, MemoryScope Scope, MemoryCategory[] Categories,
    int MaxItems, int MaxCharacters, string? QueryText)
{
    public static MemoryNodeSettings Read(JsonElement config)
    {
        var s = config.GetProperty("settings");
        var mode = s.GetProperty("mode").GetString() switch
        {
            "exact" => MemoryMode.Exact,
            "semantic" => MemoryMode.Semantic,
            "hybrid" => MemoryMode.Hybrid,
            _ => throw new InvalidDataException("Modo de memória inválido.")
        };
        var scope = s.GetProperty("scope").GetString() switch
        {
            "person" => MemoryScope.Person,
            "conversation" => MemoryScope.Conversation,
            _ => throw new InvalidDataException("Escopo de memória inválido.")
        };
        if (mode == MemoryMode.Exact && scope != MemoryScope.Person || mode == MemoryMode.Semantic && scope != MemoryScope.Conversation)
            throw new InvalidDataException("Exato exige pessoa; semântico exige conversa. Use híbrido para pessoa com consulta.");
        if (s.TryGetProperty("activeOnly", out var active) && active.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Somente memória ativa está disponível nesta versão.");
        var items = s.GetProperty("maxItems").GetInt32();
        var chars = s.GetProperty("maxCharacters").GetInt32();
        if (items is < 1 or > 50 || chars is < 1 or > 100000) throw new InvalidDataException("Budget de memória inválido.");
        var categories = s.GetProperty("categories").EnumerateArray().Select(x => MemoryCategories.Parse(x.GetString()!)).Distinct().ToArray();
        var query = s.TryGetProperty("queryText", out var q) ? q.GetString() : null;
        return new(mode, scope, categories, items, chars, query);
    }

    public static bool IsPersonInput(FlowType type)
    {
        if (type is ArrayType array) type = array.ElementType;
        if (type is NullableType optional) type = optional.InnerType;
        return type == FlowDomain.Person;
    }
}

internal static class MemoryValues
{
    public static JsonElement Serialize(IReadOnlyList<MemoryItem> items) => JsonSerializer.SerializeToElement(items.Select(m => new
    {
        conteudo = m.Content,
        categoria = MemoryCategories.Name(m.Category),
        _subjects = m.Subjects.Select(p => new { nome = p.DisplayName ?? "", stableId = p.ActorStableId }).ToArray()
    }).ToArray());
}
