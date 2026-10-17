using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Plugin.TestFixture;

public sealed class FixtureNodePlugin : IFlowNodePlugin
{
    public int ContractVersion => 2;
    public IReadOnlyList<NodeDefinition> DescribeNodes(NodePluginContext context)
    {
        if (context.Configuration.TryGetProperty("compoundSchema", out var compound) && compound.ValueKind == JsonValueKind.True)
            return [CompoundDefinition(false)];
        var kind = context.Configuration.TryGetProperty("kind", out var configured) ? configured.GetString()! : "fixture";
        return [Definition(context, kind)];
    }
    public IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context)
    {
        if (context.Configuration.TryGetProperty("runtimeFactoryThrows", out var throwOnBinding) && throwOnBinding.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("Runtime handler factory was accessed.");
        if (context.Configuration.TryGetProperty("compoundSchema", out var compound) && compound.ValueKind == JsonValueKind.True)
        {
            var mismatch = context.Configuration.TryGetProperty("compoundMismatch", out var mismatchValue) && mismatchValue.ValueKind == JsonValueKind.True;
            return [new(CompoundDefinition(mismatch), (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(new Dictionary<string, JsonElement>()))];
        }
        var kind = context.Configuration.TryGetProperty("kind", out var configured) ? configured.GetString()! : "fixture";
        return [new(Definition(context, kind),
            async (_, _, token) =>
            {
                if (context.Configuration.TryGetProperty("wait", out var wait) && wait.GetBoolean()) await Task.Delay(Timeout.Infinite, token);
                if (context.Configuration.TryGetProperty("fail", out var fail) && fail.GetBoolean()) throw new InvalidDataException("Fixture failed");
                IReadOnlyDictionary<string, JsonElement> output = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("fixture-output") };
                return output;
            }, isEntry: true)];
    }

    private static NodeDefinition Definition(NodePluginContext context, string kind) =>
        new(kind, 1, FlowEffect.Pure, dataOutputs: [new("value", new StringType())], controlOutputs: [new("then")])
        {
            ExecutionMode = FlowNodeExecutionMode.Entry,
            NavigableGraphReference = context.Configuration.TryGetProperty("navigationReference", out var reference) && reference.ValueKind == JsonValueKind.String
                ? reference.GetString() : null
        };

    private static NodeDefinition CompoundDefinition(bool mismatch)
    {
        var schema = new ObjectType(new Dictionary<string, FlowField>
        {
            ["name"] = new(mismatch ? new NumberType() : new StringType(), Required: true),
            ["state"] = new(new EnumType(["open", "closed"]), Required: false)
        });
        return new NodeDefinition("fixture", 1, FlowEffect.Pure,
            dataOutputs: [new("value", new ArrayType(schema))],
            configFields: [new("profile", new NullableType(new ArrayType(schema)))],
            controlOutputs: [new("then")]) { ExecutionMode = FlowNodeExecutionMode.Entry };
    }
}

public sealed class UnsupportedPlugin : IFlowNodePlugin
{
    public int ContractVersion => 1;
    public IReadOnlyList<NodeDefinition> DescribeNodes(NodePluginContext context) => [];
    public IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context) => [];
}
