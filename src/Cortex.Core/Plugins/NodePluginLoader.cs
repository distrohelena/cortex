using System.Reflection;
using System.Collections.ObjectModel;
using Cortex.Core.Flows;

namespace Cortex.Core.Plugins;

public sealed record NodeLibrary(string Id, string DisplayName);

public sealed record PluginLoadReport(string Id, bool Enabled, bool Loaded, string? Error);

public sealed class LoadedNodePlugins : IDisposable
{
    private readonly List<NodePluginLoadContext> _contexts;
    internal LoadedNodePlugins(IReadOnlyList<FlowNodeHandler> handlers, IReadOnlyList<PluginLoadReport> reports, IReadOnlyDictionary<string, NodeLibrary> librariesByKind, List<NodePluginLoadContext> contexts)
    { Handlers = handlers; Reports = reports; LibrariesByKind = librariesByKind; _contexts = contexts; }
    public IReadOnlyList<FlowNodeHandler> Handlers { get; private set; }
    public IReadOnlyList<PluginLoadReport> Reports { get; }
    public IReadOnlyDictionary<string, NodeLibrary> LibrariesByKind { get; }
    public void Dispose()
    {
        Handlers = [];
        foreach (var context in _contexts) context.Unload();
        _contexts.Clear();
    }
}

public static class NodePluginLoader
{
    public static LoadedNodePlugins Load(CortexEditorSettings settings, IReadOnlyList<NodePluginManifest> manifests,
        string configurationDirectory, Action<string>? diagnostic = null)
    {
        settings.Validate();
        var handlers = new List<FlowNodeHandler>();
        var reports = new List<PluginLoadReport>();
        var libraries = new Dictionary<string, NodeLibrary>(StringComparer.Ordinal);
        var contexts = new List<NodePluginLoadContext>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            if (!ids.Add(manifest.Id)) throw new InvalidDataException("Manifestos com ID duplicado: " + manifest.Id);
            var setting = settings.Plugins.SingleOrDefault(entry => entry.Id == manifest.Id);
            // This check precedes any filesystem access, assembly/type resolution, or plugin construction.
            if (setting?.Enabled != true) { reports.Add(new(manifest.Id, false, false, null)); continue; }
            NodePluginLoadContext? context = null;
            try
            {
                if (manifest.ContractVersion != NodePluginContext.CurrentContractVersion) throw new InvalidDataException("Versão de contrato de plugin incompatível.");
                var path = Path.GetFullPath(manifest.AssemblyPath);
                context = new(path);
                var assembly = context.LoadFromAssemblyPath(path);
                var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
                if (!typeof(IFlowNodePlugin).IsAssignableFrom(type)) throw new InvalidDataException("O tipo não implementa o contrato de plugin do Cortex.");
                var plugin = (IFlowNodePlugin)Activator.CreateInstance(type)!;
                if (plugin.ContractVersion != NodePluginContext.CurrentContractVersion) throw new InvalidDataException("Versão de contrato de plugin incompatível.");
                var added = plugin.CreateHandlers(new(setting.Configuration.Clone(), Path.GetFullPath(configurationDirectory), diagnostic)).ToArray();
                if (added.Any(handler => handler is null || TypedFlowCompiler.IsBuiltinKind(handler.Definition.Kind)) ||
                    added.Select(handler => handler.Definition.Kind).Distinct(StringComparer.Ordinal).Count() != added.Length ||
                    added.Any(handler => handlers.Any(existing => existing.Definition.Kind == handler.Definition.Kind)))
                    throw new InvalidDataException("O plugin contém kinds duplicados ou reservados pelo Cortex.");
                handlers.AddRange(added);
                var library = new NodeLibrary(manifest.Id, string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName);
                foreach (var handler in added) libraries.Add(handler.Definition.Kind, library);
                contexts.Add(context);
                context = null;
                reports.Add(new(manifest.Id, true, true, null));
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            { reports.Add(new(manifest.Id, true, false, error.GetBaseException().Message)); }
            finally { context?.Unload(); }
        }
        foreach (var setting in settings.Plugins.Where(setting => !ids.Contains(setting.Id)))
            reports.Add(new(setting.Id, setting.Enabled, false, setting.Enabled ? "Plugin não instalado." : null));
        return new(handlers.AsReadOnly(), reports.AsReadOnly(), new ReadOnlyDictionary<string, NodeLibrary>(libraries), contexts);
    }
}
