using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Boundaries;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows;

public sealed record FlowCompletionRequest(string NodeId, string System, JsonElement Inputs, JsonElement? ResponseSchema, string? Model, string? Provider, string? Correction,
    string? ReasoningEffort = null, int? MaxOutputTokens = null)
{
    // Host-only bridge metadata. Never part of provider input or project authority.
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string>? HostReferences { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? HostExecutionId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public CompletionDiagnostics? Diagnostics { get; init; }
}
public interface IFlowCompletion
{
    Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token);
}
public sealed record TypedValue(FlowType Type, JsonElement Value);
public sealed record TypedStep(string NodeId, string Status, string? Exit, string? Error)
{
    public FlowFailureDiagnostic? Diagnostic { get; init; }
}
// Provider-accepted speech from this run. Only the session runtime turns it into history, exactly once.
public sealed record ConfirmedSend(string NodeId, string ProviderMessageId, string Text, OutboundSpeaker Speaker)
{
    public string? ReplyToMessageId { get; init; }
}
public sealed record TypedRunResult(bool Success, IReadOnlyList<TypedStep> Steps, IReadOnlyDictionary<string, Dictionary<string, TypedValue>> Outputs, string? TargetStateId = null,
    ConfirmedSend? ConfirmedSend = null)
{
    public IReadOnlyList<FlowInferenceAttempt> InferenceAttempts { get; init; } = [];
    public FlowFailureDiagnostic? Failure => Steps.LastOrDefault(s => s.Diagnostic is not null)?.Diagnostic;
}
internal sealed record TypedNodeExecutionTrace(string NodeId, string Status, string? Exit, string? Error, DateTimeOffset Timestamp, TimeSpan Duration,
    IReadOnlyDictionary<string, string>? Metadata = null);

public static class TypedFlowRunner
{
    /// <summary>Run a host-defined graph without a chat message or a text model provider.</summary>
    public static Task<TypedRunResult> RunAsync(TypedFlow flow, CancellationToken token) =>
        RunAsync(flow, JsonSerializer.SerializeToElement(new { }), new NoCompletion(), token);

    private sealed class NoCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            throw new InvalidDataException("A text model provider is required for prompt nodes.");
    }

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

    public static Task<TypedRunResult> RunAsync(TypedFlow flow, JsonElement message, IFlowCompletion completion,
        FlowDiagnosticOptions diagnostics, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        diagnostics.Validate();
        return RunCoreAsync(flow, message, completion, null, TimeProvider.System, null, null, null, null, null, null, token, null, diagnostics);
    }

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
        CancellationToken token, OutboundExecution? outbound, FlowDiagnosticOptions? diagnostics = null)
    {
        var scope = new Durable.ArtifactScope("local-harness", new("local", "local", "local", "local", "local"), "local", "local");
        var projectRef = new Durable.ArtifactRef(1, scope, "project", new string('0', 64));
        var identity = new Durable.ExecutionStart(scope, "local", projectRef, projectRef, "local", 0, Sessions.SessionEntryKind.Dm);
        var state = Execution.FlowExecutionKernel.Start(flow, message, identity, runtimeOutputs);
        var services = new Execution.FlowExecutionServices(completion) { Clock = timeProvider, Observer = observer,
            MemoryQuery = memoryQuery, InstructionContext = instructionContext, InstructionOutput = instructionOutput,
            NodeStarting = nodeStarting, BoundaryRegistry = boundaryRegistry, Outbound = outbound, Diagnostics = diagnostics ?? new() };
        Execution.KernelStepResult step;
        do { step = await Execution.FlowExecutionKernel.AdvanceAsync(flow, state, services, token); }
        while (step.Status == Durable.ExecutionStatus.Ready);
        if (step.Status == Durable.ExecutionStatus.WaitingForHuman)
            return new(false, [.. step.Result.Steps, new(state.Snapshot.Cursor!, "failed", null, "human-feedback-backend-required")], step.Result.Outputs, null, step.Result.ConfirmedSend) { InferenceAttempts = step.Result.InferenceAttempts };
        return step.Result;
    }

    internal static string? OptionalText(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static Dictionary<string, TypedValue> ValidateOutput(TypedNode node, string text, bool structured)
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
            catch (InvalidDataException e) { throw new InvalidDataException($"Campo {port.Name} ({port.Id}): {e.Message}", e); }
            if (port.Config.TryGetProperty("min", out var min) && value.GetDouble() < min.GetDouble()) throw new InvalidDataException("Número abaixo do mínimo: " + port.Name);
            if (port.Config.TryGetProperty("max", out var max) && value.GetDouble() > max.GetDouble()) throw new InvalidDataException("Número acima do máximo: " + port.Name);
            // A word is a non-empty whitespace-delimited token, using .NET whitespace semantics.
            if (port.Config.TryGetProperty("maxWords", out var words) && value.GetString()!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > words.GetInt32()) throw new InvalidDataException("Limite de palavras excedido: " + port.Name);
            output[port.Id] = new(port.Type, value.Clone());
        }
        return output;
    }
}
