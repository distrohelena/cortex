using System.Collections.ObjectModel;
using System.Text.Json;

namespace Cortex.Core.Flows;

/// <summary>Host-owned function contract, independent from its executable delegate.</summary>
public sealed class FlowFunctionDefinition
{
    public FlowFunctionDefinition(
        string id,
        string displayName,
        IEnumerable<FlowDataPortDefinition> parameters,
        IEnumerable<FlowDataPortDefinition> returns,
        FlowNodeExecutionMode executionMode,
        FlowEffect effect,
        string? navigableGraphReference = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Function identity is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Function display name is required.", nameof(displayName));
        if (executionMode is not (FlowNodeExecutionMode.Action or FlowNodeExecutionMode.Pure))
            throw new ArgumentException("Callable functions must be Action or Pure.", nameof(executionMode));
        if (executionMode == FlowNodeExecutionMode.Pure && effect != FlowEffect.Pure)
            throw new ArgumentException("Pure functions must have Pure effect.", nameof(effect));
        if (effect is not (FlowEffect.Pure or FlowEffect.Model or FlowEffect.WriteState))
            throw new ArgumentException("Callable functions in the local runner support Pure, Model, or WriteState effects.", nameof(effect));

        Id = id;
        DisplayName = displayName;
        Parameters = FreezePorts(parameters, nameof(parameters));
        Returns = FreezePorts(returns, nameof(returns));
        ExecutionMode = executionMode;
        Effect = effect;
        NavigableGraphReference = string.IsNullOrWhiteSpace(navigableGraphReference) ? null : navigableGraphReference;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<FlowDataPortDefinition> Parameters { get; }
    public IReadOnlyList<FlowDataPortDefinition> Returns { get; }
    public FlowNodeExecutionMode ExecutionMode { get; }
    public FlowEffect Effect { get; }
    public string? NavigableGraphReference { get; }

    private static IReadOnlyList<FlowDataPortDefinition> FreezePorts(
        IEnumerable<FlowDataPortDefinition> ports, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(ports, parameterName);
        var copy = ports.ToArray();
        if (copy.Any(port => port is null || string.IsNullOrWhiteSpace(port.Name) || port.Type is null || port.AcceptsAny))
            throw new ArgumentException("Function ports need names and concrete types.", parameterName);
        if (copy.Select(port => port.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Function port names must be unique.", parameterName);
        return new ReadOnlyCollection<FlowDataPortDefinition>(copy);
    }
}

public sealed class FlowFunctionRegistry
{
    private readonly Dictionary<string, FlowFunctionDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, TypedValue>, CancellationToken,
        Task<IReadOnlyDictionary<string, JsonElement>>>> _invokers = new(StringComparer.Ordinal);

    public void RegisterDefinition(FlowFunctionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Id, definition))
            throw new ArgumentException("Function identity is already registered: " + definition.Id, nameof(definition));
    }

    public void RegisterInvoker(
        string functionId,
        Func<IReadOnlyDictionary<string, TypedValue>, CancellationToken,
            Task<IReadOnlyDictionary<string, JsonElement>>> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        if (!_definitions.ContainsKey(functionId))
            throw new KeyNotFoundException("Function definition is not registered: " + functionId);
        if (!_invokers.TryAdd(functionId, invoke))
            throw new ArgumentException("Function delegate is already registered: " + functionId, nameof(functionId));
    }

    public void Register(
        FlowFunctionDefinition definition,
        Func<IReadOnlyDictionary<string, TypedValue>, CancellationToken,
            Task<IReadOnlyDictionary<string, JsonElement>>> invoke)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invoke);
        RegisterDefinition(definition);
        _invokers.Add(definition.Id, invoke);
    }

    /// <summary>Returns registered contracts without resolving or invoking host delegates.</summary>
    public IReadOnlyList<FlowFunctionDefinition> Describe() =>
        new ReadOnlyCollection<FlowFunctionDefinition>(_definitions.Values.ToArray());

    /// <summary>Projects registered function metadata into node contracts without requiring an invoker.</summary>
    public NodeDefinition DescribeCall(string functionId, string kind)
    {
        if (!_definitions.TryGetValue(functionId, out var definition))
            throw new KeyNotFoundException("Function identity is not registered: " + functionId);
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Call node kind is required.", nameof(kind));
        var isPure = definition.ExecutionMode == FlowNodeExecutionMode.Pure;
        return new NodeDefinition(kind, 1, definition.Effect,
            dataInputs: definition.Parameters,
            dataOutputs: definition.Returns,
            controlInputs: isPure ? null : [new FlowControlPortDefinition("exec")],
            controlOutputs: isPure ? null : [new FlowControlPortDefinition("then")])
        {
            DisplayName = "Chamar " + definition.DisplayName,
            ExecutionMode = definition.ExecutionMode,
            NavigableGraphReference = definition.NavigableGraphReference
        };
    }

    /// <summary>Creates a handler whose target is fixed to the registered function identity.</summary>
    public FlowNodeHandler CreateCallHandler(string functionId, string kind)
    {
        if (!_definitions.ContainsKey(functionId))
            throw new KeyNotFoundException("Function identity is not registered: " + functionId);
        if (!_invokers.TryGetValue(functionId, out var invoke))
            throw new InvalidOperationException("Function has no registered host delegate: " + functionId);
        var nodeDefinition = DescribeCall(functionId, kind);
        return new FlowNodeHandler(nodeDefinition, (_, inputs, token) => invoke(inputs, token));
    }
}

public static class FlowStructFactory
{
    public static FlowNodeHandler CreateMakeHandler(string kind, ObjectType schema, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var inputs = schema.Fields.Select(pair =>
            new FlowDataPortDefinition(pair.Key, pair.Value.Type, pair.Value.Required)).ToArray();
        var definition = new NodeDefinition(kind, 1, FlowEffect.Pure,
            dataInputs: inputs,
            dataOutputs: [new FlowDataPortDefinition("value", schema)])
        { DisplayName = displayName ?? "Criar objeto " + kind, ExecutionMode = FlowNodeExecutionMode.Pure };
        return new FlowNodeHandler(definition, (_, values, _) =>
        {
            var fields = values.ToDictionary(pair => pair.Key, pair => pair.Value.Value.Clone(), StringComparer.Ordinal);
            var value = JsonSerializer.SerializeToElement(fields);
            FlowDomain.Check(value, schema);
            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["value"] = value });
        });
    }

    public static FlowNodeHandler CreateBreakHandler(
        string kind, ObjectType schema, IEnumerable<string> selectedFields, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(selectedFields);
        var selected = selectedFields.ToArray();
        if (selected.Any(string.IsNullOrWhiteSpace) ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length ||
            selected.Any(name => !schema.Fields.ContainsKey(name)))
            throw new ArgumentException("Selected fields must be declared, unique, and non-empty.", nameof(selectedFields));

        var outputs = selected.Select(name =>
            new FlowDataPortDefinition(name, schema.Fields[name].Type, Required: true)).ToArray();
        var definition = new NodeDefinition(kind, 1, FlowEffect.Pure,
            dataInputs: [new FlowDataPortDefinition("value", schema, Required: true)],
            dataOutputs: outputs)
        { DisplayName = displayName ?? "Separar estrutura " + kind, ExecutionMode = FlowNodeExecutionMode.Pure };
        return new FlowNodeHandler(definition, (_, inputs, _) =>
        {
            var source = inputs["value"].Value;
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var name in selected)
            {
                if (!source.TryGetProperty(name, out var value))
                    throw new InvalidDataException("Selected field is absent from the object: " + name);
                FlowDomain.Check(value, schema.Fields[name].Type);
                values.Add(name, value.Clone());
            }

            return Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(values);
        });
    }
}
