using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Boundaries;
using Filomena.Core.Outbound;

namespace Filomena.Core.Flows;

public sealed record FlowCompletionRequest(string NodeId, string System, JsonElement Inputs, JsonElement? ResponseSchema, string? Model, string? Provider, string? Correction,
    string? ReasoningEffort = null, int? MaxOutputTokens = null);
public interface IFlowCompletion
{
    Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token);
}
public sealed record TypedValue(FlowType Type, JsonElement Value);
public sealed record TypedStep(string NodeId, string Status, string? Exit, string? Error);
public sealed record TypedRunResult(bool Success, IReadOnlyList<TypedStep> Steps, IReadOnlyDictionary<string, Dictionary<string, TypedValue>> Outputs, string? TargetStateId = null);
internal sealed record TypedNodeExecutionTrace(string NodeId, string Status, string? Exit, string? Error, DateTimeOffset Timestamp, TimeSpan Duration,
    IReadOnlyDictionary<string, string>? Metadata = null);

public static class TypedFlowRunner
{
    public static JsonElement? ResponseSchema(TypedNode node)
    {
        if (BoundaryEnforcementProfile.Enabled(node.Source.Config)) return BoundaryDetectionTemplates.Read(node.Source.Config).SemanticSchema.ToJsonSchema();
        if (TypedFlowCompiler.IsTextOutput(node.Source.Config)) return null;
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

    public static Task<TypedRunResult> RunAsync(TypedFlow flow, JsonElement message, IFlowCompletion completion, CancellationToken token) =>
        RunCoreAsync(flow, message, completion, null, TimeProvider.System, null, null, null, null, null, null, token, null);

    internal static Task<TypedRunResult> RunSessionAsync(
        TypedFlow flow,
        JsonElement message,
        IFlowCompletion completion,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>> runtimeOutputs,
        TimeProvider timeProvider,
        Action<TypedNodeExecutionTrace> observer,
        Func<TypedNode, JsonElement?, string?, CancellationToken, Task<JsonElement>> memoryQuery,
        InstructionContext instructionContext,
        Func<string, IReadOnlyDictionary<string, TypedValue>, CancellationToken, Task>? instructionOutput,
        Func<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>, CancellationToken, Task>? nodeStarting,
        BoundaryExecutionRegistry boundaryRegistry,
        CancellationToken token, OutboundExecution? outbound = null) =>
        RunCoreAsync(flow, message, completion, runtimeOutputs, timeProvider, observer, memoryQuery, instructionContext, instructionOutput, nodeStarting, boundaryRegistry, token, outbound);

    private static async Task<TypedRunResult> RunCoreAsync(
        TypedFlow flow,
        JsonElement message,
        IFlowCompletion completion,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>? runtimeOutputs,
        TimeProvider timeProvider,
        Action<TypedNodeExecutionTrace>? observer,
        Func<TypedNode, JsonElement?, string?, CancellationToken, Task<JsonElement>>? memoryQuery,
        InstructionContext? instructionContext,
        Func<string, IReadOnlyDictionary<string, TypedValue>, CancellationToken, Task>? instructionOutput,
        Func<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>, CancellationToken, Task>? nodeStarting,
        BoundaryExecutionRegistry? boundaryRegistry,
        CancellationToken token, OutboundExecution? outbound)
    {
        var steps = new List<TypedStep>();
        var published = new Dictionary<string, Dictionary<string, TypedValue>>();
        var values = new Dictionary<PortRef, TypedValue>();
        var references = new Dictionary<string, string>();
        var sendAttempted = false;
        string current = flow.EntryId;
        DateTimeOffset nodeStartedAt = default;
        long? nodeStartedTimestamp = null;
        JsonElement SerializeInput(JsonElement value, FlowType type)
        {
            if (type is NullableType nullable)
                return value.ValueKind == JsonValueKind.Null ? value.Clone() : SerializeInput(value, nullable.InnerType);
            if (type is ArrayType array)
                return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(item => SerializeInput(item, array.ElementType)).ToArray());
            if (type == FlowDomain.Boundary)
            {
                if (boundaryRegistry is null) throw new InvalidDataException("Boundary exige contexto de execução confiável.");
                return boundaryRegistry.ForModel(JsonSerializer.SerializeToElement(new[] { value }), timeProvider.GetUtcNow(), references)[0].Clone();
            }
            return FlowDomain.ForModel(value, type, references);
        }
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
            if (node.Source.Kind is not ("property" or "count")) throw new InvalidDataException("Output operacional ainda não disponível.");
            var input = Input(node, node.Inputs.Single());
            var projected = node.Source.Kind == "count"
                ? FlowDomain.CountItems(input.Value, input.Type)
                : FlowDomain.Project(input.Value, input.Type, TypedFlowCompiler.PropertyPath(node.Source.Config));
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
                nodeStartedAt = timeProvider.GetUtcNow();
                nodeStartedTimestamp = timeProvider.GetTimestamp();
                observer?.Invoke(new(current, "started", null, null, nodeStartedAt, TimeSpan.Zero));
                if (nodeStarting is not null)
                    await nodeStarting(current, published.ToDictionary(pair => pair.Key,
                        pair => (IReadOnlyDictionary<string, TypedValue>)pair.Value.AsReadOnly()).AsReadOnly(), token);
                if (node.Source.Kind == "prompt" && BoundaryEnforcementProfile.Enabled(node.Source.Config))
                {
                    if (boundaryRegistry is null) throw new InvalidDataException("Boundary enforcement requires trusted execution context.");
                    TypedValue ProfileInput(string name)
                    {
                        var port = node.Inputs.Single(p => p.Name == name);
                        var value = Input(node, port);
                        FlowDomain.Check(value.Value, port.Type);
                        return value;
                    }
                    var output = await BoundaryEnforcementProfile.RunAsync(node, ProfileInput("limites").Value,
                        ProfileInput("resposta").Value.GetString()!, ProfileInput("mensagens").Value,
                        boundaryRegistry, completion, timeProvider,
                        metadata => observer?.Invoke(new(current, "boundary-policy", null, null, timeProvider.GetUtcNow(), TimeSpan.Zero, metadata)), token);
                    outbound?.Enforced(current, ProfileInput("resposta"), ProfileInput("limites"),
                        output["resultado"].Value.GetString() == "permitir", ShadowOutboundHost.Fingerprint(node.Source.Config));
                    if (instructionOutput is not null) await instructionOutput(current, output.AsReadOnly(), token);
                    token.ThrowIfCancellationRequested();
                    published[current] = output;
                    foreach (var (port, value) in output) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "prompt")
                {
                    var inputs = new Dictionary<string, JsonElement>();
                    foreach (var port in node.Inputs)
                    {
                        var connected = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id));
                        var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                        if (!port.Required && !connected && !configured) continue;
                        var value = Input(node, port); FlowDomain.Check(value.Value, port.Type);
                        inputs[port.Name] = SerializeInput(value.Value, port.Type);
                    }
                    var config = node.Source.Config;
                    var schema = ResponseSchema(node);
                    var system = InstructionContext.Compose(config, instructionContext);
                    string? correction = null; Dictionary<string, TypedValue>? output = null;
                    var retries = config.TryGetProperty("correctionRetry", out var retry) && retry.ValueKind == JsonValueKind.False ? 0 : 1;
                    for (var attempt = 0; attempt <= retries; attempt++)
                    {
                        var request = new FlowCompletionRequest(current, system, JsonSerializer.SerializeToElement(inputs), schema,
                            OptionalText(config, "model"), OptionalText(config, "provider"), correction,
                            OptionalText(config, "reasoningEffort"), config.TryGetProperty("maxOutputTokens", out var budget) ? budget.GetInt32() : null);
                        var text = await completion.CompleteAsync(request, token);
                        try { output = ValidateOutput(node, text, schema is not null); break; }
                        catch (Exception e) when (e is JsonException or InvalidDataException)
                        {
                            if (attempt == retries) throw new InvalidDataException("Resposta inválida após validação: " + (e is JsonException ? "JSON inválido." : e.Message));
                            correction = e is JsonException ? "Retorne um objeto JSON válido conforme o schema." : e.Message;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (instructionOutput is not null)
                    {
                        var snapshot = new System.Collections.ObjectModel.ReadOnlyDictionary<string, TypedValue>(
                            output!.ToDictionary(p => p.Key, p => p.Value with { Value = p.Value.Value.Clone() }));
                        await instructionOutput(current, snapshot, token);
                        token.ThrowIfCancellationRequested();
                    }
                    published[current] = output!;
                    outbound?.Generated(current, output!);
                    foreach (var (port, value) in output!) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "interpretBoundaries")
                {
                    if (boundaryRegistry is null) throw new InvalidDataException("Interpretar Limites exige contexto de execução confiável.");
                    var output = await boundaryRegistry.CompileAsync(Input(node, node.Inputs.Single()).Value,
                        BoundaryCompilerSettings.Read(node.Source.Config), timeProvider.GetUtcNow(), token);
                    var value = new TypedValue(new ArrayType(FlowDomain.Boundary), output);
                    outbound?.Compiled(current, Input(node, node.Inputs.Single()), value);
                    published[current] = new() { ["boundaries"] = value };
                    values[new(current, "boundaries")] = value;
                }
                else if (node.Source.Kind == "memory")
                {
                    if (memoryQuery is null) throw new InvalidDataException("Memória exige contexto de sessão.");
                    TypedValue? OptionalInput(string id)
                    {
                        var port = node.Inputs.Single(p => p.Id == id);
                        return flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, id)) ||
                            port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _)
                            ? Input(node, port) : null;
                    }
                    var response = await memoryQuery(node, OptionalInput("persons")?.Value, OptionalInput("query")?.Value.GetString(), token);
                    FlowDomain.Check(response, new ArrayType(FlowDomain.Memory));
                    var value = new TypedValue(new ArrayType(FlowDomain.Memory), response.Clone());
                    outbound?.Recall(current, value);
                    published[current] = new() { ["memories"] = value };
                    values[new(current, "memories")] = value;
                }
                else if (node.Source.Kind == "send")
                {
                    var candidate = Input(node, node.Inputs.Single());
                    FlowDomain.Check(candidate.Value, new StringType());
                    var result = outbound is not null ? outbound.Send(candidate)
                        : sendAttempted ? new ShadowSendResult("blocked", "duplicate_attempt") : new ShadowSendResult("disabled", "outbound_host_unavailable");
                    sendAttempted = true;
                    exit = result.Status;
                    published[current] = new()
                    {
                        ["status"] = new(node.Outputs.Single(p => p.Id == "status").Type, JsonSerializer.SerializeToElement(result.Status)),
                        ["diagnostico"] = new(new StringType(), JsonSerializer.SerializeToElement(result.DiagnosticCode))
                    };
                    foreach (var (port, value) in published[current]) values[new(current, port)] = value;
                    observer?.Invoke(new(current, "outbound", null, null, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                        outbound?.Trace(result) ?? new Dictionary<string, string> { ["result"] = result.Status, ["diagnosticCode"] = result.DiagnosticCode }));
                }
                else if (node.Source.Kind is "accumulate" or "history")
                {
                    if (runtimeOutputs is null || !runtimeOutputs.TryGetValue(current, out var supplied))
                        throw new InvalidDataException("Session runtime output is unavailable for " + node.Source.Kind + ".");
                    var output = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
                    foreach (var port in node.Outputs)
                    {
                        if (!supplied.TryGetValue(port.Id, out var value)) throw new InvalidDataException("Session runtime output port is unavailable: " + port.Id);
                        FlowDomain.Check(value.Value, port.Type);
                        if (!value.Type.IsCompatibleWith(port.Type)) throw new InvalidDataException("Session runtime output type is incompatible: " + port.Id);
                        output[port.Id] = new(port.Type, value.Value.Clone());
                        values[new(current, port.Id)] = output[port.Id];
                    }
                    published[current] = output;
                }
                else if (node.Source.Kind == "exit")
                {
                    if (runtimeOutputs is null) throw new InvalidDataException("Exit requires session runtime context.");
                    var settings = node.Source.Config.GetProperty("settings");
                    string target;
                    if (settings.TryGetProperty("targetStateId", out var configured) && configured.ValueKind == JsonValueKind.String)
                    {
                        target = configured.GetString()!;
                    }
                    else
                    {
                        var selected = Input(node, node.Inputs.Single()).Value.GetString()!;
                        target = settings.GetProperty("stateMapping").GetProperty(selected).GetString()!;
                    }
                    var duration = timeProvider.GetElapsedTime(nodeStartedTimestamp.Value);
                    steps.Add(new(current, "completed", null, null));
                    observer?.Invoke(new(current, "completed", null, null, timeProvider.GetUtcNow(), duration));
                    nodeStartedTimestamp = null;
                    if (sendAttempted)
                        observer?.Invoke(new(current, "hypothetical-transition", null, null, timeProvider.GetUtcNow(), TimeSpan.Zero,
                            new Dictionary<string, string> { ["wouldTransitionTo"] = target, ["applied"] = "false" }));
                    return new(true, steps, published, sendAttempted ? null : target);
                }
                else if (node.Source.Kind == "branch")
                {
                    var left = Input(node, node.Inputs.Single(p => p.Id == "a"));
                    var right = Input(node, node.Inputs.Single(p => p.Id == "b"));
                    exit = TypedBranch.Compare(left, right, node.Source.Config.GetProperty("settings").GetProperty("operator").GetString()!) ? "true" : "false";
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
                var completedDuration = timeProvider.GetElapsedTime(nodeStartedTimestamp.Value);
                steps.Add(new(current, "completed", exit, null));
                observer?.Invoke(new(current, "completed", exit, null, timeProvider.GetUtcNow(), completedDuration));
                nodeStartedTimestamp = null;
                var next = flow.Graph.ControlEdges.SingleOrDefault(e => e.Source == new PortRef(current, exit));
                if (next is null) return new(true, steps, published);
                current = next.Target.NodeId;
            }
        }
        catch (OperationCanceledException)
        {
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(current, "cancelled", null, "cancelled", timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value)));
            throw;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or HttpRequestException or InvalidOperationException or BoundaryCompilationException)
        {
            steps.Add(new(current, "failed", null, e is HttpRequestException ? "Serviço de modelo indisponível." : e.Message));
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(current, "failed", null, e.GetType().Name, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                    e is BoundaryCompilationException failure ? new Dictionary<string, string>
                    {
                        ["sourceMemoryId"] = failure.SourceMemoryId,
                        ["status"] = failure.Status,
                        ["diagnosticCode"] = failure.DiagnosticCode,
                        ["taxonomyVersion"] = "v1",
                        ["expectedKind"] = BoundaryAdmission.Kind,
                        ["template"] = "no_teasing_about_topic:v1"
                    } : null));
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
            var port = node.Outputs.Single();
            if (port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("maxWords", out var words)
                && text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > words.GetInt32())
                throw new InvalidDataException("Limite de palavras excedido: " + port.Name);
            return new() { [port.Id] = new(new StringType(), JsonSerializer.SerializeToElement(text)) };
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
