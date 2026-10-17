using System.Text.Json;
using Cortex.Core.Boundaries;

namespace Cortex.Core.Flows;

internal sealed record ResolvedFlowPort(string Id, string Name, FlowType? Type, bool Required, JsonElement Config = default)
{
    public JsonElement? ConstantValue => Config.ValueKind == JsonValueKind.Object && Config.TryGetProperty("value", out var value) ? value.Clone() : null;
}
internal sealed record ResolvedFlowNode(
    FlowNode Source, string DisplayName, FlowNodeExecutionMode ExecutionMode, FlowEffect Effect,
    IReadOnlyList<ResolvedFlowPort> Inputs, IReadOnlyList<ResolvedFlowPort> Outputs,
    IReadOnlyList<FlowControlPortDefinition> ControlInputs, IReadOnlyList<FlowControlPortDefinition> ControlOutputs,
    IReadOnlyList<FlowConfigFieldDefinition> Configuration, IReadOnlyList<TypedFlowIssue> Diagnostics,
    string? NavigableGraphReference = null);

/// <summary>Single typed graph contract resolver shared by compilation and metadata description.</summary>
internal static class TypedFlowNodeResolver
{
    public static ResolvedFlowNode Resolve(FlowNode node, IReadOnlyDictionary<string, NodeDefinition> definitions,
        Func<string, ResolvedFlowPort?> incoming, bool allowUnresolved,
        IReadOnlyList<DataEdge>? dataEdges = null, IReadOnlyList<ControlEdge>? controlEdges = null)
    {
        var id = node.Id;
        var config = node.Config;
        var inputs = new List<ResolvedFlowPort>();
        var outputs = new List<ResolvedFlowPort>();
        var controlsIn = new List<FlowControlPortDefinition>();
        var controlsOut = new List<FlowControlPortDefinition>();
        var issues = new List<TypedFlowIssue>();
        var mode = node.Kind switch { "dm" or "group" => FlowNodeExecutionMode.Entry, "property" or "count" => FlowNodeExecutionMode.Pure, _ => FlowNodeExecutionMode.Action };
        var effect = BuiltinEffect(node.Kind);
        var display = BuiltinName(node.Kind);
        string? navigation = null;
        void In(string name, FlowType? type, bool required = false, JsonElement? constant = null) => inputs.Add(new(name, name, type, required, WrapConstant(constant)));
        void Out(string name, FlowType? type, bool required = true, JsonElement config = default) => outputs.Add(new(name, name, type, required, config));
        ResolvedFlowPort Inferred(string name, bool required = true)
        {
            var found = incoming(name);
            if (found is not null)
            {
                if (found.Type is null && !issues.Any(issue => issue.Port == name))
                    issues.Add(new(id, name, "Port type is unresolved because its upstream contract is unresolved."));
                return found with { Id = name, Name = name, Required = required };
            }
            if (!allowUnresolved) throw new InvalidDataException("Conecte exatamente uma origem em " + name);
            issues.Add(new(id, name, "Port type is unresolved until an upstream value is connected."));
            return new(name, name, null, required);
        }
        try
        {
        switch (node.Kind)
        {
            case "humanFeedback":
                In("context", new StringType()); Out("feedback", new StringType(), true);
                controlsIn.Add(new("exec")); controlsOut.Add(new("then")); controlsOut.Add(new("timeout")); effect = FlowEffect.Await;
                var human = HumanFeedbackNode.Read(config);
                if (human.TimeoutSeconds is not null && !(controlEdges ?? []).Any(edge => edge.Source == new PortRef(id, "timeout")))
                {
                    if (allowUnresolved) issues.Add(new(id, "timeout", "Connect the timeout execution output when a timeout is configured."));
                    else throw new InvalidDataException("Conecte a saída timeout quando houver timeout configurado.");
                }
                break;
            case "send":
                In("message", new StringType(), true); Out("status", new EnumType(Outbound.SendNodeContract.Statuses), true); Out("diagnostico", new StringType(), true);
                controlsIn.Add(new("exec")); foreach (var name in Outbound.SendNodeContract.Statuses) controlsOut.Add(new(name)); effect = FlowEffect.Send;
                Outbound.SendNodeContract.Validate(config); break;
            case "dm": case "group":
                Out("message", FlowDomain.Message, true); controlsOut.Add(new("then")); mode = FlowNodeExecutionMode.Entry; effect = FlowEffect.Pure; break;
            case "accumulate":
                In("message", FlowDomain.Message, true); Out("messages", new ArrayType(FlowDomain.Message), true);
                controlsIn.Add(new("exec")); controlsOut.Add(new("then")); effect = FlowEffect.Pure; TypedFlowCompiler.ValidateAccumulator(config); break;
            case "history":
                Out("messages", new ArrayType(FlowDomain.Message), true); controlsIn.Add(new("exec")); controlsOut.Add(new("then")); effect = FlowEffect.ReadMemory; break;
            case "exit":
                controlsIn.Add(new("exec")); effect = FlowEffect.Pure;
                var exitSettings = config.GetProperty("settings");
                if (exitSettings.GetProperty("mode").GetString() != "switch") throw new InvalidDataException("Trocar Estado executável aceita somente o modo switch.");
                var hasTarget = exitSettings.TryGetProperty("targetStateId", out var target) && target.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(target.GetString());
                if (hasTarget)
                {
                    if (exitSettings.TryGetProperty("stateMapping", out _)) throw new InvalidDataException("Use targetStateId OU stateMapping.");
                    if ((dataEdges ?? []).Any(edge => edge.Target == new PortRef(id, "state"))) throw new InvalidDataException("Não conecte state quando targetStateId estiver configurado.");
                    break;
                }
                var state = Inferred("state"); In("state", state.Type, true);
                if (state.Type is not EnumType stateEnum)
                { if (state.Type is not null || !allowUnresolved) throw new InvalidDataException("A entrada state deve ser Enum."); }
                else
                {
                    if (!exitSettings.TryGetProperty("stateMapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Configure stateMapping para todos os valores do enum.");
                    var mappings = mapping.EnumerateObject().ToArray();
                    if (mappings.Length > 64 || !mappings.Select(item => item.Name).ToHashSet(StringComparer.Ordinal).SetEquals(stateEnum.Values) || mappings.Any(item => item.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.Value.GetString())))
                        throw new InvalidDataException("stateMapping deve corresponder exatamente aos valores do enum e apontar para IDs preenchidos.");
                }
                break;
            case "memory":
                var persons = incoming("persons");
                FlowType? personType = persons is null ? FlowDomain.Person : persons.Type;
                if (personType is null)
                {
                    if (!allowUnresolved) throw new InvalidDataException("O tipo da entrada persons ainda não foi resolvido.");
                    issues.Add(new(id, "persons", "Port type is unresolved because its upstream contract is unresolved."));
                }
                else if (!MemoryNodeSettings.IsPersonInput(personType))
                {
                    if (!allowUnresolved) throw new InvalidDataException("Memória recebe Pessoa, Pessoa? ou lista dessas pessoas.");
                    issues.Add(new(id, "persons", "Memória recebe Pessoa, Pessoa? ou lista dessas pessoas."));
                }
                In("persons", personType, false); Out("memories", new ArrayType(FlowDomain.Memory), true); controlsIn.Add(new("exec")); controlsOut.Add(new("then")); effect = FlowEffect.ReadMemory;
                try
                {
                    var memory = MemoryNodeSettings.Read(config);
                    inputs[0] = inputs[0] with { Required = memory.Scope == Memory.MemoryScope.Person };
                    var query = memory.QueryText is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(memory.QueryText);
                    In("query", new StringType(), memory.Scope == Memory.MemoryScope.Conversation, query);
                    if (memory.QueryText is not null && string.IsNullOrWhiteSpace(memory.QueryText)) throw new InvalidDataException("Consulta configurada vazia.");
                }
                catch (Exception error) when (allowUnresolved && error is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException)
                {
                    issues.Add(new(id, "config", error.Message));
                    if (!inputs.Any(port => port.Name == "query")) In("query", new StringType());
                }
                break;
            case "count":
                var list = Inferred("list");
                In("list", list.Type, true); Out("count", new NumberType(), true); mode = FlowNodeExecutionMode.Pure; effect = FlowEffect.Pure;
                if (list.Type is not ArrayType && (list.Type is not null || !allowUnresolved)) throw new InvalidDataException("Contar Itens exige Lista não opcional.");
                break;
            case "interpretBoundaries":
                In("memories", new ArrayType(FlowDomain.Memory), true); Out("boundaries", new ArrayType(FlowDomain.Boundary), true);
                controlsIn.Add(new("exec")); controlsOut.Add(new("then")); effect = FlowEffect.Model; _ = BoundaryCompilerSettings.Read(config); break;
            case "property":
                var input = Inferred("object"); In("object", input.Type, true); Out("value", null, true); mode = FlowNodeExecutionMode.Pure; effect = FlowEffect.Pure;
                var path = TypedFlowCompiler.PropertyPath(node.Config);
                if (path.Length == 0) throw new InvalidDataException("Escolha uma propriedade.");
                FlowType? property = input.Type is null ? null : FlowDomain.PropertyType(input.Type, path);
                outputs[0] = outputs[0] with { Type = property }; break;
            case "prompt":
                controlsIn.Add(new("exec")); controlsOut.Add(new("then")); effect = FlowEffect.Model;
                foreach (var port in TypedFlowCompiler.Fields(id, config, "inputs", false, allowUnresolved ? issues : null)) inputs.Add(new(port.Id, port.Name, port.Type, port.Required, port.Config));
                if (BoundaryEnforcementProfile.Enabled(config))
                {
                    var resolved = inputs.Select(port => new TypedPort(port.Id, port.Name, port.Type!, port.Required, default)).ToArray();
                    BoundaryEnforcementProfile.Validate(config, resolved);
                    foreach (var port in BoundaryEnforcementProfile.Outputs) Out(port.Id, port.Type, port.Required);
                }
                else
                {
                    foreach (var port in TypedFlowCompiler.Fields(id, config, "responseFields", true, allowUnresolved ? issues : null)) outputs.Add(new(port.Id, port.Name, port.Type, port.Required, port.Config));
                    if (outputs.Count == 0) Out("message", new StringType(), true);
                    if (TypedFlowCompiler.IsTextOutput(config) && (outputs.Count != 1 || outputs[0].Type is not StringType)) throw new InvalidDataException("Saída Texto exige exatamente um campo Texto.");
                }
                if (config.TryGetProperty("reasoningEffort", out var effort) && (effort.ValueKind != JsonValueKind.String || effort.GetString() is not ("low" or "medium" or "high"))) throw new InvalidDataException("Reasoning effort inválido.");
                if (config.TryGetProperty("maxOutputTokens", out var maxTokens) && (maxTokens.ValueKind != JsonValueKind.Number || !maxTokens.TryGetInt32(out var budget) || budget < 1)) throw new InvalidDataException("Budget de saída deve ser inteiro positivo.");
                if (!config.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(prompt.GetString())) throw new InvalidDataException("Preencha a instrução.");
                InstructionContext.Enabled(config, "usePersonalityCore"); InstructionContext.Enabled(config, "useStateGuidance"); InstructionContext.Enabled(config, "useInterestProfile");
                break;
            case "branch":
                var left = Inferred("a");
                In("a", left.Type, true); controlsIn.Add(new("exec")); controlsOut.Add(new("true")); controlsOut.Add(new("false")); effect = FlowEffect.Pure;
                var settings = config.GetProperty("settings");
                if (settings.TryGetProperty("b", out var literal))
                {
                    In("b", left.Type, true, literal.Clone());
                    if (left.Type is not null) { TypedBranch.Validate(left.Type, left.Type, settings.GetProperty("operator").GetString()); FlowDomain.Check(literal, left.Type); }
                }
                else
                {
                    var right = Inferred("b"); In("b", right.Type, true);
                    if (left.Type is not null && right.Type is not null) TypedBranch.Validate(left.Type, right.Type, settings.GetProperty("operator").GetString());
                }
                break;
            case "switch":
                var value = Inferred("value");
                In("value", value.Type, true); controlsIn.Add(new("exec")); effect = FlowEffect.Pure;
                if (value.Type is not (StringType or EnumType) && (value.Type is not null || !allowUnresolved)) throw new InvalidDataException("Switch recebe Texto ou Enum.");
                var cases = config.GetProperty("settings").GetProperty("cases").EnumerateArray().ToArray();
                var names = cases.Select(item => item.GetProperty("value").GetString()!).ToArray(); var ids = cases.Select(item => item.GetProperty("id").GetString()!).ToArray();
                if (cases.Length > 64 || names.Distinct().Count() != names.Length || ids.Distinct().Count() != ids.Length || ids.Any(string.IsNullOrWhiteSpace) || ids.Contains("other")) throw new InvalidDataException("Casos inválidos ou duplicados.");
                if (value.Type is EnumType enumeration && !enumeration.Values.ToHashSet().SetEquals(names)) throw new InvalidDataException("Atualize os casos para corresponder ao enum.");
                foreach (var caseId in ids) controlsOut.Add(new(caseId)); if (TypedFlowCompiler.HasOther(node.Config)) controlsOut.Add(new("other")); break;
            default:
                if (!definitions.TryGetValue(node.Kind, out var definition))
                { issues.Add(new(id, "kind", "Node kind is unavailable: " + node.Kind)); break; }
                _ = FlowNodeHandler.ValidateDefinition(definition);
                var pluginConfig = FlowNodeHandler.Config(node);
                var resolvedDefinition = definition.Resolve(pluginConfig);
                issues.AddRange(resolvedDefinition.Issues.Select(issue => new TypedFlowIssue(id, "config", issue.Message)));
                if (resolvedDefinition.DataInputs.Concat(resolvedDefinition.DataOutputs).Any(port => port.AcceptsAny)) throw new InvalidDataException("Resolved host data ports must declare concrete types.");
                mode = definition.ExecutionMode; effect = definition.Effect; display = definition.DisplayName ?? definition.Kind; navigation = definition.NavigableGraphReference;
                if (mode == FlowNodeExecutionMode.Pure && (resolvedDefinition.ControlInputs.Count != 0 || resolvedDefinition.ControlOutputs.Count != 0))
                    throw new InvalidDataException("Pure host node resolution cannot expose execution ports.");
                if (mode == FlowNodeExecutionMode.Entry && (resolvedDefinition.DataInputs.Count != 0 || resolvedDefinition.ControlInputs.Count != 0 || resolvedDefinition.ControlOutputs.Any(port => port.Name != "then")))
                    throw new InvalidDataException("Resolved host entries cannot expose execution inputs or data inputs.");
                if (mode == FlowNodeExecutionMode.Action && (resolvedDefinition.ControlInputs.Count != 1 || resolvedDefinition.ControlInputs[0].Name != "exec" || resolvedDefinition.ControlOutputs.Any(port => port.Name != "then")))
                    throw new InvalidDataException("Resolved action host nodes must use exec/then control ports.");
                foreach (var port in resolvedDefinition.DataInputs) In(port.Name, port.Type, port.Required);
                foreach (var port in resolvedDefinition.DataOutputs) Out(port.Name, port.Type, port.Required);
                controlsIn.AddRange(resolvedDefinition.ControlInputs); controlsOut.AddRange(resolvedDefinition.ControlOutputs);
                var inputValues = TypedFlowCompiler.ConfiguredInputValues(id, pluginConfig, resolvedDefinition.DataInputs, allowUnresolved ? issues : null);
                for (var index = 0; index < resolvedDefinition.DataInputs.Count; index++)
                {
                    var port = resolvedDefinition.DataInputs[index];
                    if (inputValues.TryGetValue(port.Name, out var configured)) inputs[index] = inputs[index] with { Config = WrapConstant(configured) };
                }
                break;
        }
        }
        catch (Exception error) when (allowUnresolved && error is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or FlowSchemaException or BoundaryCompilationException or TypedFlowException)
        {
            if (error is TypedFlowException typed) issues.AddRange(typed.Issues);
            else issues.Add(new(id, "config", error.Message));
        }
        if (node.Kind == "prompt") display = "Executar tarefa";
        if (issues.Count > 0 && !allowUnresolved) throw new TypedFlowException(issues);
        return new(node, display, mode, effect, inputs, outputs, controlsIn, controlsOut,
            definitions.GetValueOrDefault(node.Kind)?.ConfigFields ?? [], issues, navigation);
    }

    private static JsonElement WrapConstant(JsonElement? value) => value is { } constant
        ? JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { ["value"] = constant }) : default;
    private static FlowEffect BuiltinEffect(string kind) => kind switch
    {
        "dm" or "group" or "accumulate" or "property" or "count" or "branch" or "switch" or "exit" => FlowEffect.Pure,
        "history" or "memory" => FlowEffect.ReadMemory, "send" => FlowEffect.Send, "humanFeedback" => FlowEffect.Await, _ => FlowEffect.Model
    };
    private static string BuiltinName(string kind) => kind switch
    {
        "dm" => "Mensagem direta", "group" => "Mensagem em grupo", "property" => "Ler propriedade", "count" => "Contar itens",
        "exit" => "Trocar Estado", "branch" => "Condição", "switch" => "Selecionar caso", _ => kind
    };
}
