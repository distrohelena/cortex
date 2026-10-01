using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Evaluation.Execution;

public static class ConversationCaseInput
{
    public static JsonElement Build(string caseId, JsonElement context, string selfActorRef)
    {
        if (string.IsNullOrWhiteSpace(caseId) || string.IsNullOrWhiteSpace(selfActorRef)) throw Invalid();
        CheckKeys(context, ["conversation", "history", "currentBatch"]);
        var kind = Text(context, "conversation");
        if (kind is not ("dm" or "group")) throw Invalid();
        var history = Array(context, "history");
        var batch = Array(context, "currentBatch");
        if (batch.Length == 0) throw Invalid();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in history.Concat(batch))
        {
            CheckKeys(message, ["sender", "text", "reply"]);
            var sender = message.GetProperty("sender");
            CheckKeys(sender, ["ref", "name"]);
            var reference = Text(sender, "ref");
            var name = Text(sender, "name");
            if (names.TryGetValue(reference, out var existing) && existing != name) throw Invalid();
            names[reference] = name;
        }
        if (batch.Any(m => Text(m.GetProperty("sender"), "ref") == selfActorRef)) throw Invalid();

        object Person(string reference) => new
        {
            nome = names.GetValueOrDefault(reference, ""),
            stableId = "synthetic:actor:" + reference
        };
        JsonElement Message(JsonElement message, string segment, int index)
        {
            object? reply = null;
            if (message.TryGetProperty("reply", out var quote) && quote.ValueKind != JsonValueKind.Null)
            {
                CheckKeys(quote, ["senderRef", "text"]);
                var sender = Text(quote, "senderRef");
                string? quotedText = null;
                if (quote.TryGetProperty("text", out var text) && text.ValueKind != JsonValueKind.Null)
                {
                    if (text.ValueKind != JsonValueKind.String) throw Invalid();
                    quotedText = text.GetString();
                }
                // The corpus has no provider message ID; never infer quote identity from its text.
                reply = new
                {
                    id = $"synthetic:{caseId}:{segment}:{index}:quote",
                    texto = quotedText,
                    remetente = Person(sender),
                    origem = "synthetic_fixture"
                };
            }
            return JsonSerializer.SerializeToElement(new
            {
                texto = Text(message, "text"),
                remetente = Person(Text(message.GetProperty("sender"), "ref")),
                canal = new { nome = kind == "group" ? "grupo sintético" : "DM sintética", nativeId = "synthetic:" + caseId },
                reply,
                id = $"synthetic:{caseId}:{segment}:{index}"
            });
        }
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var type = new ArrayType(FlowDomain.Message);
        var historyInput = FlowDomain.ForModel(JsonSerializer.SerializeToElement(history.Select((m, i) => Message(m, "history", i)).ToArray()), type, refs);
        var batchInput = FlowDomain.ForModel(JsonSerializer.SerializeToElement(batch.Select((m, i) => Message(m, "batch", i)).ToArray()), type, refs);
        return JsonSerializer.SerializeToElement(new { mensagens = batchInput, historico = historyInput, tipo_conversa = kind == "dm" ? "dm" : "grupo" });
    }

    private static void CheckKeys(JsonElement value, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
    }

    private static string Text(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString())) throw Invalid();
        return text.GetString()!;
    }

    private static JsonElement[] Array(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) throw Invalid();
        return array.EnumerateArray().ToArray();
    }

    private static InvalidDataException Invalid() => new("Invalid synthetic conversation context.");
}
