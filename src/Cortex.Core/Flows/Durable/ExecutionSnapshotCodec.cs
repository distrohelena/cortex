using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

internal sealed class ExecutionSnapshotV1
{
    public required ArtifactScope Scope { get; init; }
    public required string BatchId { get; init; }
    public required ArtifactRef ProjectRef { get; init; }
    public required ArtifactRef BatchRef { get; init; }
    public required string StateId { get; init; }
    public long StateVersion { get; set; }
    public SessionEntryKind EntryKind { get; init; }
    public string? Cursor { get; set; }
    public long Ordinal { get; set; }
    public ExecutionStatus Status { get; set; }
    public List<TypedStep> Steps { get; } = [];
    public Dictionary<PortRef, TypedValue> Values { get; } = [];
    public Dictionary<string, Dictionary<string, TypedValue>> Outputs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> References { get; } = new(StringComparer.Ordinal);
    public JsonElement TrustedEvidence { get; set; } = JsonSerializer.SerializeToElement(new { });
    public bool SendAttempted { get; set; }
    public ConfirmedSend? ConfirmedSend { get; set; }
    public string? TargetStateId { get; set; }
}

internal static class ExecutionSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record TypeWire(JsonElement Schema, string? Domain, TypeWire? Element, TypeWire? Inner, Dictionary<string, TypeWire>? Fields);
    private sealed record ValueWire(TypeWire Type, JsonElement Value);
    private sealed record PortWire(string NodeId, string Port, int ValueId);
    private sealed record SnapshotWire(int Version, ArtifactScope Scope, string BatchId, ArtifactRef ProjectRef,
        ArtifactRef BatchRef, string StateId, long StateVersion, SessionEntryKind EntryKind, string? Cursor,
        long Ordinal, ExecutionStatus Status, IReadOnlyList<TypedStep> Steps, IReadOnlyList<PortWire> Values,
        IReadOnlyList<ValueWire> TypedValues, Dictionary<string, Dictionary<string, int>> Outputs, Dictionary<string, string> References,
        JsonElement TrustedEvidence, bool SendAttempted, ConfirmedSend? ConfirmedSend, string? TargetStateId);

    internal static JsonElement Encode(ExecutionSnapshotV1 state)
    {
        Validate(state);
        var ids = new Dictionary<TypedValue, int>(ReferenceEqualityComparer.Instance);
        var table = new List<ValueWire>();
        int ValueId(TypedValue value)
        {
            if (ids.TryGetValue(value, out var id)) return id;
            id = table.Count; ids.Add(value, id); table.Add(EncodeValue(value)); return id;
        }
        var values = state.Values.OrderBy(x => x.Key.NodeId, StringComparer.Ordinal).ThenBy(x => x.Key.Port, StringComparer.Ordinal)
            .Select(x => new PortWire(x.Key.NodeId, x.Key.Port, ValueId(x.Value))).ToArray();
        var outputs = state.Outputs.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key,
            x => x.Value.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => ValueId(p.Value)));
        return JsonSerializer.SerializeToElement(new SnapshotWire(1, state.Scope, state.BatchId, state.ProjectRef,
            state.BatchRef, state.StateId, state.StateVersion, state.EntryKind, state.Cursor, state.Ordinal, state.Status,
            state.Steps.ToArray(), values, table, outputs,
            state.References.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value),
            state.TrustedEvidence.Clone(), state.SendAttempted, state.ConfirmedSend, state.TargetStateId), Options);
    }

    internal static ExecutionSnapshotV1 Decode(JsonElement value, ArtifactScope expectedScope)
    {
        try
        {
            var wire = value.Deserialize<SnapshotWire>(Options) ?? throw new InvalidDataException("Missing snapshot.");
            if (wire.Version != 1 || wire.Scope != expectedScope) throw new InvalidDataException("Unknown snapshot version or wrong scope.");
            var state = new ExecutionSnapshotV1 { Scope = wire.Scope, BatchId = wire.BatchId, ProjectRef = wire.ProjectRef,
                BatchRef = wire.BatchRef, StateId = wire.StateId, StateVersion = wire.StateVersion, EntryKind = wire.EntryKind,
                Cursor = wire.Cursor, Ordinal = wire.Ordinal, Status = wire.Status, TrustedEvidence = wire.TrustedEvidence.Clone(),
                SendAttempted = wire.SendAttempted, ConfirmedSend = wire.ConfirmedSend, TargetStateId = wire.TargetStateId };
            state.Steps.AddRange(wire.Steps);
            var table = wire.TypedValues.Select(DecodeValue).ToArray();
            TypedValue ValueAt(int id) => id >= 0 && id < table.Length ? table[id] : throw new InvalidDataException("Invalid typed value reference.");
            foreach (var port in wire.Values) state.Values.Add(new(port.NodeId, port.Port), ValueAt(port.ValueId));
            foreach (var output in wire.Outputs) state.Outputs.Add(output.Key, output.Value.ToDictionary(p => p.Key, p => ValueAt(p.Value)));
            foreach (var reference in wire.References) state.References.Add(reference.Key, reference.Value);
            Validate(state);
            return state;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException or FlowSchemaException)
        { throw new InvalidDataException("Malformed execution snapshot.", error); }
    }

    private static void Validate(ExecutionSnapshotV1 state)
    {
        DurableIdentity.Validate(state.Scope);
        DurableIdentity.Validate(state.ProjectRef);
        DurableIdentity.Validate(state.BatchRef);
        if (state.ProjectRef.Scope != state.Scope || state.BatchRef.Scope != state.Scope || state.Ordinal < 0 || state.StateVersion < 0
            || !Enum.IsDefined(state.Status) || !Enum.IsDefined(state.EntryKind)) throw new InvalidDataException("Invalid snapshot identity or state.");
        DurableIdentity.Require(state.BatchId);
        DurableIdentity.Require(state.StateId);
    }

    private static ValueWire EncodeValue(TypedValue value)
    { Check(value.Value, value.Type); return new(EncodeType(value.Type), value.Value.Clone()); }
    private static TypedValue DecodeValue(ValueWire wire)
    { var type = DecodeType(wire.Type); Check(wire.Value, type); return new(type, wire.Value.Clone()); }
    private static void Check(JsonElement value, FlowType type)
    {
        if (type is IntegerType)
        { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _)) throw new InvalidDataException("Invalid integer value."); }
        else if (type is ArrayType array)
        { if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid array value."); foreach (var item in value.EnumerateArray()) Check(item, array.ElementType); }
        else if (type is NullableType nullable)
        { if (value.ValueKind != JsonValueKind.Null) Check(value, nullable.InnerType); }
        else if (type is ObjectType obj)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid object value.");
            foreach (var (name, field) in obj.Fields)
                if (value.TryGetProperty(name, out var item)) Check(item, field.Type);
                else if (field.Required) throw new InvalidDataException("Missing required field.");
        }
        else FlowDomain.Check(value, type);
    }

    private static readonly Dictionary<string, FlowType> Domains = new(StringComparer.Ordinal)
    { ["person"] = FlowDomain.Person, ["channel"] = FlowDomain.Channel, ["reply"] = FlowDomain.Reply,
        ["message"] = FlowDomain.Message, ["memory"] = FlowDomain.Memory, ["boundary"] = FlowDomain.Boundary };
    private static TypeWire EncodeType(FlowType type) => new(type.ToJsonSchema(),
        Domains.FirstOrDefault(x => ReferenceEquals(type, x.Value)).Key,
        type is ArrayType array ? EncodeType(array.ElementType) : null,
        type is NullableType nullable ? EncodeType(nullable.InnerType) : null,
        type is ObjectType obj ? obj.Fields.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => EncodeType(x.Value.Type)) : null);
    private static FlowType DecodeType(TypeWire wire)
    {
        var parsed = FlowType.ParseJsonSchema(wire.Schema);
        if (wire.Domain is { } domain)
        {
            if (!Domains.TryGetValue(domain, out var alias) || !SameSchema(parsed, alias)) throw new InvalidDataException("Invalid domain alias.");
            return alias;
        }
        var restored = parsed switch
        {
            ArrayType when wire.Element is not null => new ArrayType(DecodeType(wire.Element)),
            NullableType when wire.Inner is not null => new NullableType(DecodeType(wire.Inner)),
            ObjectType obj when wire.Fields is not null => new ObjectType(obj.Fields.ToDictionary(x => x.Key,
                x => new FlowField(DecodeType(wire.Fields[x.Key]), x.Value.Required))),
            _ => parsed
        };
        if (!SameSchema(parsed, restored)) throw new InvalidDataException("Type metadata does not match schema.");
        return restored;
    }
    private static bool SameSchema(FlowType left, FlowType right) => Canonical(left.ToJsonSchema()) == Canonical(right.ToJsonSchema());
    private static string Canonical(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", json.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", json.EnumerateArray().Select(Canonical)) + "]",
        _ => json.GetRawText()
    };
}
