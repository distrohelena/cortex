using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Cortex.Core.Flows;

public sealed record FlowCompilationResult(
    CompiledFlowRelease? Release,
    IReadOnlyList<FlowIssue> Issues);

public static class FlowCompiler
{
    public const int PortableArtifactFormatVersion = 1;
    public const int CatalogVersion = 1;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static FlowCompilationResult Compile(
        FlowWorkspaceDefinition workspace,
        IReadOnlyList<EnvironmentBindings> completeEnvironmentRegistry,
        FlowEnvironment targetEnvironment,
        string releaseId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(completeEnvironmentRegistry);

        var issues = new List<FlowIssue>();
        issues.AddRange(FlowWorkspaceShape.Validate(workspace, completeEnvironmentRegistry));
        if (issues.Count != 0)
        {
            return new FlowCompilationResult(null, FreezeIssues(issues));
        }

        if (string.IsNullOrWhiteSpace(releaseId))
        {
            issues.Add(new FlowIssue("compiler.release_id_invalid", "/releaseId", "Release ID must be non-empty."));
        }

        foreach (var graph in workspace.Graphs)
        {
            foreach (var issue in FlowValidator.Validate(graph, NodeCatalog.V1))
            {
                issues.Add(PrefixGraphIssue(graph.Id, issue));
            }
        }

        issues.AddRange(FlowBindingValidator.Validate(workspace, completeEnvironmentRegistry));
        var targetBindings = completeEnvironmentRegistry
            .Where(binding => binding.Environment == targetEnvironment)
            .ToArray();
        if (targetBindings.Length == 0)
        {
            issues.Add(new FlowIssue("binding.environment_missing", $"/environments/{WireEnvironment(targetEnvironment)}", "Target environment is not present in the complete environment registry."));
        }

        var orderedIssues = FreezeIssues(issues);
        if (orderedIssues.Any(issue => issue.Severity == FlowIssueSeverity.Error) || targetBindings.Length != 1)
        {
            return new FlowCompilationResult(null, orderedIssues);
        }

        var release = CompiledFlowRelease.Create(
            targetEnvironment,
            releaseId,
            workspace.Characters.Select(CompiledCharacterDefinition.Create),
            workspace.Graphs.Select(FreezeGraph),
            workspace.ModelProfiles.Select(CompiledModelProfileDefinition.Create),
            workspace.CharacterModelProfileReferences,
            FreezeEnvironmentBindings(targetBindings[0]));

        return new FlowCompilationResult(release, orderedIssues);
    }

    public static string Export(CompiledFlowRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("artifactFormatVersion", PortableArtifactFormatVersion);
            writer.WriteNumber("catalogVersion", CatalogVersion);
            writer.WriteString("releaseId", release.ReleaseId);
            writer.WriteString("environment", WireEnvironment(release.Environment));
            writer.WritePropertyName("workspace");
            writer.WriteStartObject();

            writer.WritePropertyName("characters");
            writer.WriteStartArray();
            foreach (var character in release.Characters.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", character.Id);
                writer.WriteString("displayName", character.DisplayName);
                writer.WriteString("personality", character.Personality);
                writer.WriteString("graphId", character.GraphId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("graphs");
            writer.WriteStartArray();
            foreach (var graph in release.Graphs.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                WriteGraph(writer, graph);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("modelProfiles");
            writer.WriteStartArray();
            foreach (var profile in release.ModelProfiles.OrderBy(value => value.ProfileId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("profileId", profile.ProfileId);
                writer.WriteString("credentialReference", profile.CredentialReference);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("modelProfileReferences");
            writer.WriteStartObject();
            foreach (var reference in release.CharacterModelProfileReferences.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WriteString(reference.Key, reference.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return StrictUtf8.GetString(stream.ToArray());
    }

    public static FlowCompilationResult Import(
        string artifact,
        IReadOnlyList<EnvironmentBindings> completeEnvironmentRegistry,
        FlowEnvironment targetEnvironment)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(completeEnvironmentRegistry);

        var parsed = ReadArtifact(artifact);
        if (parsed.Workspace is null)
        {
            return new FlowCompilationResult(null, FreezeIssues(parsed.Issues));
        }

        return Compile(parsed.Workspace, completeEnvironmentRegistry, targetEnvironment, parsed.ReleaseId!);
    }

    private static CompiledFlowGraph FreezeGraph(GraphDocument graph)
    {
        var definitions = new Dictionary<string, (FlowNode Node, NodeDefinition Definition)>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (NodeCatalog.V1.TryGet(node.Kind, node.BlockVersion, out var definition))
            {
                definitions[node.Id] = (node, definition);
            }
        }
        var order = TopologicalOrder(graph, definitions.Keys);
        var nodes = order.Select(id =>
        {
            var state = definitions[id];
            return CompiledNodePlan.Create(state.Node, state.Definition);
        });

        var dataEdges = graph.DataEdges
            .OrderBy(edge => edge.Source.NodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Source.Port, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target.NodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target.Port, StringComparer.Ordinal)
            .ThenBy(edge => string.Join("\u001f", edge.FieldPath), StringComparer.Ordinal)
            .Select(edge => new DataEdge(
                new PortRef(edge.Source.NodeId, edge.Source.Port),
                ImmutableArray.CreateRange(edge.FieldPath),
                new PortRef(edge.Target.NodeId, edge.Target.Port)));
        var controlEdges = graph.ControlEdges
            .OrderBy(edge => edge.Source.NodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Source.Port, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target.NodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target.Port, StringComparer.Ordinal)
            .Select(edge => new ControlEdge(
                new PortRef(edge.Source.NodeId, edge.Source.Port),
                new PortRef(edge.Target.NodeId, edge.Target.Port)));

        return CompiledFlowGraph.Create(
            graph.Id,
            graph.FormatVersion,
            graph.Role,
            graph.CharacterId,
            nodes,
            dataEdges,
            controlEdges,
            graph.EntryNodeIds.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> TopologicalOrder(
        GraphDocument graph,
        IEnumerable<string> validNodeIds)
    {
        var ids = validNodeIds.ToHashSet(StringComparer.Ordinal);
        var adjacency = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var edge in graph.ControlEdges)
        {
            AddDependency(adjacency, edge.Source.NodeId, edge.Target.NodeId);
        }

        foreach (var edge in graph.DataEdges)
        {
            AddDependency(adjacency, edge.Source.NodeId, edge.Target.NodeId);
        }

        var indegree = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var targets in adjacency.Values)
        {
            foreach (var target in targets)
            {
                indegree[target]++;
            }
        }

        var ready = new SortedSet<string>(ids.Where(id => indegree[id] == 0), StringComparer.Ordinal);
        var result = new List<string>(ids.Count);
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            result.Add(id);
            foreach (var target in adjacency[id].Order(StringComparer.Ordinal))
            {
                if (--indegree[target] == 0)
                {
                    ready.Add(target);
                }
            }
        }

        return result;
    }

    private static void AddDependency(IDictionary<string, HashSet<string>> adjacency, string source, string target)
    {
        if (adjacency.ContainsKey(source) && adjacency.ContainsKey(target) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            adjacency[source].Add(target);
        }
    }

    private static CompiledEnvironmentBindings FreezeEnvironmentBindings(EnvironmentBindings bindings) =>
        CompiledEnvironmentBindings.Create(
            bindings.Environment,
            bindings.RouteOwners,
            bindings.SpaceBindings,
            bindings.Grants,
            bindings.Capabilities,
            bindings.Destinations,
            bindings.RouteDuplicationGrants);

    private static FlowIssue PrefixGraphIssue(string graphId, FlowIssue issue) =>
        issue with { Path = "/graphs/" + Escape(graphId) + (issue.Path == "/" ? string.Empty : issue.Path) };

    private static IReadOnlyList<FlowIssue> FreezeIssues(IEnumerable<FlowIssue> issues) =>
        issues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ThenBy(issue => issue.Severity)
            .ToImmutableArray();

    private static void WriteGraph(Utf8JsonWriter writer, CompiledFlowGraph graph)
    {
        writer.WriteStartObject();
        writer.WriteString("id", graph.Id);
        writer.WriteNumber("formatVersion", graph.FormatVersion);
        writer.WriteString("role", graph.Role == GraphRole.Ingress ? "ingress" : "character");
        if (graph.CharacterId is not null)
        {
            writer.WriteString("characterId", graph.CharacterId);
        }

        writer.WritePropertyName("entryNodeIds");
        writer.WriteStartArray();
        foreach (var entry in graph.EntryNodeIds)
        {
            writer.WriteStringValue(entry);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("nodes");
        writer.WriteStartArray();
        foreach (var node in graph.OrderedNodes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", node.Id);
            writer.WriteString("kind", node.Kind);
            writer.WriteNumber("blockVersion", node.BlockVersion);
            writer.WritePropertyName("config");
            node.Config.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("dataEdges");
        writer.WriteStartArray();
        foreach (var edge in graph.DataEdges)
        {
            WriteDataEdge(writer, edge);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("controlEdges");
        writer.WriteStartArray();
        foreach (var edge in graph.ControlEdges)
        {
            WriteControlEdge(writer, edge);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDataEdge(Utf8JsonWriter writer, DataEdge edge)
    {
        writer.WriteStartObject();
        WritePort(writer, "source", edge.Source);
        writer.WritePropertyName("fieldPath");
        writer.WriteStartArray();
        foreach (var field in edge.FieldPath)
        {
            writer.WriteStringValue(field);
        }

        writer.WriteEndArray();
        WritePort(writer, "target", edge.Target);
        writer.WriteEndObject();
    }

    private static void WriteControlEdge(Utf8JsonWriter writer, ControlEdge edge)
    {
        writer.WriteStartObject();
        WritePort(writer, "source", edge.Source);
        WritePort(writer, "target", edge.Target);
        writer.WriteEndObject();
    }

    private static void WritePort(Utf8JsonWriter writer, string name, PortRef port)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("nodeId", port.NodeId);
        writer.WriteString("port", port.Port);
        writer.WriteEndObject();
    }

    private static ArtifactReadResult ReadArtifact(string artifact)
    {
        var issues = new List<FlowIssue>();
        byte[] utf8;
        try
        {
            if (artifact.Length > FlowLimits.MaxInputUtf8Bytes ||
                StrictUtf8.GetByteCount(artifact) > FlowLimits.MaxInputUtf8Bytes)
            {
                issues.Add(new FlowIssue("artifact.input_too_large", "/", "Artifact exceeds the maximum UTF-8 size."));
                return new ArtifactReadResult(null, null, issues);
            }

            utf8 = StrictUtf8.GetBytes(artifact);
        }
        catch (EncoderFallbackException)
        {
            issues.Add(new FlowIssue("artifact.encoding_invalid", "/", "Artifact is not valid UTF-8 text."));
            return new ArtifactReadResult(null, null, issues);
        }

        if (utf8.Length > FlowLimits.MaxInputUtf8Bytes)
        {
            issues.Add(new FlowIssue("artifact.input_too_large", "/", "Artifact exceeds the maximum UTF-8 size."));
            return new ArtifactReadResult(null, null, issues);
        }

        JsonDocument document;
        try
        {
            if (HasJsonDepthOverflow(utf8))
            {
                issues.Add(new FlowIssue("artifact.json_depth_exceeded", "/", "Artifact nesting exceeds the supported limit."));
                return new ArtifactReadResult(null, null, issues);
            }

            DetectDuplicateProperties(utf8);
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                MaxDepth = FlowLimits.MaxJsonParseDepth,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (DuplicatePropertyException exception)
        {
            issues.Add(new FlowIssue("artifact.property_duplicate", exception.Path, "Artifact contains a duplicate property."));
            return new ArtifactReadResult(null, null, issues);
        }
        catch (JsonException)
        {
            issues.Add(new FlowIssue("artifact.json_malformed", "/", "Artifact is not valid JSON."));
            return new ArtifactReadResult(null, null, issues);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new FlowIssue("artifact.root_invalid", "/", "Portable artifact root must be an object."));
                return new ArtifactReadResult(null, null, issues);
            }

            EnsureOnly(root, "/", ["artifactFormatVersion", "catalogVersion", "releaseId", "environment", "workspace"], issues);
            var artifactVersion = ReadInt(root, "artifactFormatVersion", "/", issues);
            var catalogVersion = ReadInt(root, "catalogVersion", "/", issues);
            var releaseId = ReadString(root, "releaseId", "/", issues);
            var sourceEnvironment = ReadString(root, "environment", "/", issues);
            if (artifactVersion is not null && artifactVersion != PortableArtifactFormatVersion)
            {
                issues.Add(new FlowIssue("artifact.version_unsupported", "/artifactFormatVersion", "Portable artifact format version is unsupported."));
            }

            if (catalogVersion is not null && catalogVersion != CatalogVersion)
            {
                issues.Add(new FlowIssue("artifact.catalog_unsupported", "/catalogVersion", "Portable artifact catalog version is unsupported."));
            }

            if (sourceEnvironment is not null && !TryParseEnvironment(sourceEnvironment, out _))
            {
                issues.Add(new FlowIssue("artifact.environment_invalid", "/environment", "Portable artifact environment is unsupported."));
            }

            FlowWorkspaceDefinition? workspace;
            if (!root.TryGetProperty("workspace", out var workspaceElement))
            {
                issues.Add(new FlowIssue("artifact.field_required", "/workspace", "Required field is missing."));
                workspace = null;
            }
            else
            {
                workspace = ReadWorkspace(workspaceElement, issues);
            }
            if (artifactVersion is null || catalogVersion is null || releaseId is null || sourceEnvironment is null || workspace is null || issues.Count > 0)
            {
                return new ArtifactReadResult(null, null, issues);
            }

            return new ArtifactReadResult(workspace, releaseId, issues);
        }
    }

    private static FlowWorkspaceDefinition? ReadWorkspace(JsonElement element, ICollection<FlowIssue> issues)
    {
        const string path = "/workspace";
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new FlowIssue("artifact.workspace_invalid", path, "Artifact workspace must be an object."));
            return null;
        }

        EnsureOnly(element, path, ["characters", "graphs", "modelProfiles", "modelProfileReferences"], issues);
        var characters = ReadCharacters(element, issues);
        var graphs = ReadGraphs(element, issues);
        var profiles = ReadProfiles(element, issues);
        var references = ReadReferences(element, issues);
        if (characters is null || graphs is null || profiles is null || references is null)
        {
            return null;
        }

        return new FlowWorkspaceDefinition(characters, graphs, profiles, references);
    }

    private static List<CharacterDefinition>? ReadCharacters(JsonElement workspace, ICollection<FlowIssue> issues)
    {
        if (!ReadArray(workspace, "characters", "/workspace", issues, out var elements))
        {
            return null;
        }

        var result = new List<CharacterDefinition>();
        foreach (var (element, index) in elements.Select((value, index) => (value, index)))
        {
            var path = $"/workspace/characters/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new FlowIssue("artifact.character_invalid", path, "Character must be an object."));
                continue;
            }

            EnsureOnly(element, path, ["id", "displayName", "personality", "graphId"], issues);
            var id = ReadString(element, "id", path, issues);
            var displayName = ReadString(element, "displayName", path, issues);
            var personality = ReadString(element, "personality", path, issues);
            var graphId = ReadString(element, "graphId", path, issues);
            if (id is not null && displayName is not null && personality is not null && graphId is not null)
            {
                result.Add(new CharacterDefinition(id, displayName, personality, graphId));
            }
        }

        return result;
    }

    private static List<GraphDocument>? ReadGraphs(JsonElement workspace, ICollection<FlowIssue> issues)
    {
        if (!ReadArray(workspace, "graphs", "/workspace", issues, out var elements))
        {
            return null;
        }

        var result = new List<GraphDocument>();
        foreach (var (element, index) in elements.Select((value, index) => (value, index)))
        {
            var path = $"/workspace/graphs/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new FlowIssue("artifact.graph_invalid", path, "Graph must be an object."));
                continue;
            }

            var read = FlowDocumentReader.Read(element.GetRawText());
            foreach (var issue in read.Issues)
            {
                issues.Add(issue with { Path = path + (issue.Path == "/" ? string.Empty : issue.Path) });
            }

            if (read.Document is not null)
            {
                result.Add(read.Document);
            }
        }

        return result;
    }

    private static List<ModelProfileDefinition>? ReadProfiles(JsonElement workspace, ICollection<FlowIssue> issues)
    {
        if (!ReadArray(workspace, "modelProfiles", "/workspace", issues, out var elements))
        {
            return null;
        }

        var result = new List<ModelProfileDefinition>();
        foreach (var (element, index) in elements.Select((value, index) => (value, index)))
        {
            var path = $"/workspace/modelProfiles/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new FlowIssue("artifact.model_profile_invalid", path, "Model profile must be an object."));
                continue;
            }

            EnsureOnly(element, path, ["profileId", "credentialReference"], issues);
            var profileId = ReadString(element, "profileId", path, issues);
            var credentialReference = ReadString(element, "credentialReference", path, issues);
            if (profileId is not null && credentialReference is not null)
            {
                result.Add(new ModelProfileDefinition(profileId, credentialReference));
            }
        }

        return result;
    }

    private static Dictionary<string, string>? ReadReferences(JsonElement workspace, ICollection<FlowIssue> issues)
    {
        const string path = "/workspace/modelProfileReferences";
        if (!workspace.TryGetProperty("modelProfileReferences", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new FlowIssue("artifact.field_invalid", path, "Model profile references must be an object."));
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                issues.Add(new FlowIssue("artifact.reference_invalid", path + "/" + Escape(property.Name), "Model profile reference must be a non-empty string."));
                continue;
            }

            result[property.Name] = property.Value.GetString()!;
        }

        return result;
    }

    private static bool ReadArray(JsonElement parent, string name, string path, ICollection<FlowIssue> issues, out JsonElement[] elements)
    {
        elements = [];
        if (!parent.TryGetProperty(name, out var element))
        {
            issues.Add(new FlowIssue("artifact.field_required", path + "/" + name, "Required field is missing."));
            return false;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new FlowIssue("artifact.field_invalid", path + "/" + name, "Field must be an array."));
            return false;
        }

        elements = element.EnumerateArray().ToArray();
        return true;
    }

    private static int? ReadInt(JsonElement parent, string name, string path, ICollection<FlowIssue> issues)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            issues.Add(new FlowIssue("artifact.field_required", Join(path, name), "Required field is missing."));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            issues.Add(new FlowIssue("artifact.field_invalid", Join(path, name), "Field must be a 32-bit integer."));
            return null;
        }

        return value;
    }

    private static string? ReadString(JsonElement parent, string name, string path, ICollection<FlowIssue> issues)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            issues.Add(new FlowIssue("artifact.field_required", Join(path, name), "Required field is missing."));
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            issues.Add(new FlowIssue("artifact.field_invalid", Join(path, name), "Field must be a non-empty string."));
            return null;
        }

        return element.GetString();
    }

    private static void EnsureOnly(JsonElement element, string path, string[] allowed, ICollection<FlowIssue> issues)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
            {
                issues.Add(new FlowIssue("artifact.field_unknown", Join(path, property.Name), "Field is not supported."));
            }
        }
    }

    private static void DetectDuplicateProperties(byte[] utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = int.MaxValue, CommentHandling = JsonCommentHandling.Disallow });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (objects.Count > 0 && !objects.Peek().Add(reader.GetString()!))
                {
                    throw new DuplicatePropertyException("/");
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objects.Pop();
            }
        }
    }

    private static bool HasJsonDepthOverflow(byte[] utf8)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        foreach (var value in utf8)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (value == (byte)'\\')
                {
                    escaped = true;
                }
                else if (value == (byte)'\"')
                {
                    inString = false;
                }

                continue;
            }

            if (value == (byte)'\"')
            {
                inString = true;
            }
            else if (value is (byte)'{' or (byte)'[')
            {
                if (++depth > FlowLimits.MaxJsonParseDepth)
                {
                    return true;
                }
            }
            else if (value is (byte)'}' or (byte)']')
            {
                depth--;
            }
        }

        return false;
    }

    private static string WireEnvironment(FlowEnvironment environment) =>
        environment switch
        {
            FlowEnvironment.Testnet => "testnet",
            FlowEnvironment.Mainnet => "mainnet",
            _ => throw new ArgumentOutOfRangeException(nameof(environment)),
        };

    private static bool TryParseEnvironment(string value, out FlowEnvironment environment)
    {
        if (string.Equals(value, "testnet", StringComparison.OrdinalIgnoreCase))
        {
            environment = FlowEnvironment.Testnet;
            return true;
        }

        if (string.Equals(value, "mainnet", StringComparison.OrdinalIgnoreCase))
        {
            environment = FlowEnvironment.Mainnet;
            return true;
        }

        environment = default;
        return false;
    }

    private static string Join(string path, string segment) =>
        path == "/" ? "/" + Escape(segment) : path + "/" + Escape(segment);

    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed record ArtifactReadResult(FlowWorkspaceDefinition? Workspace, string? ReleaseId, IReadOnlyList<FlowIssue> Issues);

    private sealed class DuplicatePropertyException(string path) : Exception
    {
        public string Path { get; } = path;
    }
}

public sealed class CompiledFlowRelease
{
    private CompiledFlowRelease(
        FlowEnvironment environment,
        string releaseId,
        IEnumerable<CompiledCharacterDefinition> characters,
        IEnumerable<CompiledFlowGraph> graphs,
        IEnumerable<CompiledModelProfileDefinition> modelProfiles,
        IReadOnlyDictionary<string, string> characterModelProfileReferences,
        CompiledEnvironmentBindings environmentBindings)
    {
        Environment = environment;
        ReleaseId = releaseId;
        Characters = characters.OrderBy(value => value.Id, StringComparer.Ordinal).ToImmutableArray();
        Graphs = graphs.OrderBy(value => value.Id, StringComparer.Ordinal).ToImmutableArray();
        ModelProfiles = modelProfiles.OrderBy(value => value.ProfileId, StringComparer.Ordinal).ToImmutableArray();
        CharacterModelProfileReferences = characterModelProfileReferences.ToFrozenDictionary(StringComparer.Ordinal);
        EnvironmentBindings = environmentBindings;
    }

    public FlowEnvironment Environment { get; }
    public string ReleaseId { get; }
    public IReadOnlyList<CompiledCharacterDefinition> Characters { get; }
    public IReadOnlyList<CompiledFlowGraph> Graphs { get; }
    public IReadOnlyList<CompiledModelProfileDefinition> ModelProfiles { get; }
    public IReadOnlyDictionary<string, string> CharacterModelProfileReferences { get; }
    public IReadOnlyDictionary<string, string> ModelProfileReferences => CharacterModelProfileReferences;
    public CompiledEnvironmentBindings EnvironmentBindings { get; }
    public CompiledEnvironmentBindings Bindings => EnvironmentBindings;

    internal static CompiledFlowRelease Create(
        FlowEnvironment environment,
        string releaseId,
        IEnumerable<CompiledCharacterDefinition> characters,
        IEnumerable<CompiledFlowGraph> graphs,
        IEnumerable<CompiledModelProfileDefinition> modelProfiles,
        IReadOnlyDictionary<string, string> characterModelProfileReferences,
        CompiledEnvironmentBindings environmentBindings) =>
        new(environment, releaseId, characters, graphs, modelProfiles, characterModelProfileReferences, environmentBindings);
}

public sealed record CompiledCharacterDefinition(string Id, string DisplayName, string Personality, string GraphId)
{
    internal static CompiledCharacterDefinition Create(CharacterDefinition character) =>
        new(character.Id, character.DisplayName, character.Personality, character.GraphId);
}

public sealed record CompiledModelProfileDefinition(string ProfileId, string CredentialReference)
{
    internal static CompiledModelProfileDefinition Create(ModelProfileDefinition profile) =>
        new(profile.ProfileId, profile.CredentialReference);
}

public sealed class CompiledFlowGraph
{
    private CompiledFlowGraph(
        string id,
        int formatVersion,
        GraphRole role,
        string? characterId,
        IEnumerable<CompiledNodePlan> nodes,
        IEnumerable<DataEdge> dataEdges,
        IEnumerable<ControlEdge> controlEdges,
        IEnumerable<string> entryNodeIds)
    {
        Id = id;
        FormatVersion = formatVersion;
        Role = role;
        CharacterId = characterId;
        OrderedNodes = nodes.ToImmutableArray();
        DataEdges = dataEdges.ToImmutableArray();
        ControlEdges = controlEdges.ToImmutableArray();
        EntryNodeIds = entryNodeIds.ToImmutableArray();
    }

    public string Id { get; }
    public int FormatVersion { get; }
    public GraphRole Role { get; }
    public string? CharacterId { get; }
    public IReadOnlyList<CompiledNodePlan> Nodes => OrderedNodes;
    public IReadOnlyList<CompiledNodePlan> OrderedNodes { get; }
    public IReadOnlyList<DataEdge> DataEdges { get; }
    public IReadOnlyList<ControlEdge> ControlEdges { get; }
    public IReadOnlyList<string> EntryNodeIds { get; }

    internal static CompiledFlowGraph Create(
        string id,
        int formatVersion,
        GraphRole role,
        string? characterId,
        IEnumerable<CompiledNodePlan> nodes,
        IEnumerable<DataEdge> dataEdges,
        IEnumerable<ControlEdge> controlEdges,
        IEnumerable<string> entryNodeIds) =>
        new(id, formatVersion, role, characterId, nodes, dataEdges, controlEdges, entryNodeIds);
}

public sealed class CompiledNodePlan
{
    private CompiledNodePlan(
        string id,
        string kind,
        int blockVersion,
        FlowEffect effect,
        JsonElement config,
        IEnumerable<CompiledDataPort> dataInputs,
        IEnumerable<CompiledDataPort> dataOutputs,
        IEnumerable<CompiledControlPort> controlInputs,
        IEnumerable<CompiledControlPort> controlOutputs)
    {
        Id = id;
        Kind = kind;
        BlockVersion = blockVersion;
        Effect = effect;
        Config = config.Clone();
        DataInputs = dataInputs.ToImmutableArray();
        DataOutputs = dataOutputs.ToImmutableArray();
        ControlInputs = controlInputs.ToImmutableArray();
        ControlOutputs = controlOutputs.ToImmutableArray();
    }

    public string Id { get; }
    public string Kind { get; }
    public int BlockVersion { get; }
    public FlowEffect Effect { get; }
    public JsonElement Config { get; }
    public IReadOnlyList<CompiledDataPort> DataInputs { get; }
    public IReadOnlyList<CompiledDataPort> DataOutputs { get; }
    public IReadOnlyList<CompiledControlPort> ControlInputs { get; }
    public IReadOnlyList<CompiledControlPort> ControlOutputs { get; }

    internal static CompiledNodePlan Create(FlowNode node, NodeDefinition definition)
    {
        var resolution = definition.Resolve(node.Config);
        return new(
            node.Id,
            node.Kind,
            node.BlockVersion,
            definition.Effect,
            node.Config,
            resolution.DataInputs.Select(CompiledDataPort.Create),
            resolution.DataOutputs.Select(CompiledDataPort.Create),
            resolution.ControlInputs.Select(CompiledControlPort.Create),
            resolution.ControlOutputs.Select(CompiledControlPort.Create));
    }
}

public sealed record CompiledDataPort(
    string Name,
    CompiledFlowType Type,
    bool Required,
    bool AcceptsAny)
{
    internal static CompiledDataPort Create(FlowDataPortDefinition port) =>
        new(port.Name, CompiledFlowType.Create(port.Type), port.Required, port.AcceptsAny);
}

public sealed record CompiledControlPort(string Name, bool Required)
{
    internal static CompiledControlPort Create(FlowControlPortDefinition port) => new(port.Name, port.Required);
}

public sealed class CompiledFlowType
{
    private CompiledFlowType(
        string kind,
        IEnumerable<string> values,
        IEnumerable<KeyValuePair<string, CompiledFlowField>> fields,
        CompiledFlowType? elementType,
        CompiledFlowType? innerType)
    {
        Kind = kind;
        Values = values.ToImmutableArray();
        Fields = fields.ToFrozenDictionary(StringComparer.Ordinal);
        ElementType = elementType;
        InnerType = innerType;
    }

    public string Kind { get; }
    public IReadOnlyList<string> Values { get; }
    public IReadOnlyDictionary<string, CompiledFlowField> Fields { get; }
    public CompiledFlowType? ElementType { get; }
    public CompiledFlowType? InnerType { get; }

    internal static CompiledFlowType Create(FlowType type) =>
        type switch
        {
            EnumType enumType => new(type.Kind, enumType.Values, [], null, null),
            ObjectType objectType => new(type.Kind, [], objectType.Fields.Select(value => new KeyValuePair<string, CompiledFlowField>(value.Key, CompiledFlowField.Create(value.Value))), null, null),
            ArrayType arrayType => new(type.Kind, [], [], Create(arrayType.ElementType), null),
            NullableType nullableType => new(type.Kind, [], [], null, Create(nullableType.InnerType)),
            _ => new(type.Kind, [], [], null, null),
        };
}

public sealed record CompiledFlowField(CompiledFlowType Type, bool Required)
{
    internal static CompiledFlowField Create(FlowField field) => new(CompiledFlowType.Create(field.Type), field.Required);
}

public sealed class CompiledEnvironmentBindings
{
    private CompiledEnvironmentBindings(
        FlowEnvironment environment,
        IEnumerable<RouteOwnerBinding> routeOwners,
        IEnumerable<SpaceBinding> spaces,
        IEnumerable<FlowGrant> grants,
        IEnumerable<string> capabilities,
        IEnumerable<DestinationBinding> destinations,
        IEnumerable<RouteDuplicationGrant> routeDuplicationGrants)
    {
        Environment = environment;
        RouteOwners = routeOwners.Select(value => new RouteOwnerBinding(value.RouteId, value.AccountId, value.ConversationId, value.OwnerCharacterId, value.ExplicitDuplicate, value.DuplicationGrantId, value.IngressGraphId)).ToImmutableArray();
        Spaces = spaces.Select(value => CompiledSpaceBinding.Create(value)).ToImmutableArray();
        Grants = grants.Select(value => new FlowGrant(value.CharacterId, value.LogicalSpaceId, value.Operation, value.Mode, value.ProtectorId)).ToImmutableArray();
        Capabilities = capabilities.ToFrozenSet(StringComparer.Ordinal);
        Destinations = destinations.Select(value => new DestinationBinding(value.DestinationId, value.Environment, value.AccountId, value.ConversationId, value.DisplayName)).ToImmutableArray();
        RouteDuplicationGrants = routeDuplicationGrants.Select(value => new RouteDuplicationGrant(value.GrantId, value.RouteId, value.CharacterId)).ToImmutableArray();
    }

    public FlowEnvironment Environment { get; }
    public IReadOnlyList<RouteOwnerBinding> RouteOwners { get; }
    public IReadOnlyList<CompiledSpaceBinding> Spaces { get; }
    public IReadOnlyList<CompiledSpaceBinding> SpaceBindings => Spaces;
    public IReadOnlyList<FlowGrant> Grants { get; }
    public IReadOnlySet<string> Capabilities { get; }
    public IReadOnlyList<DestinationBinding> Destinations { get; }
    public IReadOnlyList<RouteDuplicationGrant> RouteDuplicationGrants { get; }

    internal static CompiledEnvironmentBindings Create(
        FlowEnvironment environment,
        IEnumerable<RouteOwnerBinding> routeOwners,
        IEnumerable<SpaceBinding> spaces,
        IEnumerable<FlowGrant> grants,
        IEnumerable<string> capabilities,
        IEnumerable<DestinationBinding> destinations,
        IEnumerable<RouteDuplicationGrant> routeDuplicationGrants) =>
        new(environment, routeOwners, spaces, grants, capabilities, destinations, routeDuplicationGrants);
}

public sealed class CompiledSpaceBinding
{
    private CompiledSpaceBinding(
        string logicalSpaceId,
        FlowEnvironment environment,
        FlowSpaceScope scope,
        string? ownerCharacterId,
        IEnumerable<string> explicitConversationIds,
        string physicalStoreKey)
    {
        LogicalSpaceId = logicalSpaceId;
        Environment = environment;
        Scope = scope;
        OwnerCharacterId = ownerCharacterId;
        ExplicitConversationIds = explicitConversationIds.ToImmutableArray();
        PhysicalStoreKey = physicalStoreKey;
    }

    public string LogicalSpaceId { get; }
    public FlowEnvironment Environment { get; }
    public FlowSpaceScope Scope { get; }
    public string? OwnerCharacterId { get; }
    public IReadOnlyList<string> ExplicitConversationIds { get; }
    public string PhysicalStoreKey { get; }

    internal static CompiledSpaceBinding Create(SpaceBinding space) =>
        new(space.LogicalSpaceId, space.Environment, space.Scope, space.OwnerCharacterId, space.ExplicitConversationIds, space.PhysicalStoreKey);
}
