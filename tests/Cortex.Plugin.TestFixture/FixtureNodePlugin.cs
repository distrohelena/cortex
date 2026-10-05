using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;

namespace Cortex.Plugin.TestFixture;

public sealed class FixtureNodePlugin : IFlowNodePlugin
{
    public int ContractVersion => 1;
    public IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context)
    {
        var kind = context.Configuration.TryGetProperty("kind", out var configured) ? configured.GetString()! : "fixture";
        return [new(new NodeDefinition(kind, 1, FlowEffect.Pure, dataOutputs: [new("value", new StringType())]),
            async (_, _, token) =>
            {
                if (context.Configuration.TryGetProperty("wait", out var wait) && wait.GetBoolean()) await Task.Delay(Timeout.Infinite, token);
                if (context.Configuration.TryGetProperty("fail", out var fail) && fail.GetBoolean()) throw new InvalidDataException("Fixture failed");
                IReadOnlyDictionary<string, JsonElement> output = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("fixture-output") };
                return output;
            }, isEntry: true)];
    }
}

public sealed class UnsupportedPlugin : IFlowNodePlugin
{
    public int ContractVersion => 2;
    public IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context) => [];
}
