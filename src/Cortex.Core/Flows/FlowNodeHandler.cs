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
        definition = ValidateDefinition(definition, isEntry);
        Definition = definition;
        IsEntry = definition.ExecutionMode == FlowNodeExecutionMode.Entry;
        IsPure = definition.ExecutionMode == FlowNodeExecutionMode.Pure;
        _execute = execute;
    }

    internal static NodeDefinition ValidateDefinition(NodeDefinition definition, bool isEntry = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.BlockVersion != 1 || definition.Effect is not (FlowEffect.Pure or FlowEffect.Model or FlowEffect.WriteState))
            throw new ArgumentException("Host nodes in the local runner support block version 1 and Pure/Model/WriteState effects.", nameof(definition));
        if (isEntry && definition.ExecutionMode == FlowNodeExecutionMode.Pure)
            throw new ArgumentException("An entry node cannot be configured as Pure.", nameof(definition));
        if (isEntry || definition.ExecutionMode == FlowNodeExecutionMode.Entry)
        {
            definition = definition with { ExecutionMode = FlowNodeExecutionMode.Entry };
        }

        if (definition.ExecutionMode == FlowNodeExecutionMode.Pure)
        {
            if (definition.Effect != FlowEffect.Pure)
                throw new ArgumentException("Pure host nodes must have Pure effect.", nameof(definition));
            if (definition.ControlInputs.Count != 0 || definition.ControlOutputs.Count != 0)
                throw new ArgumentException("Pure host nodes cannot have execution ports.", nameof(definition));
        }
        else if (definition.ExecutionMode == FlowNodeExecutionMode.Entry
            ? definition.ControlInputs.Count != 0 || definition.DataInputs.Count != 0 || definition.ControlOutputs.Any(port => port.Name != "then")
            : definition.ControlOutputs.Any(port => port.Name != "then") || definition.ControlInputs.Count != 1 || definition.ControlInputs[0].Name != "exec")
        {
            throw new ArgumentException("Host nodes use exec/then control ports; entries have no inputs.", nameof(definition));
        }
        if (definition.DataInputs.Concat(definition.DataOutputs).Any(port => port.AcceptsAny))
            throw new ArgumentException("Host node data ports must declare concrete types.", nameof(definition));
        return definition;
    }

    public NodeDefinition Definition { get; }
    public bool IsEntry { get; }
    public bool IsPure { get; }

    internal Task<IReadOnlyDictionary<string, JsonElement>> ExecuteAsync(JsonElement config,
        IReadOnlyDictionary<string, TypedValue> inputs, CancellationToken token) => _execute(config, inputs, token);

    internal static JsonElement Config(FlowNode node) => node.Config.TryGetProperty("settings", out var settings)
        ? settings : JsonSerializer.SerializeToElement(new { });
}
