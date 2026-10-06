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
        var snapshot = new ExecutionSnapshotV1 { Scope = identity.Scope, BatchId = identity.BatchId,
            ProjectRef = identity.ProjectRef, BatchRef = identity.BatchRef, StateId = identity.StateId,
            StateVersion = identity.StateVersion, EntryKind = identity.EntryKind, Cursor = flow.EntryId,
            Status = ExecutionStatus.Ready };
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
            "prompt" => StepEffect.Inference, "memory" => StepEffect.Memory,
            "interpretBoundaries" => StepEffect.Boundary, "send" => StepEffect.Send,
            "humanFeedback" => StepEffect.Feedback, _ => StepEffect.Pure
        }
    };
    internal static async Task<KernelStepResult> AdvanceAsync(TypedFlow flow, FlowExecutionState state,
        FlowExecutionServices services, CancellationToken token, bool oneNode = false)
    {
        if (state.Snapshot.Status != ExecutionStatus.Ready) throw new InvalidOperationException("Execution is not ready to advance.");
        var traces = new List<TypedNodeExecutionTrace>();
        var result = await ExecuteAdvanceAsync(flow, state, services with { Observer = trace => { traces.Add(trace); services.Observer?.Invoke(trace); } }, token, oneNode);
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
        var references = snapshot.References;
        var sendAttempted = snapshot.SendAttempted;
        ConfirmedSend? confirmed = snapshot.ConfirmedSend;
        string current = snapshot.Cursor ?? throw new InvalidOperationException("Execution has no active cursor.");
        var completion = services.Completion;
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
            if (type == FlowDomain.Boundary)
            {
                if (boundaryRegistry is null) throw new InvalidDataException("Boundary exige contexto de execução confiável.");
                return boundaryRegistry.ForModel(JsonSerializer.SerializeToElement(new[] { value }), timeProvider.GetUtcNow(), references)[0].Clone();
            }
            return FlowDomain.ForModel(value, type, references);
        }
        TypedValue Input(TypedNode node, TypedPort port)
        {
            var edge = flow.Graph.DataEdges.SingleOrDefault(e => e.Target == new PortRef(node.Source.Id, port.Id));
            if (edge is not null) return Resolve(edge.Source);
            if (port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out var local)) return new(port.Type, local.Clone());
            throw new InvalidDataException("Entrada não disponível: " + port.Name);
        }
        TypedValue Resolve(PortRef key)
        {
            if (values.TryGetValue(key, out var existing)) return existing;
            var node = flow.Nodes[key.NodeId];
            if (node.Source.Kind is not ("property" or "count")) throw new InvalidDataException("Output operacional ainda não disponível.");
            var input = Input(node, node.Inputs.Single());
            var projected = node.Source.Kind == "count"
                ? FlowDomain.CountItems(input.Value, input.Type)
                : FlowDomain.Project(input.Value, input.Type, TypedFlowCompiler.PropertyPath(node.Source.Config));
            var value = new TypedValue(node.Outputs.Single().Type, projected);
            values[key] = value; return value;
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
                        if (!port.Required && !flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id))) continue;
                        var input = Input(node, port);
                        FlowDomain.Check(input.Value, port.Type);
                        inputs.Add(port.Id, input);
                    }
                    var raw = await handler.ExecuteAsync(FlowNodeHandler.Config(node.Source), inputs.AsReadOnly(), token);
                    if (!raw.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(node.Outputs.Select(port => port.Id)))
                        throw new InvalidDataException("Host output ports do not match the declared ports.");
                    var output = new Dictionary<string, TypedValue>(StringComparer.Ordinal);
                    foreach (var port in node.Outputs)
                    {
                        FlowDomain.Check(raw[port.Id], port.Type);
                        output.Add(port.Id, new(port.Type, raw[port.Id].Clone()));
                    }
                    published[current] = output;
                    foreach (var (port, value) in output) values[new(current, port)] = value;
                }
                else if (node.Source.Kind == "humanFeedback")
                {
                    snapshot.Ordinal--; // This node remains pending until a durable resolution completes it.
                    var port = node.Inputs.Single();
                    var context = flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, port.Id))
                        ? Input(node, port).Value.GetString() : null;
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
                    TypedValue ProfileInput(string name)
                    {
                        var port = node.Inputs.Single(p => p.Name == name);
                        var value = Input(node, port);
                        FlowDomain.Check(value.Value, port.Type);
                        return value;
                    }
                    var output = await BoundaryEnforcementProfile.RunAsync(node, ProfileInput("limites").Value,
                        ProfileInput("resposta").Value.GetString()!, ProfileInput("mensagens").Value,
                        boundaryRegistry, completion, timeProvider,
                        metadata => observer?.Invoke(new(current, "boundary-policy", null, null, timeProvider.GetUtcNow(), TimeSpan.Zero, metadata)), token);
                    outbound?.Enforced(current, ProfileInput("resposta"), ProfileInput("limites"),
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
                        var value = Input(node, port); FlowDomain.Check(value.Value, port.Type);
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
                            TypedFlowRunner.OptionalText(config, "reasoningEffort"), config.TryGetProperty("maxOutputTokens", out var budget) ? budget.GetInt32() : null);
                        var text = await completion.CompleteAsync(request, token);
                        try { output = TypedFlowRunner.ValidateOutput(node, text, schema is not null); break; }
                        catch (Exception e) when (e is JsonException or InvalidDataException)
                        {
                            if (attempt == retries) throw new InvalidDataException("Resposta inválida após validação: " + (e is JsonException ? "JSON inválido." : e.Message));
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
                    var output = await boundaryRegistry.CompileAsync(Input(node, node.Inputs.Single()).Value,
                        BoundaryCompilerSettings.Read(node.Source.Config), timeProvider.GetUtcNow(), token);
                    var value = new TypedValue(new ArrayType(FlowDomain.Boundary), output);
                    outbound?.Compiled(current, Input(node, node.Inputs.Single()), value);
                    published[current] = new() { ["boundaries"] = value };
                    values[new(current, "boundaries")] = value;
                }
                else if (node.Source.Kind == "memory")
                {
                    if (memoryQuery is null) throw new InvalidDataException("Memória exige contexto de sessão.");
                    TypedValue? OptionalInput(string id)
                    {
                        var port = node.Inputs.Single(p => p.Id == id);
                        return flow.Graph.DataEdges.Any(e => e.Target == new PortRef(current, id)) ||
                            port.Config.ValueKind == JsonValueKind.Object && port.Config.TryGetProperty("value", out _)
                            ? Input(node, port) : null;
                    }
                    var response = await memoryQuery(node, OptionalInput("persons")?.Value, OptionalInput("query")?.Value.GetString(), token);
                    FlowDomain.Check(response, new ArrayType(FlowDomain.Memory));
                    var value = new TypedValue(new ArrayType(FlowDomain.Memory), response.Clone());
                    outbound?.Recall(current, value);
                    published[current] = new() { ["memories"] = value };
                    values[new(current, "memories")] = value;
                }
                else if (node.Source.Kind == "send")
                {
                    var candidate = Input(node, node.Inputs.Single());
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
                        var selected = Input(node, node.Inputs.Single()).Value.GetString()!;
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
                    var left = Input(node, node.Inputs.Single(p => p.Id == "a"));
                    var right = Input(node, node.Inputs.Single(p => p.Id == "b"));
                    exit = TypedBranch.Compare(left, right, node.Source.Config.GetProperty("settings").GetProperty("operator").GetString()!) ? "true" : "false";
                }
                else if (node.Source.Kind == "switch")
                {
                    var value = Input(node, node.Inputs.Single()).Value.GetString();
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
        catch (OperationCanceledException)
        {
            snapshot.Status = ExecutionStatus.Cancelled; snapshot.Cursor = null;
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(current, "cancelled", null, "cancelled", timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value)));
            throw;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or HttpRequestException or InvalidOperationException or BoundaryCompilationException)
        {
            steps.Add(new(current, "failed", null, e is HttpRequestException ? "Serviço de modelo indisponível." : e.Message));
            if (nodeStartedTimestamp is not null)
                observer?.Invoke(new(current, "failed", null, e.GetType().Name, timeProvider.GetUtcNow(), timeProvider.GetElapsedTime(nodeStartedTimestamp.Value),
                    e is BoundaryCompilationException failure ? new Dictionary<string, string>
                    {
                        ["sourceMemoryId"] = failure.SourceMemoryId,
                        ["status"] = failure.Status,
                        ["diagnosticCode"] = failure.DiagnosticCode,
                        ["taxonomyVersion"] = "v1",
                        ["expectedKind"] = BoundaryAdmission.Kind,
                        ["template"] = "no_teasing_about_topic:v1"
                    } : null));
            // A failure after a confirmed send does not un-send it: the speech still reaches history, without a transition.
            return Complete(false, null);
        }
        finally { snapshot.SendAttempted = sendAttempted; snapshot.ConfirmedSend = confirmed; }
    }

}
