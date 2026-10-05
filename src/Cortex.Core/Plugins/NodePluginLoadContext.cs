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
        if (name.Name is "System" or "netstandard" or "Microsoft.CSharp" || name.Name?.StartsWith("System.", StringComparison.Ordinal) == true) return null;
        var dependency = _resolver.ResolveAssemblyToPath(name);
        return dependency is null ? null : LoadFromAssemblyPath(dependency);
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
