using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Plugins;

public interface IFlowNodePlugin
{
    int ContractVersion { get; }
    /// <summary>Returns static node contracts only; implementations must not create runtime resources here.</summary>
    IReadOnlyList<NodeDefinition> DescribeNodes(NodePluginContext context);
    IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context);
}

public sealed record NodePluginContext(JsonElement Configuration, string ConfigurationDirectory, Action<string>? Diagnostic = null)
{
    public const int CurrentContractVersion = 2;

    public static string ResolvePath(string value, string directory, bool allowCommandName = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (allowCommandName && !Path.IsPathRooted(value) && value.IndexOfAny(['/', '\\']) < 0) return value;
        return Path.GetFullPath(value, Path.GetFullPath(directory));
    }
}
