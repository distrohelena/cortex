using System.Text.Json;

namespace Cortex.Core.Flows;

/// <summary>A host-owned operation using Cortex's existing node and port contracts.</summary>
public sealed class FlowNodeHandler
{
    private readonly Func<JsonElement, IReadOnlyDictionary<string, TypedValue>, CancellationToken,
        Task<IReadOnlyDictionary<string, JsonElement>>> _execute;

    public FlowNodeHandler(NodeDefinition definition,
        Func<JsonElement, IReadOnlyDictionary<string, TypedValue>, CancellationToken,
            Task<IReadOnlyDictionary<string, JsonElement>>> execute, bool isEntry = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(execute);
        if (definition.BlockVersion != 1 || definition.Effect is not (FlowEffect.Pure or FlowEffect.Model))
            throw new ArgumentException("Host nodes in the local runner support block version 1 and Pure/Model effects.", nameof(definition));
        if (definition.ControlOutputs.Any(port => port.Name != "then") ||
            (isEntry ? definition.ControlInputs.Count != 0 || definition.DataInputs.Count != 0
                : definition.ControlInputs.Count != 1 || definition.ControlInputs[0].Name != "exec"))
            throw new ArgumentException("Host nodes use exec/then control ports; entries have no inputs.", nameof(definition));
        if (definition.DataInputs.Concat(definition.DataOutputs).Any(port => port.AcceptsAny))
            throw new ArgumentException("Host node data ports must declare concrete types.", nameof(definition));
        Definition = definition;
        IsEntry = isEntry;
        _execute = execute;
    }

    public NodeDefinition Definition { get; }
    public bool IsEntry { get; }

    internal Task<IReadOnlyDictionary<string, JsonElement>> ExecuteAsync(JsonElement config,
        IReadOnlyDictionary<string, TypedValue> inputs, CancellationToken token) => _execute(config, inputs, token);

    internal static JsonElement Config(FlowNode node) => node.Config.TryGetProperty("settings", out var settings)
        ? settings : JsonSerializer.SerializeToElement(new { });
}
