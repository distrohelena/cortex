using System.Text.Json;
using System.Text.Json.Nodes;

namespace Filomena.Core.Flows;

public sealed record FlowCompletionRequest(string NodeId, string System, JsonElement Inputs, JsonElement? ResponseSchema, string? Model, string? Provider, string? Correction);
public interface IFlowCompletion
{
    Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token);
}
public sealed record TypedValue(FlowType Type, JsonElement Value);
public sealed record TypedStep(string NodeId, string Status, string? Exit, string? Error);
public sealed record TypedRunResult(bool Success, IReadOnlyList<TypedStep> Steps, IReadOnlyDictionary<string, Dictionary<string, TypedValue>> Outputs);

public static class TypedFlowRunner
{
    public static JsonElement? ResponseSchema(TypedNode node)
    {
        if (!node.Source.Config.TryGetProperty("responseFields", out var fields) || fields.GetArrayLength() == 0) return null;
        var schema = new ObjectType(node.Outputs.ToDictionary(p => p.Name, p => new FlowField(p.Type, true))).ToJsonSchema();
        var json = JsonNode.Parse(schema.GetRawText())!;
        foreach (var port in node.Outputs)
        {
            var property = json["properties"]![port.Name]!;
            if (port.Config.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String) property["description"] = description.GetString();
            if (port.Config.TryGetProperty("maxWords", out var wordLimit))
                property["description"] = (property["description"]?.GetValue<string>() ?? "") + $" Máximo de {wordLimit.GetInt32()} palavras separadas por whitespace.";
            foreach (var (key, keyword) in new[] { ("min", "minimum"), ("max", "maximum") })
                if (port.Config.TryGetProperty(key, out var bound)) property[keyword] = bound.GetDouble();
        }
        return JsonSerializer.SerializeToElement(json);
    }

    public static async Task<TypedRunResult> RunAsync(TypedFlow flow, JsonElement message, IFlowCompletion completion, CancellationToken token)
    {
        var steps = new List<TypedStep>();
        var published = new Dictionary<string, Dictionary<string, TypedValue>>();
        var values = new Dictionary<PortRef, TypedValue>();
        var references = new Dictionary<string, string>();
        string current = flow.EntryId;
        TypedValue Input(TypedNode node, TypedPort port)
        {
            var edge = flow.Graph.DataEdges.SingleOrDefault(e => e.Target == new PortRef(node.Source.Id, port.Id));
            if (edge is not null) return Resolve(edge.Source);
            if (port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out var local)) return new(port.Type, local.Clone());
            throw new InvalidDataException("Entrada não disponível: " + port.Name);
        }
        TypedValue Resolve(PortRef key)
        {
            if (values.TryGetValue(key, out var existing)) return existing;
            var node = flow.Nodes[key.NodeId];
            if (node.Source.Kind != "property") throw new InvalidDataException("Output operacional ainda não disponível.");
            var input = Input(node, node.Inputs.Single());
            var projected = FlowDomain.Project(input.Value, input.Type, TypedFlowCompiler.PropertyPath(node.Source.Config));
            var value = new TypedValue(node.Outputs.Single().Type, projected);
            values[key] = value; return value;
        }
        try
        {
            FlowDomain.Check(message, FlowDomain.Message);
            values[new(flow.EntryId, "message")] = new(FlowDomain.Message, message.Clone());
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var node = flow.Nodes[current]; var exit = "then";
                if (node.Source.Kind == "prompt")
                {
                    var inputs = new Dictionary<string, JsonElement>();
                    foreach (var port in node.Inputs)
                    {
                        var connected = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id));
                        var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                        if (!port.Required && !connected && !configured) continue;
                        var value = Input(node, port); FlowDomain.Check(value.Value, port.Type);
                        inputs[port.Name] = FlowDomain.ForModel(value.Value, port.Type, references);
                    }
                    var config = node.Source.Config;
                    var schema = ResponseSchema(node);
                    string? correction = null; Dictionary<string, TypedValue>? output = null;
                    var retries = config.TryGetProperty("correctionRetry", out var retry) && retry.ValueKind == JsonValueKind.False ? 0 : 1;
                    for (var attempt = 0; attempt <= retries; attempt++)
                    {
                        var request = new FlowCompletionRequest(current, config.GetProperty("prompt").GetString()!, JsonSerializer.SerializeToElement(inputs), schema,
                            OptionalText(config, "model"), OptionalText(config, "provider"), correction);
                        var text = await completion.CompleteAsync(request, token);
                        try { output = ValidateOutput(node, text, schema is not null); break; }
                        catch (Exception e) when (e is JsonException or InvalidDataException)
                        {
                            if (attempt == retries) throw new InvalidDataException("Resposta inválida após validação: " + (e is JsonException ? "JSON inválido." : e.Message));
                            correction = e is JsonException ? "Retorne um objeto JSON válido conforme o schema." : e.Message;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    published[current] = output!;
                    foreach (var (port, value) in output!) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "switch")
                {
                    var value = Input(node, node.Inputs.Single()).Value.GetString();
                    var cases = node.Source.Config.GetProperty("settings").GetProperty("cases");
                    var match = cases.EnumerateArray().Where(c => c.GetProperty("value").GetString() == value).ToArray();
                    if (match.Length == 1) exit = match[0].GetProperty("id").GetString()!;
                    else if (TypedFlowCompiler.HasOther(node.Source.Config)) exit = "other";
                    else throw new InvalidDataException("Switch sem caso correspondente e sem Outro.");
                }
                steps.Add(new(current, "completed", exit, null));
                var next = flow.Graph.ControlEdges.SingleOrDefault(e => e.Source == new PortRef(current, exit));
                if (next is null) return new(true, steps, published);
                current = next.Target.NodeId;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is InvalidDataException or JsonException or HttpRequestException or InvalidOperationException)
        {
            steps.Add(new(current, "failed", null, e is HttpRequestException ? "Serviço de modelo indisponível." : e.Message));
            return new(false, steps, published);
        }
    }

    private static string? OptionalText(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static Dictionary<string, TypedValue> ValidateOutput(TypedNode node, string text, bool structured)
    {
        if (text.Length > 65536) throw new InvalidDataException("Resposta grande demais.");
        if (!structured)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Texto vazio.");
            return new() { ["message"] = new(new StringType(), JsonSerializer.SerializeToElement(text)) };
        }
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Resposta deve ser objeto JSON.");
        var seen = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
            if (!seen.Add(property.Name) || !node.Outputs.Any(p => p.Name == property.Name)) throw new InvalidDataException("Campo extra ou duplicado.");
        var output = new Dictionary<string, TypedValue>();
        foreach (var port in node.Outputs)
        {
            if (!root.TryGetProperty(port.Name, out var value)) throw new InvalidDataException("Campo ausente: " + port.Name);
            try { FlowDomain.Check(value, port.Type); }
            catch (InvalidDataException e) { throw new InvalidDataException($"Campo {port.Name} ({port.Id}): {e.Message}"); }
            if (port.Config.TryGetProperty("min", out var min) && value.GetDouble() < min.GetDouble()) throw new InvalidDataException("Número abaixo do mínimo: " + port.Name);
            if (port.Config.TryGetProperty("max", out var max) && value.GetDouble() > max.GetDouble()) throw new InvalidDataException("Número acima do máximo: " + port.Name);
            // A word is a non-empty whitespace-delimited token, using .NET whitespace semantics.
            if (port.Config.TryGetProperty("maxWords", out var words) && value.GetString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > words.GetInt32()) throw new InvalidDataException("Limite de palavras excedido: " + port.Name);
            output[port.Id] = new(port.Type, value.Clone());
        }
        return output;
    }
}
