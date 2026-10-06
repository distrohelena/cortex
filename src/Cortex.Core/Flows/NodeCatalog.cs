using System.Collections.ObjectModel;
using System.Text.Json;

namespace Cortex.Core.Flows;

public sealed record FlowDataPortDefinition(string Name, FlowType Type, bool Required = false, bool AcceptsAny = false);

public sealed record FlowControlPortDefinition(string Name, bool Required = false);

public sealed record FlowConfigFieldDefinition(string Name, FlowType Type, bool Required = false)
{
    public string? Label { get; init; }
}

public sealed record NodeDefinitionResolution(
    IReadOnlyList<FlowDataPortDefinition> DataInputs,
    IReadOnlyList<FlowDataPortDefinition> DataOutputs,
    IReadOnlyList<FlowControlPortDefinition> ControlInputs,
    IReadOnlyList<FlowControlPortDefinition> ControlOutputs,
    IReadOnlyList<FlowIssue> Issues)
{
    public static NodeDefinitionResolution From(NodeDefinition definition, IEnumerable<FlowIssue>? issues = null) =>
        new(definition.DataInputs, definition.DataOutputs, definition.ControlInputs, definition.ControlOutputs, (issues ?? []).ToArray());
}

public sealed record NodeDefinition
{
    private readonly Func<JsonElement, NodeDefinitionResolution>? _resolver;

    public NodeDefinition(
        string kind,
        int blockVersion,
        FlowEffect effect,
        IEnumerable<FlowDataPortDefinition>? dataInputs = null,
        IEnumerable<FlowDataPortDefinition>? dataOutputs = null,
        IEnumerable<FlowControlPortDefinition>? controlInputs = null,
        IEnumerable<FlowControlPortDefinition>? controlOutputs = null,
        IEnumerable<FlowConfigFieldDefinition>? configFields = null,
        Func<JsonElement, NodeDefinitionResolution>? resolver = null)
    {
        Kind = kind;
        BlockVersion = blockVersion;
        Effect = effect;
        DataInputs = Freeze(dataInputs);
        DataOutputs = Freeze(dataOutputs);
        ControlInputs = Freeze(controlInputs);
        ControlOutputs = Freeze(controlOutputs);
        ConfigFields = Freeze(configFields);
        _resolver = resolver;
    }

    public string Kind { get; }
    public int BlockVersion { get; }
    public FlowEffect Effect { get; }
    public IReadOnlyList<FlowDataPortDefinition> DataInputs { get; }
    public IReadOnlyList<FlowDataPortDefinition> DataOutputs { get; }
    public IReadOnlyList<FlowControlPortDefinition> ControlInputs { get; }
    public IReadOnlyList<FlowControlPortDefinition> ControlOutputs { get; }
    public IReadOnlyList<FlowConfigFieldDefinition> ConfigFields { get; }

    public NodeDefinitionResolution Resolve(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return NodeDefinitionResolution.From(this);
        }

        var resolved = _resolver?.Invoke(config) ?? NodeDefinitionResolution.From(this);

        var configurationIssues = ConfigFields
            .SelectMany(field => ConfigurationIssues(config, field))
            .ToArray();
        return configurationIssues.Length == 0
            ? resolved
            : resolved with { Issues = resolved.Issues.Concat(configurationIssues).ToArray() };
    }

    internal bool TryGetDataInput(string name, out FlowDataPortDefinition port) => TryGet(DataInputs, name, out port);
    internal bool TryGetDataOutput(string name, out FlowDataPortDefinition port) => TryGet(DataOutputs, name, out port);
    internal bool TryGetControlInput(string name, out FlowControlPortDefinition port) => TryGet(ControlInputs, name, out port);
    internal bool TryGetControlOutput(string name, out FlowControlPortDefinition port) => TryGet(ControlOutputs, name, out port);

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T>? values) =>
        new ReadOnlyCollection<T>((values ?? []).ToArray());

    private static bool TryGet<T>(IEnumerable<T> ports, string name, out T port) where T : class
    {
        port = ports.FirstOrDefault(value => value switch
        {
            FlowDataPortDefinition data => string.Equals(data.Name, name, StringComparison.Ordinal),
            FlowControlPortDefinition control => string.Equals(control.Name, name, StringComparison.Ordinal),
            _ => false,
        })!;
        return port is not null;
    }

    private static IEnumerable<FlowIssue> ConfigurationIssues(JsonElement config, FlowConfigFieldDefinition field)
    {
        if (!config.TryGetProperty(field.Name, out var value))
        {
            if (field.Required)
            {
                yield return new FlowIssue("node.config_field_required", "/config/" + Escape(field.Name), "Required node configuration is missing.");
            }

            yield break;
        }

        if (field.Type is StringType && (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
        {
            yield return new FlowIssue("node.config_field_invalid", "/config/" + Escape(field.Name), "Configuration field has an invalid type or value.");
        }
    }

    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

public sealed class NodeCatalog
{
    public const int MaxBranchCount = 64;
    public const int MaxBranchNameLength = 128;

    private readonly IReadOnlyDictionary<(string Kind, int Version), NodeDefinition> _definitions;

    private NodeCatalog(IEnumerable<NodeDefinition> definitions)
    {
        _definitions = new ReadOnlyDictionary<(string Kind, int Version), NodeDefinition>(
            definitions.ToDictionary(definition => (definition.Kind, definition.BlockVersion)));
        Definitions = new ReadOnlyCollection<NodeDefinition>(_definitions.Values.OrderBy(definition => definition.Kind, StringComparer.Ordinal).ToArray());
    }

    public static NodeCatalog V1 { get; } = CreateV1();

    public IReadOnlyList<NodeDefinition> Definitions { get; }

    public bool TryGet(string kind, int blockVersion, out NodeDefinition definition) =>
        _definitions.TryGetValue((kind, blockVersion), out definition!);

    private static NodeCatalog CreateV1()
    {
        var text = new StringType();
        var any = new StringType();
        var run = new[] { new FlowControlPortDefinition("run") };
        var success = new[] { new FlowControlPortDefinition("success", Required: true) };
        var definitions = new List<NodeDefinition>
        {
            Definition("dm-input", FlowEffect.Pure, dataOutputs: [Port("text", text)], controlOutputs: success),
            Definition("group-input", FlowEffect.Pure, dataOutputs: [Port("text", text)], controlOutputs: success),
            Definition("manual-input", FlowEffect.Pure, dataOutputs: [Port("text", text)], controlOutputs: success),
            Definition("character-dispatch", FlowEffect.Pure, dataInputs: [Port("input", any, Required: true, AcceptsAny: true)], dataOutputs: [Port("result", any, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("characterId", text, Required: true)]),
            Definition("context", FlowEffect.Pure, dataOutputs: [Port("text", text)], controlInputs: run, controlOutputs: success),
            Definition("burst", FlowEffect.Pure, dataInputs: [Port("text", text, AcceptsAny: true)], dataOutputs: [Port("text", text)], controlInputs: run, controlOutputs: success),
            Definition("ai", FlowEffect.Model, dataInputs: [Port("prompt", text, Required: true)], dataOutputs: [Port("text", text)], controlInputs: run, controlOutputs: success, configFields: [Field("modelProfileId", text)], resolver: ResolveAi),
            Definition("condition", FlowEffect.Pure, dataInputs: [Port("value", any, Required: true, AcceptsAny: true)], controlInputs: run, resolver: ResolveCondition),
            Definition("parallel", FlowEffect.Pure, controlInputs: run, resolver: ResolveBranches),
            Definition("join", FlowEffect.Pure, resolver: ResolveJoin),
            Definition("await", FlowEffect.Await, controlInputs: run, resolver: ResolveAwait),
            Definition("humanFeedback", FlowEffect.Await, dataInputs: [Port("context", text)],
                dataOutputs: [Port("feedback", text)], controlInputs: run, controlOutputs: [new("then"), new("timeout")], resolver: HumanFeedbackNode.Resolve),
            Definition("history-record", FlowEffect.WriteMemory, dataInputs: [Port("value", any, Required: true, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("history-read", FlowEffect.ReadMemory, dataOutputs: [Port("result", any, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("memory-learn", FlowEffect.WriteMemory, dataInputs: [Port("value", any, Required: true, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("memory-read", FlowEffect.ReadMemory, dataOutputs: [Port("result", any, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("state-read", FlowEffect.ReadMemory, dataOutputs: [Port("value", any, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("state-update", FlowEffect.WriteState, dataInputs: [Port("value", any, Required: true, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("spaceId", text)]),
            Definition("protector", FlowEffect.Pure, dataInputs: [Port("request", any, Required: true, AcceptsAny: true)], dataOutputs: [Port("result", any, AcceptsAny: true)], controlInputs: run, controlOutputs: success, configFields: [Field("protectorId", text)]),
            Definition("send", FlowEffect.Send, dataInputs: [Port("body", text, Required: true)], controlInputs: run, controlOutputs: [new FlowControlPortDefinition("success")], configFields: [Field("destinationId", text, Required: true)]),
        };

        return new NodeCatalog(definitions);
    }

    private static NodeDefinition Definition(
        string kind,
        FlowEffect effect,
        IEnumerable<FlowDataPortDefinition>? dataInputs = null,
        IEnumerable<FlowDataPortDefinition>? dataOutputs = null,
        IEnumerable<FlowControlPortDefinition>? controlInputs = null,
        IEnumerable<FlowControlPortDefinition>? controlOutputs = null,
        IEnumerable<FlowConfigFieldDefinition>? configFields = null,
        Func<JsonElement, NodeDefinitionResolution>? resolver = null) =>
        new(kind, 1, effect, dataInputs, dataOutputs, controlInputs, controlOutputs, configFields, resolver);

    private static FlowDataPortDefinition Port(string name, FlowType type, bool Required = false, bool AcceptsAny = false) =>
        new(name, type, Required, AcceptsAny);

    private static FlowConfigFieldDefinition Field(string name, FlowType type, bool Required = false) =>
        new(name, type, Required);

    private static NodeDefinitionResolution ResolveAi(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        var outputSchema = Find(config, "outputSchema", "schema", "outputType");
        if (outputSchema is null)
        {
            return new NodeDefinitionResolution([Port("prompt", new StringType(), Required: true)], [Port("text", new StringType())], [new("run")], [new("success", Required: true)], issues);
        }

        if (outputSchema.Value.ValueKind == JsonValueKind.String && string.Equals(outputSchema.Value.GetString(), "text", StringComparison.OrdinalIgnoreCase))
        {
            return new NodeDefinitionResolution([Port("prompt", new StringType(), Required: true)], [Port("text", new StringType())], [new("run")], [new("success", Required: true)], issues);
        }

        if (outputSchema.Value.ValueKind == JsonValueKind.String && string.Equals(outputSchema.Value.GetString(), "structured", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new FlowIssue("node.config_schema_required", "/config/outputSchema", "Structured AI output requires a supported schema."));
            return AiResolution(new StringType(), issues, structured: false);
        }

        try
        {
            var schema = outputSchema.Value.ValueKind == JsonValueKind.Object && outputSchema.Value.TryGetProperty("kind", out _)
                ? FlowType.ParsePortable(outputSchema.Value)
                : FlowType.ParseJsonSchema(outputSchema.Value);
            return AiResolution(schema, issues, structured: true);
        }
        catch (FlowSchemaException exception)
        {
            issues.Add(exception.Issue with { Path = "/config/outputSchema" + (exception.Issue.Path == "/" ? string.Empty : exception.Issue.Path) });
            return AiResolution(new StringType(), issues, structured: false);
        }
    }

    private static NodeDefinitionResolution AiResolution(FlowType outputType, IEnumerable<FlowIssue> issues, bool structured)
    {
        var outputs = structured
            ? new[] { Port("output", outputType, AcceptsAny: true) }
            : new[] { Port("text", new StringType()) };
        return new NodeDefinitionResolution([Port("prompt", new StringType(), Required: true)], outputs, [new("run")], [new("success", Required: true)], issues.ToArray());
    }

    private static NodeDefinitionResolution ResolveCondition(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        if (config.TryGetProperty("fieldPath", out var fieldPath))
        {
            if (fieldPath.ValueKind != JsonValueKind.String && fieldPath.ValueKind != JsonValueKind.Array ||
                fieldPath.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(fieldPath.GetString()) ||
                fieldPath.ValueKind == JsonValueKind.Array && fieldPath.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
            {
                issues.Add(new FlowIssue("node.condition_field_invalid", "/config/fieldPath", "Condition field path must be a non-empty field name or string array."));
            }
        }
        else
        {
            RequireString(config, "field", issues);
        }
        if (!config.TryGetProperty("field", out _) && !config.TryGetProperty("fieldPath", out _))
        {
            issues.Add(new FlowIssue("node.condition_field_required", "/config/field", "Condition requires a selected field."));
        }

        var operatorElement = config.TryGetProperty("operator", out var configuredOperator)
            ? configuredOperator
            : config.TryGetProperty("op", out var shorthandOperator) ? shorthandOperator : default;
        if (operatorElement.ValueKind == JsonValueKind.Undefined)
        {
            issues.Add(new FlowIssue("node.condition_operator_required", "/config/operator", "Condition requires a typed comparison operator."));
        }
        else if (operatorElement.ValueKind != JsonValueKind.String || !IsSupportedOperator(operatorElement.GetString()))
        {
            issues.Add(new FlowIssue("node.condition_operator_invalid", "/config/operator", "Condition operator is not supported for typed comparison."));
        }

        if (!config.TryGetProperty("value", out _))
        {
            issues.Add(new FlowIssue("node.condition_literal_required", "/config/value", "Condition requires a literal value."));
        }

        return new NodeDefinitionResolution([Port("value", new StringType(), Required: true, AcceptsAny: true)], [], [new("run")], [new("true", Required: true), new("false", Required: true)], issues);
    }

    private static bool IsSupportedOperator(string? value) => value?.ToLowerInvariant() is
        "equals" or "eq" or "notequals" or "ne" or "lessthan" or "lt" or "lessorequal" or "lte" or "greaterthan" or "gt" or "greaterorequal" or "gte" or
        "contains" or "startswith" or "endswith" or "in";

    private static bool IsSupportedJoinMode(JsonElement mode)
    {
        if (mode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return mode.GetString()?.ToLowerInvariant() is "all" or "first-valid" or "firstvalid" or "independent";
    }

    private static NodeDefinitionResolution ResolveBranches(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        var branches = ReadNames(config, "branches", issues);
        return new NodeDefinitionResolution([], [], [new("run")], branches.Select(name => new FlowControlPortDefinition(name, Required: true)).ToArray(), issues);
    }

    private static NodeDefinitionResolution ResolveJoin(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        var branches = ReadNames(config, "branches", issues);
        if (!config.TryGetProperty("mode", out var mode) || !IsSupportedJoinMode(mode))
        {
            issues.Add(new FlowIssue("node.join_mode_invalid", "/config/mode", "Join requires mode all, first-valid, or independent."));
        }

        var inputs = branches.Select(name => Port(name, new StringType(), AcceptsAny: true)).ToArray();
        var outputs = new List<FlowDataPortDefinition> { Port("result", new StringType(), AcceptsAny: true) };
        outputs.AddRange(branches.Select(name => Port(name, new StringType(), AcceptsAny: true)));
        return new NodeDefinitionResolution(inputs, outputs, branches.Select(name => new FlowControlPortDefinition(name, Required: true)).ToArray(), [], issues);
    }

    private static NodeDefinitionResolution ResolveAwait(JsonElement config)
    {
        var issues = new List<FlowIssue>();
        if (!config.TryGetProperty("resumePredicate", out var predicate) || predicate.ValueKind is JsonValueKind.Null)
        {
            issues.Add(new FlowIssue("node.await_predicate_required", "/config/resumePredicate", "Await requires an explicit resume predicate."));
        }
        else
        {
            ValidateResumePredicate(predicate, issues);
        }

        RequireString(config, "deadline", issues);
        return new NodeDefinitionResolution([], [], [new("run")], [new("resumed", Required: true), new("timeout", Required: true)], issues);
    }

    private static JsonElement? Find(JsonElement config, params string[] names)
    {
        foreach (var name in names)
        {
            if (config.TryGetProperty(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static void RequireString(JsonElement config, string name, ICollection<FlowIssue> issues)
    {
        if (!config.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            issues.Add(new FlowIssue("node.config_field_invalid", "/config/" + name, "Configuration field must be a non-empty string."));
        }
    }

    private static string[] ReadNames(JsonElement config, string name, ICollection<FlowIssue> issues)
    {
        if (!config.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new FlowIssue("node.branches_required", "/config/" + name, "Node requires a non-empty named branch array."));
            return [];
        }

        var count = value.GetArrayLength();
        if (count > MaxBranchCount)
        {
            issues.Add(new FlowIssue("node.branches_invalid", "/config/" + name, $"Branch count must not exceed {MaxBranchCount}."));
            return [];
        }

        var names = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invalid = count == 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                invalid = true;
                continue;
            }

            var branch = item.GetString()!;
            if (branch.Length > MaxBranchNameLength || !seen.Add(branch))
            {
                invalid = true;
                continue;
            }

            names.Add(branch);
        }

        if (invalid)
        {
            issues.Add(new FlowIssue("node.branches_invalid", "/config/" + name, $"Branch names must be non-empty, unique, and at most {MaxBranchNameLength} characters."));
            return [];
        }

        return names.ToArray();
    }

    private static void ValidateResumePredicate(JsonElement predicate, ICollection<FlowIssue> issues)
    {
        const int maxStringLength = 128;
        if (predicate.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new FlowIssue("node.await_predicate_invalid", "/config/resumePredicate", "Resume predicate must be a bounded event predicate object."));
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var invalid = false;
        foreach (var property in predicate.EnumerateObject())
        {
            if (!names.Add(property.Name) || property.Name is not ("eventKind" or "conversationId" or "actorId" or "correlationId"))
            {
                invalid = true;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()) || property.Value.GetString()!.Length > maxStringLength)
            {
                invalid = true;
            }
        }

        if (!predicate.TryGetProperty("eventKind", out var eventKind) ||
            eventKind.ValueKind != JsonValueKind.String ||
            eventKind.GetString() is not ("dm" or "group" or "manual"))
        {
            invalid = true;
        }

        if (!predicate.TryGetProperty("conversationId", out var conversationId) ||
            conversationId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(conversationId.GetString()) ||
            conversationId.GetString()!.Length > maxStringLength)
        {
            invalid = true;
        }

        if (invalid)
        {
            issues.Add(new FlowIssue("node.await_predicate_invalid", "/config/resumePredicate", "Resume predicate must contain only bounded eventKind, conversationId, actorId, and correlationId fields."));
        }
    }
}
