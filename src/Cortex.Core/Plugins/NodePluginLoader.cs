using System.Reflection;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Core.Plugins;

public sealed record NodeLibrary(string Id, string DisplayName);

public sealed record PluginLoadReport(string Id, bool Enabled, bool Loaded, string? Error);

public sealed class LoadedNodePlugins : IDisposable
{
    private readonly object _sync = new();
    private List<NodePluginLoadContext>? _contexts;
    private IReadOnlyList<(IFlowNodePlugin Plugin, NodePluginContext Context, IReadOnlyList<NodeDefinition> Definitions)>? _plugins;
    private IReadOnlyList<FlowNodeHandler>? _handlers;
    private ExceptionDispatchInfo? _bindFailure;
    private bool _disposed;
    internal LoadedNodePlugins(IReadOnlyList<NodeDefinition> definitions, IReadOnlyList<PluginLoadReport> reports,
        IReadOnlyDictionary<string, NodeLibrary> librariesByKind, List<NodePluginLoadContext> contexts,
        IReadOnlyList<(IFlowNodePlugin Plugin, NodePluginContext Context, IReadOnlyList<NodeDefinition> Definitions)> plugins)
    {
        Definitions = definitions; Reports = reports; LibrariesByKind = librariesByKind; _contexts = contexts; _plugins = plugins;
    }
    public IReadOnlyList<NodeDefinition> Definitions { get; private set; }
    public IReadOnlyList<PluginLoadReport> Reports { get; }
    public IReadOnlyDictionary<string, NodeLibrary> LibrariesByKind { get; }
    /// <summary>Explicitly creates runtime handlers. Discovery and editor metadata never call this method.</summary>
    public IReadOnlyList<FlowNodeHandler> BindHandlers()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handlers is not null) return _handlers;
            _bindFailure?.Throw();
            try
            {
                _handlers = Bind(_plugins ?? []);
                _plugins = null;
                return _handlers;
            }
            catch (Exception error)
            {
                _bindFailure = ExceptionDispatchInfo.Capture(error);
                throw;
            }
        }
    }

    private IReadOnlyList<FlowNodeHandler> Bind(IReadOnlyList<(IFlowNodePlugin Plugin, NodePluginContext Context, IReadOnlyList<NodeDefinition> Definitions)> plugins)
    {
        var handlers = new List<FlowNodeHandler>();
        foreach (var (plugin, context, metadata) in plugins)
        {
            var created = plugin.CreateHandlers(context).ToArray();
            if (created.Length != metadata.Count || created.Any(handler => handler is null) ||
                created.Select(handler => handler.Definition.Kind).Distinct(StringComparer.Ordinal).Count() != created.Length ||
                created.Any(handler => !metadata.Any(definition => SameContract(handler.Definition, definition))))
                throw new InvalidDataException("Runtime handlers do not match the plugin's metadata contract.");
            handlers.AddRange(created);
        }
        return handlers.AsReadOnly();
    }

    private static bool SameContract(NodeDefinition runtime, NodeDefinition metadata)
    {
        if (runtime.Kind != metadata.Kind || runtime.BlockVersion != metadata.BlockVersion || runtime.Effect != metadata.Effect ||
            runtime.ExecutionMode != metadata.ExecutionMode || runtime.DisplayName != metadata.DisplayName ||
            runtime.NavigableGraphReference != metadata.NavigableGraphReference) return false;
        return SamePorts(runtime.DataInputs, metadata.DataInputs) && SamePorts(runtime.DataOutputs, metadata.DataOutputs) &&
            runtime.ControlInputs.SequenceEqual(metadata.ControlInputs) && runtime.ControlOutputs.SequenceEqual(metadata.ControlOutputs) &&
            runtime.ConfigFields.Count == metadata.ConfigFields.Count && runtime.ConfigFields.Zip(metadata.ConfigFields).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.Required == pair.Second.Required && pair.First.Label == pair.Second.Label &&
                JsonElement.DeepEquals(pair.First.Type.ToJsonSchema(), pair.Second.Type.ToJsonSchema()));
    }

    private static bool SamePorts(IReadOnlyList<FlowDataPortDefinition> left, IReadOnlyList<FlowDataPortDefinition> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Name == pair.Second.Name &&
            pair.First.Required == pair.Second.Required && pair.First.AcceptsAny == pair.Second.AcceptsAny &&
            JsonElement.DeepEquals(pair.First.Type.ToJsonSchema(), pair.Second.Type.ToJsonSchema()));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            Definitions = [];
            _plugins = null;
            _handlers = null;
            _bindFailure = null;
            if (_contexts is not null)
            {
                foreach (var context in _contexts) context.Unload();
                _contexts.Clear();
                _contexts = null;
            }
        }
    }
}

public static class NodePluginLoader
{
    public static LoadedNodePlugins Load(CortexEditorSettings settings, IReadOnlyList<NodePluginManifest> manifests,
        string configurationDirectory, Action<string>? diagnostic = null)
    {
        settings.Validate();
        var definitions = new List<NodeDefinition>();
        var reports = new List<PluginLoadReport>();
        var libraries = new Dictionary<string, NodeLibrary>(StringComparer.Ordinal);
        var contexts = new List<NodePluginLoadContext>();
        var plugins = new List<(IFlowNodePlugin Plugin, NodePluginContext Context, IReadOnlyList<NodeDefinition> Definitions)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            if (!ids.Add(manifest.Id)) throw new InvalidDataException("Manifestos com ID duplicado: " + manifest.Id);
            var setting = settings.Plugins.SingleOrDefault(entry => entry.Id == manifest.Id);
            // This check precedes filesystem access, assembly/type resolution, and plugin construction.
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
                var pluginContext = new NodePluginContext(setting.Configuration.Clone(), Path.GetFullPath(configurationDirectory), diagnostic);
                var added = plugin.DescribeNodes(pluginContext).ToArray();
                if (added.Any(definition => definition is null || TypedFlowCompiler.IsBuiltinKind(definition.Kind)) ||
                    added.Select(definition => definition.Kind).Distinct(StringComparer.Ordinal).Count() != added.Length ||
                    added.Any(definition => definitions.Any(existing => existing.Kind == definition.Kind)))
                    throw new InvalidDataException("O plugin contém kinds duplicados ou reservados pelo Cortex.");
                definitions.AddRange(added);
                var library = new NodeLibrary(manifest.Id, string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName);
                foreach (var definition in added) libraries.Add(definition.Kind, library);
                contexts.Add(context);
                context = null;
                plugins.Add((plugin, pluginContext, added));
                reports.Add(new(manifest.Id, true, true, null));
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            { reports.Add(new(manifest.Id, true, false, error.GetBaseException().Message)); }
            finally { context?.Unload(); }
        }
        foreach (var setting in settings.Plugins.Where(setting => !ids.Contains(setting.Id)))
            reports.Add(new(setting.Id, setting.Enabled, false, setting.Enabled ? "Plugin não instalado." : null));
        return new(definitions.AsReadOnly(), reports.AsReadOnly(), new ReadOnlyDictionary<string, NodeLibrary>(libraries), contexts, plugins.AsReadOnly());
    }
}
