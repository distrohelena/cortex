using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class ExecutionSnapshotCodecTests
{
    [Fact]
    public void PrivatePromptResponsesSurviveCheckpointWithoutPublicReportReconstruction()
    {
        var state = Snapshot();
        state.PromptResponses.Add("decision", "{\"respond\":true}");
        var restored = ExecutionSnapshotCodec.Decode(ExecutionSnapshotCodec.Encode(state), state.Scope);
        Assert.Equal("{\"respond\":true}", restored.PromptResponses["decision"]);
    }
    internal static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    internal static ExecutionSnapshotV1 Snapshot() => new()
    {
        Scope = DurableIdentityTests.Scope,
        BatchId = "batch",
        ProjectRef = new(1, DurableIdentityTests.Scope, "project", new string('a', 64)),
        BatchRef = new(1, DurableIdentityTests.Scope, "batch", new string('b', 64)),
        StateId = "initial", StateVersion = 3, EntryKind = SessionEntryKind.Dm,
        Cursor = "next", Ordinal = 2, Status = ExecutionStatus.Ready,
    };

    [Fact]
    public void TypeAndValueRoundTrip()
    {
        var state = Snapshot();
        state.Values[new("prompt", "answer")] = new(new EnumType(["yes", "no"]), Json("\"yes\""));
        state.Values[new("input", "optional")] = new(new NullableType(new StringType()), Json("null"));
        state.Values[new("memory", "items")] = new(new ArrayType(FlowDomain.Memory), Json("[{\"conteudo\":\"fact\",\"categoria\":\"fato\",\"_memory\":\"trusted\"}]"));
        state.Outputs["prompt"] = new() { ["answer"] = state.Values[new("prompt", "answer")] };
        state.References["stableId:person"] = "ref1";
        state.Steps.Add(new("prompt", "ok", "then", null));
        state.TrustedEvidence = Json("{\"registry\":\"private\"}");
        var encoded = ExecutionSnapshotCodec.Encode(state);
        var restored = ExecutionSnapshotCodec.Decode(encoded, state.Scope);
        Assert.Equal("yes", restored.Values[new("prompt", "answer")].Value.GetString());
        Assert.Equal(JsonValueKind.Null, restored.Values[new("input", "optional")].Value.ValueKind);
        Assert.Same(FlowDomain.Memory, Assert.IsType<ArrayType>(restored.Values[new("memory", "items")].Type).ElementType);
        Assert.Equal("trusted", restored.Values[new("memory", "items")].Value[0].GetProperty("_memory").GetString());
        Assert.Equal("ref1", restored.References["stableId:person"]);
        Assert.Same(restored.Values[new("prompt", "answer")], restored.Outputs["prompt"]["answer"]);
        Assert.Equal("next", restored.Cursor);
        Assert.Equal(3, restored.StateVersion);
        Assert.Single(restored.Steps);
        Assert.Equal(encoded.GetRawText(), ExecutionSnapshotCodec.Encode(restored).GetRawText());
    }

    [Fact]
    public void NestedDictionaryTypeAndKeysSurviveCheckpointRoundTrip()
    {
        var state = Snapshot();
        var type = new DictionaryType(new NullableType(new ArrayType(new IntegerType())));
        var value = Json("""{"model-A":[8192,4096],"model-B":null}""");
        state.Values[new("settings", "budget")] = new(type, value);

        var encoded = ExecutionSnapshotCodec.Encode(state);
        var restored = ExecutionSnapshotCodec.Decode(encoded, state.Scope);
        var restoredValue = restored.Values[new("settings", "budget")];

        Assert.Equal("dictionary", restoredValue.Type.Kind);
        Assert.Equal(value.GetRawText(), restoredValue.Value.GetRawText());
        Assert.Equal(type.ToJsonSchema().GetRawText(), restoredValue.Type.ToJsonSchema().GetRawText());
        Assert.Equal(encoded.GetRawText(), ExecutionSnapshotCodec.Encode(restored).GetRawText());
    }

    [Fact]
    public void WrongScopeAndUnknownSnapshotVersionFail()
    {
        var state = Snapshot();
        var encoded = ExecutionSnapshotCodec.Encode(state);
        Assert.Throws<InvalidDataException>(() => ExecutionSnapshotCodec.Decode(encoded, state.Scope with { ExecutionId = "other" }));
        var edited = JsonNode.Parse(encoded.GetRawText())!;
        edited["Version"] = 2;
        Assert.Throws<InvalidDataException>(() => ExecutionSnapshotCodec.Decode(JsonSerializer.SerializeToElement(edited), state.Scope));
        edited["Version"] = 1;
        edited["Ordinal"] = -1;
        Assert.Throws<InvalidDataException>(() => ExecutionSnapshotCodec.Decode(JsonSerializer.SerializeToElement(edited), state.Scope));
    }

    [Fact]
    public void InvalidTypedValueCannotBeRestored()
    {
        var state = Snapshot();
        state.Values[new("prompt", "answer")] = new(new BooleanType(), Json("\"not bool\""));
        Assert.Throws<InvalidDataException>(() => ExecutionSnapshotCodec.Encode(state));
    }
}
