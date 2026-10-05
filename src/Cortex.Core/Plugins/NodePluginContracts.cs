using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Plugins;

public interface IFlowNodePlugin
{
    int ContractVersion { get; }
    IReadOnlyList<FlowNodeHandler> CreateHandlers(NodePluginContext context);
}

public sealed record NodePluginContext(JsonElement Configuration, string ConfigurationDirectory, Action<string>? Diagnostic = null)
{
    public const int CurrentContractVersion = 1;

    public static string ResolvePath(string value, string directory, bool allowCommandName = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (allowCommandName && !Path.IsPathRooted(value) && value.IndexOfAny(['/', '\\']) < 0) return value;
        return Path.GetFullPath(value, Path.GetFullPath(directory));
    }
}
