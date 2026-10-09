using System.Reflection;
using System.Runtime.Loader;

namespace Cortex.Core.Plugins;

internal sealed class NodePluginLoadContext(string path) : AssemblyLoadContext("CortexPlugin-" + Guid.NewGuid().ToString("N"), isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(path);
    protected override Assembly? Load(AssemblyName name)
    {
        var core = typeof(IFlowNodePlugin).Assembly;
        if (name.Name == core.GetName().Name) return core;
        // Framework assemblies are shared with the host; a System.* package the plugin ships (System.Drawing.Common is not
        // part of Microsoft.NETCore.App) loads from the plugin like any other dependency.
        if (name.Name is { } simple && IsPlatform(simple)) return null;
        var dependency = _resolver.ResolveAssemblyToPath(name);
        return dependency is null ? null : LoadFromAssemblyPath(dependency);
    }
    private static readonly HashSet<string> PlatformAssemblies = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFileNameWithoutExtension).OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    internal static bool IsPlatform(string name) =>
        name is "netstandard" or "Microsoft.CSharp" || (name == "System" || name.StartsWith("System.", StringComparison.Ordinal)) && (PlatformAssemblies.Count == 0 || PlatformAssemblies.Contains(name));
    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
