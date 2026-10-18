using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Boundaries;
using Cortex.Core.Outbound;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Execution;

internal static class FlowExecutionKernel
{
    internal static FlowExecutionState Start(TypedFlow flow, JsonElement message, ExecutionStart identity,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>>? runtimeOutputs)
    {
        DurableIdentity.Validate(identity.Scope);
        var snapshot = new ExecutionSnapshotV1
        {
            Scope = identity.Scope,
            BatchId = identity.BatchId,
            ProjectRef = identity.ProjectRef,
            BatchRef = identity.BatchRef,
            StateId = identity.StateId,
            StateVersion = identity.StateVersion,
            EntryKind = identity.EntryKind,
            Cursor = flow.EntryId,
            Status = ExecutionStatus.Ready
        };
        if (flow.Nodes[flow.EntryId].Source.Kind is "dm" or "group")
            snapshot.Values[new(flow.EntryId, "message")] = new(FlowDomain.Message, message.Clone());
        return new(snapshot, runtimeOutputs);
    }
    internal static StepEffect Effect(TypedNode node) => node.HostEffect switch
    {
        FlowEffect.Model => StepEffect.Inference,
        FlowEffect.WriteState => StepEffect.Artifact,
        _ => node.Source.Kind switch
        {
            "prompt" => StepEffect.Inference,
            "memory" => StepEffect.Memory,
            "interpretBoundaries" => StepEffect.Boundary,
            "send" => StepEffect.Send,
            "humanFeedback" => StepEffect.Feedback,
            _ => StepEffect.Pure
        }
    };
    internal static async Task<KernelStepResult> AdvanceAsync(TypedFlow flow, FlowExecutionState state,
        FlowExecutionServices services, CancellationToken token, bool oneNode = false)
    {
        if (state.Snapshot.Status != ExecutionStatus.Ready) throw new InvalidOperationException("Execution is not ready to advance.");
        using var diagnosticContext = new FlowDiagnosticContext(state.Snapshot.InferenceAttempts, services.Diagnostics);
        var traces = new List<TypedNodeExecutionTrace>();
        var result = await ExecuteAdvanceAsync(flow, state, services with { Observer = trace => { traces.Add(trace); services.Observer?.Invoke(trace); } }, token, oneNode);
        result = result with { InferenceAttempts = state.Snapshot.InferenceAttempts.ToArray() };
        var next = state.Snapshot.Cursor;
        return new(result, state.Snapshot.Status, next, next is null ? StepEffect.Pure : Effect(flow.Nodes[next]), traces);
    }
    private static async Task<TypedRunResult> ExecuteAdvanceAsync(TypedFlow flow, FlowExecutionState state,
        FlowExecutionServices services, CancellationToken token, bool oneNode)
    {
        var snapshot = state.Snapshot;
        var steps = snapshot.Steps;
        var published = snapshot.Outputs;
        var values = snapshot.Values;
        foreach (var node in flow.Nodes.Values.Where(node => node.Source.Kind is "property" or "count" || node.HostExecutionMode == FlowNodeExecutionMode.Pure))
            foreach (var port in node.Outputs)
                values.Remove(new PortRef(node.Source.Id, port.Id));
        var references = snapshot.References;
        var sendAttempted = snapshot.SendAttempted;
        ConfirmedSend? confirmed = snapshot.ConfirmedSend;
        string current = snapshot.Cursor ?? throw new InvalidOperationException("Execution has no active cursor.");
        var completion = new DiagnosticFlowCompletion(services.Completion);
        var runtimeOutputs = state.RuntimeOutputs;
        var timeProvider = services.Clock;
        var observer = services.Observer;
        var memoryQuery = services.MemoryQuery;
        var instructionContext = services.InstructionContext;
        var instructionOutput = services.InstructionOutput;
        var nodeStarting = services.NodeStarting;
        var boundaryRegistry = services.BoundaryRegistry;
        var outbound = services.Outbound;
        var initialEffect = Effect(flow.Nodes[current]);
        var visited = 0;
        Dictionary<string, Dictionary<string, TypedValue>> activationPureOutputs = new(StringComparer.Ordinal);
        var failureNodeId = current;
        string? failurePort = null;
        var failureOrigin = FlowFailureOrigin.Runtime;
        TypedRunResult Complete(bool success, string? target)
        {
            snapshot.Status = success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
            snapshot.Cursor = null; snapshot.TargetStateId = target;
            return new(success, steps, published, target, confirmed);
        }
        TypedRunResult Yield(string next)
        {
            snapshot.Cursor = next;
            return new(true, steps, published, null, confirmed);
        }
        DateTimeOffset nodeStartedAt = default;
        long? nodeStartedTimestamp = null;
        JsonElement SerializeInput(JsonElement value, FlowType type)
        {
            if (type is NullableType nullable)
                return value.ValueKind == JsonValueKind.Null ? value.Clone() : SerializeInput(value, nullable.InnerType);
            if (type is ArrayType array)
                return JsonSerializer.SerializeToElement(value.EnumerateArray().Select(item => SerializeInput(item, array.ElementType)).ToArray());
            if (type is DictionaryType dictionary)
            {
                var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var item in value.EnumerateObject())
                    entries.Add(item.Name, SerializeInput(item.Value, dictionary.ValueType));
                return JsonSerializer.SerializeToElement(entries);
            }
            if (type == FlowDomain.Boundary)
            {
                if (boundaryRegistry is null) throw new InvalidDataException("Boundary exige contexto de execução confiável.");
                return boundaryRegistry.ForModel(JsonSerializer.SerializeToElement(new[] { value }), timeProvider.GetUtcNow(), references)[0].Clone();
            }
            return FlowDomain.ForModel(value, type, references);
        }
        async Task<Dictionary<string, TypedValue>> ExecuteHostAsync(TypedNode node, FlowNodeHandler handler,
            Dictionary<string, TypedValue> inputs)
        {
            failureOrigin = FlowFailureOrigin.HostNode;
            var raw = await handler.ExecuteAsync(FlowNodeHandler.Config(node.Source), inputs.AsReadOnly(), token);
            failureOrigin = FlowFailureOrigin.Runtime;
            token.ThrowIfCancellationRequested();
            var declared = node.Outputs.ToDictionary(port => port.Id, StringComparer.Ordinal);
            var undeclared = raw.Keys.FirstOrDefault(key => !declared.ContainsKey(key));
            if (undeclared is not null)
                throw new InvalidDataException("Host output port is not declared: " + undeclared);
            foreach (var port in node.Outputs)
                if (port.Required && !raw.ContainsKey(port.Id))
                {
                    failurePort = port.Id;
                    throw new InvalidDataException("Required host output is missing: " + port.Name);
                }
            var outputs = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
            foreach (var (portId, value) in raw)
            {
                var port = declared[portId];
                failurePort = port.Id;
                FlowDomain.Check(value, port.Type);
                outputs.Add(port.Id, new(port.Type, value.Clone()));
            }
            return outputs;
        }
        void StoreHostOutputs(TypedNode node, Dictionary<string, TypedValue> outputs)
        {
            foreach (var port in node.Outputs) values.Remove(new PortRef(node.Source.Id, port.Id));
            published[node.Source.Id] = outputs;
            foreach (var (port, value) in outputs) values[new(node.Source.Id, port)] = value;
        }
        async Task<TypedValue?> Input(TypedNode node, TypedPort port)
        {
            var edge = flow.Graph.DataEdges.SingleOrDefault(e => e.Target == new PortRef(node.Source.Id, port.Id));
            if (edge is not null)
            {
                var value = await Resolve(edge.Source);
                if (value is null)
                {
                    if (port.Required)
                        throw new InvalidDataException("Required input '" + port.Name + "' received missing output '" + edge.Source.Port + "' from node '" + edge.Source.NodeId + "'.");
                    return null;
                }
                FlowDomain.Check(value.Value, port.Type);
                return value;
            }
            if (port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out var local))
            {
                FlowDomain.Check(local, port.Type);
                return new(port.Type, local.Clone());
            }
            if (!port.Required) return null;
            throw new InvalidDataException("Input not available: " + port.Name);
        }
        async Task<TypedValue?> Resolve(PortRef key)
        {
            token.ThrowIfCancellationRequested();
            var node = flow.Nodes[key.NodeId];
            var pure = node.Source.Kind is "property" or "count" || node.HostExecutionMode == FlowNodeExecutionMode.Pure;
            if (!pure && values.TryGetValue(key, out var existing)) return existing;
            if (!pure)
            {
                if (published.TryGetValue(key.NodeId, out var completedOutputs))
                {
                    if (completedOutputs.ContainsKey(key.Port)) throw new InvalidDataException("Output value is unavailable after its node completed: " + key.Port);
                    var completedPort = node.Outputs.Single(port => port.Id == key.Port);
                    if (!completedPort.Required) return null;
                    throw new InvalidDataException("Required output is missing after its node completed: " + completedPort.Name);
                }
                throw new InvalidDataException("Operational output is not available yet.");
            }
            if (activationPureOutputs.TryGetValue(key.NodeId, out var cached))
            {
                if (cached.TryGetValue(key.Port, out var cachedValue)) return cachedValue;
                var cachedPort = node.Outputs.Single(port => port.Id == key.Port);
                if (!cachedPort.Required) return null;
                throw new InvalidDataException("Required pure output is missing: " + cachedPort.Name);
            }

            var previousFailureNode = failureNodeId;
            var previousFailurePort = failurePort;
            failureNodeId = node.Source.Id;
            failurePort = key.Port;
            try
            {
                Dictionary<string, TypedValue> outputs;
                if (node.Source.Kind is "property" or "count")
                {
                    var inputPort = node.Inputs.Single();
                    failurePort = inputPort.Id;
                    var input = await Input(node, inputPort) ?? throw new InvalidDataException("Required input is unavailable: " + inputPort.Name);
                    FlowDomain.Check(input.Value, inputPort.Type);
                    var projected = node.Source.Kind == "count"
                        ? FlowDomain.CountItems(input.Value, input.Type)
                        : FlowDomain.Project(input.Value, input.Type, TypedFlowCompiler.PropertyPath(node.Source.Config));
                    var outputPort = node.Outputs.Single();
                    failurePort = outputPort.Id;
                    FlowDomain.Check(projected, outputPort.Type);
                    outputs = new(StringComparer.Ordinal) { [outputPort.Id] = new(outputPort.Type, projected.Clone()) };
                }
                else
                {
                    if (!flow.HostNodes.TryGetValue(node.Source.Kind, out var handler) || !handler.IsPure)
                        throw new InvalidDataException("Pure host handler is unavailable: " + node.Source.Kind);
                    var inputs = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
                    foreach (var port in node.Inputs)
                    {
                        var connected = flow.Graph.DataEdges.Any(edge => edge.Target == new PortRef(node.Source.Id, port.Id));
                        var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                        if (!connected && !configured)
                        {
                            if (port.Required) throw new InvalidDataException("Required input is unavailable: " + port.Name);
                            continue;
                        }
                        failurePort = port.Id;
                        var input = await Input(node, port);
                        if (input is null) continue;
                        FlowDomain.Check(input.Value, port.Type);
                        inputs.Add(port.Id, input);
                    }
                    failurePort = key.Port;
                    token.ThrowIfCancellationRequested();
                    outputs = await ExecuteHostAsync(node, handler, inputs);
                }

                activationPureOutputs.Add(node.Source.Id, outputs);
                failureNodeId = previousFailureNode;
                failurePort = previousFailurePort;
                failureNodeId = node.Source.Id;
                failurePort = key.Port;
                if (!outputs.TryGetValue(key.Port, out var value))
                {
                    var outputPort = node.Outputs.Single(port => port.Id == key.Port);
                    if (!outputPort.Required)
                    {
                        failureNodeId = previousFailureNode;
                        failurePort = previousFailurePort;
                        return null;
                    }
                    throw new InvalidDataException("Required pure output is missing: " + outputPort.Name);
                }
                failureNodeId = previousFailureNode;
                failurePort = previousFailurePort;
                return value;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                if (failureNodeId == node.Source.Id)
                {
                    failureNodeId = node.Source.Id;
                    failurePort ??= key.Port;
                }
                throw new InvalidDataException("Pure node '" + node.Source.Id + "' at port '" + failurePort + "': " + exception.Message, exception);
            }
        }
        try
        {
            if (flow.Nodes[flow.EntryId].Source.Kind is "dm" or "group")
                FlowDomain.Check(values[new(flow.EntryId, "message")].Value, FlowDomain.Message);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var node = flow.Nodes[current];
                if (visited > 0 && (oneNode || initialEffect != StepEffect.Pure || Effect(node) != StepEffect.Pure || visited >= 64)) return Yield(current);
                activationPureOutputs = new(StringComparer.Ordinal);
                failureNodeId = current;
                failurePort = null;
                failureOrigin = FlowFailureOrigin.Runtime;
                visited++; snapshot.Ordinal++;
                var exit = "then";
                nodeStartedAt = timeProvider.GetUtcNow();
                nodeStartedTimestamp = timeProvider.GetTimestamp();
                observer?.Invoke(new(current, "started", null, null, nodeStartedAt, TimeSpan.Zero));
                if (nodeStarting is not null)
                    await nodeStarting(current, published.ToDictionary(pair => pair.Key,
                        pair => (IReadOnlyDictionary<string, TypedValue>)pair.Value.AsReadOnly()).AsReadOnly(), token);
                if (flow.HostNodes.TryGetValue(node.Source.Kind, out var handler))
                {
                    var inputs = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
                    foreach (var port in node.Inputs)
                    {
                        var connected = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id));
                        var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                        if (!port.Required && !connected && !configured) continue;
                        var input = await Input(node, port);
                        if (input is null) continue;
                        FlowDomain.Check(input.Value, port.Type);
                        inputs.Add(port.Id, input);
                    }
                    var output = await ExecuteHostAsync(node, handler, inputs);
                    StoreHostOutputs(node, output);
                }
                else if (node.Source.Kind == "humanFeedback")
                {
                    snapshot.Ordinal--; // This node remains pending until a durable resolution completes it.
                    var port = node.Inputs.Single();
                    var context = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id))
                        ? (await Input(node, port))?.Value.GetString() : null;
                    snapshot.PendingFeedback = HumanFeedbackNode.Request(new(snapshot.Scope, current, snapshot.Ordinal), HumanFeedbackNode.Read(node.Source.Config), context);
                    snapshot.Status = ExecutionStatus.WaitingForHuman;
                    snapshot.Cursor = current;
                    observer?.Invoke(new(current, "waitingForHuman", null, null, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value)));
                    nodeStartedTimestamp = null;
                    return new(true, steps, published, null, confirmed);
                }
                else if (node.Source.Kind == "prompt" && BoundaryEnforcementProfile.Enabled(node.Source.Config))
                {
                    if (boundaryRegistry is null) throw new InvalidDataException("Boundary enforcement requires trusted execution context.");
                    async Task<TypedValue> ProfileInput(string name)
                    {
                        var port = node.Inputs.Single(p => p.Name == name);
                        var value = await Input(node, port) ?? throw new InvalidDataException("Required input is unavailable: " + port.Name);
                        FlowDomain.Check(value.Value, port.Type);
                        return value;
                    }
                    var limits = await ProfileInput("limites");
                    var response = await ProfileInput("resposta");
                    var messages = await ProfileInput("mensagens");
                    var output = await BoundaryEnforcementProfile.RunAsync(node, limits.Value,
                        response.Value.GetString()!, messages.Value,
                        boundaryRegistry, completion, timeProvider,
                        metadata => observer?.Invoke(new(current, "boundary-policy", null, null, timeProvider.GetUtcNow(), TimeSpan.Zero, metadata)), token);
                    outbound?.Enforced(current, response, limits,
                        output["resultado"].Value.GetString() == "permitir", ShadowOutboundHost.Fingerprint(node.Source.Config));
                    if (instructionOutput is not null) await instructionOutput(current, output.AsReadOnly(), token);
                    token.ThrowIfCancellationRequested();
                    published[current] = output;
                    foreach (var (port, value) in output) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "prompt")
                {
                    var inputs = new Dictionary<string, JsonElement>();
                    foreach (var port in node.Inputs)
                    {
                        var connected = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id));
                        var configured = port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _);
                        if (!port.Required && !connected && !configured) continue;
                        var value = await Input(node, port);
                        if (value is null) continue;
                        FlowDomain.Check(value.Value, port.Type);
                        inputs[port.Name] = SerializeInput(value.Value, port.Type);
                    }
                    var config = node.Source.Config;
                    var schema = TypedFlowRunner.ResponseSchema(node);
                    var system = InstructionContext.Compose(config, instructionContext);
                    string? correction = null; Dictionary<string, TypedValue>? output = null;
                    var retries = config.TryGetProperty("correctionRetry", out var retry) && retry.ValueKind == JsonValueKind.False ? 0 : 1;
                    for (var attempt = 0; attempt <= retries; attempt++)
                    {
                        var request = new FlowCompletionRequest(current, system, JsonSerializer.SerializeToElement(inputs), schema,
                            TypedFlowRunner.OptionalText(config, "model"), TypedFlowRunner.OptionalText(config, "provider"), correction,
                            TypedFlowRunner.OptionalText(config, "reasoningEffort"), config.TryGetProperty("maxOutputTokens", out var budget) ? budget.GetInt32() : null)
                        {
                            HostExecutionId = snapshot.Scope.ExecutionId,
                            HostReferences = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                                references.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                        };
                        var (text, diagnostics) = await FlowDiagnosticContext.CompleteAsync(completion, request, token);
                        try { output = TypedFlowRunner.ValidateOutput(node, text, schema is not null); diagnostics.RecordValidation(null); break; }
                        catch (Exception e) when (e is JsonException or InvalidDataException)
                        {
                            diagnostics.RecordValidation(e);
                            if (attempt == retries)
                            {
                                var failure = new InvalidDataException("Resposta inválida após validação: " + (e is JsonException ? "JSON inválido." : e.Message), e);
                                FlowDiagnostics.Attach(failure, FlowDiagnostics.Create(e, current, null, FlowFailureOrigin.Runtime,
                                    e is JsonException ? FlowFailureCategory.Parsing : FlowFailureCategory.Validation));
                                throw failure;
                            }
                            correction = e is JsonException ? "Retorne um objeto JSON válido conforme o schema." : e.Message;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (instructionOutput is not null)
                    {
                        var instructionSnapshot = new System.Collections.ObjectModel.ReadOnlyDictionary<string, TypedValue>(
                            output!.ToDictionary(p => p.Key, p => p.Value with { Value = p.Value.Value.Clone() }));
                        await instructionOutput(current, instructionSnapshot, token);
                        token.ThrowIfCancellationRequested();
                    }
                    published[current] = output!;
                    outbound?.Generated(current, output!, references);
                    foreach (var (port, value) in output!) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "interpretBoundaries")
                {
                    if (boundaryRegistry is null) throw new InvalidDataException("Interpretar Limites exige contexto de execução confiável.");
                    var boundaryInput = await Input(node, node.Inputs.Single()) ?? throw new InvalidDataException("Required boundary input is unavailable.");
                    var output = await boundaryRegistry.CompileAsync(boundaryInput.Value,
                        BoundaryCompilerSettings.Read(node.Source.Config), timeProvider.GetUtcNow(), token);
                    var value = new TypedValue(new ArrayType(FlowDomain.Boundary), output);
                    outbound?.Compiled(current, boundaryInput, value);
                    published[current] = new() { ["boundaries"] = value };
                    values[new(current, "boundaries")] = value;
                }
                else if (node.Source.Kind == "memory")
                {
                    if (memoryQuery is null) throw new InvalidDataException("Memória exige contexto de sessão.");
                    async Task<TypedValue?> OptionalInput(string id)
                    {
                        var port = node.Inputs.Single(p => p.Id == id);
                        return flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, id)) ||
                            port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _)
                            ? await Input(node, port) : null;
                    }
                    var persons = await OptionalInput("persons");
                    var query = await OptionalInput("query");
                    var response = await memoryQuery(node, persons?.Value, query?.Value.GetString(), token);
                    FlowDomain.Check(response, new ArrayType(FlowDomain.Memory));
                    var value = new TypedValue(new ArrayType(FlowDomain.Memory), response.Clone());
                    outbound?.Recall(current, value);
                    published[current] = new() { ["memories"] = value };
                    values[new(current, "memories")] = value;
                }
                else if (node.Source.Kind == "send")
                {
                    var candidate = await Input(node, node.Inputs.Single()) ?? throw new InvalidDataException("Required send input is unavailable.");
                    FlowDomain.Check(candidate.Value, new StringType());
                    var result = outbound is not null ? await outbound.SendAsync(candidate, token)
                        : sendAttempted ? new ShadowSendResult("blocked", "duplicate_attempt") : new ShadowSendResult("disabled", "outbound_host_unavailable");
                    sendAttempted = true;
                    // Only a provider-accepted receipt with a host-owned speaker is confirmed speech. WOULD_SEND never is.
                    if (result.Status == "sent" && result.ProviderMessageId is not null && result.Speaker is not null && confirmed is null)
                        confirmed = new(current, result.ProviderMessageId, candidate.Value.GetString()!, result.Speaker)
                        { ReplyToMessageId = result.ReplyToMessageId };
                    exit = result.Status;
                    published[current] = new()
                    {
                        ["status"] = new(node.Outputs.Single(p => p.Id == "status").Type, JsonSerializer.SerializeToElement(result.Status)),
                        ["diagnostico"] = new(new StringType(), JsonSerializer.SerializeToElement(result.DiagnosticCode))
                    };
                    foreach (var (port, value) in published[current]) values[new(current, port)] = value;
                    observer?.Invoke(new(current, "outbound", null, null, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                        outbound?.Trace(result) ?? new Dictionary<string, string> { ["result"] = result.Status, ["diagnosticCode"] = result.DiagnosticCode }));
                }
                else if (node.Source.Kind is "accumulate" or "history")
                {
                    if (runtimeOutputs is null || !runtimeOutputs.TryGetValue(current, out var supplied))
                        throw new InvalidDataException("Session runtime output is unavailable for " + node.Source.Kind + ".");
                    var output = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
                    foreach (var port in node.Outputs)
                    {
                        if (!supplied.TryGetValue(port.Id, out var value)) throw new InvalidDataException("Session runtime output port is unavailable: " + port.Id);
                        FlowDomain.Check(value.Value, port.Type);
                        if (!value.Type.IsCompatibleWith(port.Type)) throw new InvalidDataException("Session runtime output type is incompatible: " + port.Id);
                        output[port.Id] = new(port.Type, value.Value.Clone());
                        values[new(current, port.Id)] = output[port.Id];
                    }
                    published[current] = output;
                }
                else if (node.Source.Kind == "exit")
                {
                    if (runtimeOutputs is null) throw new InvalidDataException("Exit requires session runtime context.");
                    var settings = node.Source.Config.GetProperty("settings");
                    string target;
                    if (settings.TryGetProperty("targetStateId", out var configured) && configured.ValueKind == JsonValueKind.String)
                    {
                        target = configured.GetString()!;
                    }
                    else
                    {
                        var selected = (await Input(node, node.Inputs.Single()))?.Value.GetString() ?? throw new InvalidDataException("Required exit input is unavailable.");
                        target = settings.GetProperty("stateMapping").GetProperty(selected).GetString()!;
                    }
                    var duration = timeProvider.GetElapsedTime(nodeStartedTimestamp.Value);
                    steps.Add(new(current, "completed", null, null));
                    observer?.Invoke(new(current, "completed", null, null, timeProvider.GetUtcNow(), duration));
                    nodeStartedTimestamp = null;
                    // After a send attempt, only confirmed speech may reach a transition; every other outcome stays hypothetical.
                    var hypothetical = sendAttempted && confirmed is null;
                    if (hypothetical)
                        observer?.Invoke(new(current, "hypothetical-transition", null, null, timeProvider.GetUtcNow(), TimeSpan.Zero,
                            new Dictionary<string, string> { ["wouldTransitionTo"] = target, ["applied"] = "false" }));
                    return Complete(true, hypothetical ? null : target);
                }
                else if (node.Source.Kind == "branch")
                {
                    var left = await Input(node, node.Inputs.Single(p => p.Id == "a")) ?? throw new InvalidDataException("Required branch input is unavailable.");
                    var right = await Input(node, node.Inputs.Single(p => p.Id == "b")) ?? throw new InvalidDataException("Required branch input is unavailable.");
                    exit = TypedBranch.Compare(left, right, node.Source.Config.GetProperty("settings").GetProperty("operator").GetString()!) ? "true" : "false";
                }
                else if (node.Source.Kind == "switch")
                {
                    var value = (await Input(node, node.Inputs.Single()))?.Value.GetString() ?? throw new InvalidDataException("Required switch input is unavailable.");
                    var cases = node.Source.Config.GetProperty("settings").GetProperty("cases");
                    var match = cases.EnumerateArray().Where(c => c.GetProperty("value").GetString() == value).ToArray();
                    if (match.Length == 1) exit = match[0].GetProperty("id").GetString()!;
                    else if (TypedFlowCompiler.HasOther(node.Source.Config)) exit = "other";
                    else throw new InvalidDataException("Switch sem caso correspondente e sem Outro.");
                }
                var completedDuration = timeProvider.GetElapsedTime(nodeStartedTimestamp.Value);
                steps.Add(new(current, "completed", exit, null));
                observer?.Invoke(new(current, "completed", exit, null, timeProvider.GetUtcNow(), completedDuration));
                nodeStartedTimestamp = null;
                var next = flow.Graph.ControlEdges.SingleOrDefault(e => e.Source == new PortRef(current, exit));
                if (next is null) return Complete(true, null);
                current = next.Target.NodeId;
            }
        }
        catch (OperationCanceledException error)
        {
            var diagnostic = FlowDiagnostics.Create(error, failureNodeId, failurePort, failureOrigin);
            steps.Add(new(failureNodeId, "cancelled", null, "cancelled") { Diagnostic = diagnostic });
            FlowDiagnostics.Attach(error, diagnostic, new(false, steps.ToArray(), new Dictionary<string, Dictionary<string, TypedValue>>(published), null, confirmed)
                { InferenceAttempts = snapshot.InferenceAttempts.ToArray() });
            snapshot.Status = ExecutionStatus.Cancelled; snapshot.Cursor = null;
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(failureNodeId, "cancelled", null, "cancelled", timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                    failurePort is null ? null : new Dictionary<string, string> { ["port"] = failurePort }));
            throw;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or HttpRequestException or InvalidOperationException or BoundaryCompilationException)
        {
            var diagnostic = FlowDiagnostics.Create(e, failureNodeId, failurePort, failureOrigin);
            steps.Add(new(failureNodeId, "failed", null, e is HttpRequestException ? "Serviço de modelo indisponível." : e.Message) { Diagnostic = diagnostic });
            FlowDiagnostics.Attach(e, diagnostic);
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(failureNodeId, "failed", null, e.GetType().Name, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                    failurePort is not null ? new Dictionary<string, string> { ["port"] = failurePort } :
                    e is BoundaryCompilationException failure ? new Dictionary<string, string>
                    {
                        ["sourceMemoryId"] = failure.SourceMemoryId,
                        ["status"] = failure.Status,
                        ["diagnosticCode"] = failure.DiagnosticCode,
                        ["taxonomyVersion"] = "v1",
                        ["expectedKind"] = BoundaryAdmission.Kind,
                        ["template"] = "no_teasing_about_topic:v1"
                    } : e.Data["failureCategory"] is "read_only_model_response_rejected" or "contained_tool_completion_rejected"
                        ? new Dictionary<string, string>
                        {
                            ["failureCategory"] = (string)e.Data["failureCategory"]!,
                            ["containedToolNodeId"] = e.Data["containedToolNodeId"] as string ?? "",
                            ["toolFailureContainmentVersion"] = e.Data["toolFailureContainmentVersion"] as string ?? "",
                            ["readOnlyModelNodeIds"] = JsonSerializer.Serialize(e.Data["readOnlyModelNodeIds"] as string[] ?? [])
                        } : null));
            // A failure after a confirmed send does not un-send it: the speech still reaches history, without a transition.
            return Complete(false, null);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // These exceptions have always escaped the runner. Preserve that behavior and expose partial evidence.
            var diagnostic = FlowDiagnostics.Create(error, failureNodeId, failurePort, failureOrigin);
            steps.Add(new(failureNodeId, "failed", null, error.GetType().Name) { Diagnostic = diagnostic });
            snapshot.Status = ExecutionStatus.Failed; snapshot.Cursor = null;
            FlowDiagnostics.Attach(error, diagnostic, new(false, steps.ToArray(), new Dictionary<string, Dictionary<string, TypedValue>>(published), null, confirmed)
                { InferenceAttempts = snapshot.InferenceAttempts.ToArray() });
            throw;
        }
        finally { snapshot.SendAttempted = sendAttempted; snapshot.ConfirmedSend = confirmed; }
    }

}
