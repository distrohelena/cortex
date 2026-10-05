using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Boundaries;
using Cortex.Core.Flows.Execution;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows.Durable;

public sealed class FlowExecutionDriver(FileExecutionArtifactStore store, IExecutionHostBindings bindings) : IFlowExecutionDriver
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Evidence(BoundaryExecutionEvidence? Boundaries, OutboundExecutionEvidence? Outbound);
    private sealed record StepCheckpoint(JsonElement Snapshot, IReadOnlyList<TypedNodeExecutionTrace> Traces);
    private sealed record Loaded(SessionFlowProject Project, ExecutionBatchV1 Batch, CompiledSessionFlowState State, TypedFlow Flow, DurableParticipationReviewV1? Review);
    private static string Operation(StepKey key) => DurableIdentity.OperationId(key);
    private const string InitialNode = "@initial-interpretation:";
    private static bool IsInitial(string? node) => node?.StartsWith(InitialNode, StringComparison.Ordinal) == true;

    public async Task<ExecutionDescriptor> InitializeAsync(ExecutionStart start, CancellationToken token)
    {
        using var lease = await store.OperationLocks.AcquireAsync("initialize-" + DurableIdentity.ExecutionWorkflowId(start.Scope, start.BatchId), token).ConfigureAwait(false);
        var existing = await store.FindAsync(start.Scope, "initial-snapshot", token).ConfigureAwait(false);
        if (existing is not null) return await DescribeAsync(existing, token).ConfigureAwait(false);
        if (start.ProjectRef.Scope != start.Scope || start.BatchRef.Scope != start.Scope) throw new InvalidDataException("Execution artifacts have foreign scopes.");
        var loaded = await LoadAsync(start.Scope, start.ProjectRef, start.BatchRef, start.StateId, start.EntryKind, token).ConfigureAwait(false);
        var state = FlowExecutionKernel.Start(loaded.Flow, loaded.Batch.Message, start, RuntimeOutputs(loaded));
        ExecutionHostBinding binding;
        try
        {
            binding = await BindingAsync(start.Scope, loaded.Project, token).ConfigureAwait(false);
            await ValidatePurposeAsync(loaded, binding, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            state.Snapshot.Status = ExecutionStatus.Failed; state.Snapshot.Cursor = null;
            return await SaveAsync(state.Snapshot, "initial-snapshot", token).ConfigureAwait(false);
        }
        if (loaded.Batch.InitialContexts.Count > 0 && (loaded.Batch.Purpose == SessionExecutionPurpose.Normal && binding.AnalyzeInitialAsync is not null
            || loaded.Batch.Reassess && binding.AnalyzeReassessmentAsync is not null))
        {
            state.Snapshot.InitialGraphCursor = state.Snapshot.Cursor;
            state.Snapshot.Cursor = InitialNode + "0";
        }
        return await SaveAsync(state.Snapshot, "initial-snapshot", token).ConfigureAwait(false);
    }

    public async Task<ExecutionDescriptor> AdvanceAsync(StepCommand command, CancellationToken token)
    {
        if (command.SnapshotRef.Scope != command.Key.Scope) throw new InvalidDataException("Command scope mismatch.");
        using var lease = await store.OperationLocks.AcquireAsync(Operation(command.Key), token).ConfigureAwait(false);
        var intent = await store.FindAsync(command.Key.Scope, "intent-" + Operation(command.Key), token).ConfigureAwait(false);
        if (intent is not null && (await ReadAsync<StepCommand>(intent, token).ConfigureAwait(false)) != command) throw new InvalidDataException("Conflicting step command.");
        var recovered = await RecoverUnderLeaseAsync(command.Key, token).ConfigureAwait(false);
        if (recovered is not null) return recovered;
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(command.SnapshotRef, token).ConfigureAwait(false), command.Key.Scope);
        ValidateCommand(snapshot, command, ExecutionStatus.Ready);
        var loaded = await LoadAsync(snapshot.Scope, snapshot.ProjectRef, snapshot.BatchRef, snapshot.StateId, snapshot.EntryKind, token).ConfigureAwait(false);
        var binding = await BindingAsync(snapshot.Scope, loaded.Project, token).ConfigureAwait(false);
        await ValidatePurposeAsync(loaded, binding, token).ConfigureAwait(false);
        if (IsInitial(snapshot.Cursor))
        {
            await store.PutOnceAsync(snapshot.Scope, "intent-" + Operation(command.Key), JsonSerializer.SerializeToElement(command, Options), token).ConfigureAwait(false);
            var index = snapshot.InitialInterpretations.Count;
            var analyze = loaded.Batch.Reassess ? binding.AnalyzeReassessmentAsync : binding.AnalyzeInitialAsync;
            if (snapshot.Cursor != InitialNode + index || index >= loaded.Batch.InitialContexts.Count || analyze is null)
                throw new UnauthorizedAccessException("Initial interpretation is not currently bound.");
            var context = loaded.Batch.InitialContexts[index];
            if (context.Snapshot.Session != snapshot.Scope.Session || context.Snapshot.ProjectRevision != snapshot.Scope.ProjectRevision
                || context.History.Any(h => h.AdmissionWatermark > (loaded.Batch.Reassess ? context.Snapshot.AdmissionWatermark : context.Original.AdmissionWatermark))
                || !loaded.Batch.Reassess && context.Snapshot.AdmissionWatermark != context.Original.AdmissionWatermark) throw new InvalidDataException("Invalid captured interpretation prefix.");
            var analysis = await analyze(context, token).ConfigureAwait(false);
            await ValidatePurposeAsync(loaded, binding, token).ConfigureAwait(false);
            PendingParticipationReviews.ValidateAnalysis(analysis, context);
            snapshot.InitialInterpretations.Add(context.Original.ProviderMessageId, analysis with { EvidenceMessageIds = analysis.EvidenceMessageIds.ToArray() });
            snapshot.Steps.Add(new(snapshot.Cursor!, "completed", "then", null)); snapshot.Ordinal++;
            snapshot.Cursor = index + 1 < loaded.Batch.InitialContexts.Count ? InitialNode + (index + 1) : snapshot.InitialGraphCursor;
            snapshot.InitialGraphCursor = index + 1 < loaded.Batch.InitialContexts.Count ? snapshot.InitialGraphCursor : null;
            if (loaded.Batch.Reassess && (!analysis.Confident || analysis.Opportunity != ReassessmentOpportunity.Relevant))
            { snapshot.Cursor = null; snapshot.InitialGraphCursor = null; snapshot.Status = ExecutionStatus.Completed; }
            return await SaveAsync(snapshot, "after-" + Operation(command.Key), token).ConfigureAwait(false);
        }
        var memoryTraces = new List<TypedNodeExecutionTrace>();
        var services = Services(snapshot, loaded, binding, memoryTraces.Add);
        var effect = FlowExecutionKernel.Effect(loaded.Flow.Nodes[snapshot.Cursor!]);
        if (intent is not null && effect is StepEffect.Inference or StepEffect.Send or StepEffect.Memory or StepEffect.Boundary)
        {
            snapshot.Status = effect == StepEffect.Send ? ExecutionStatus.ReconciliationRequired : ExecutionStatus.Failed;
            return await SaveAsync(snapshot, "after-" + Operation(command.Key), token).ConfigureAwait(false);
        }
        await store.PutOnceAsync(snapshot.Scope, "intent-" + Operation(command.Key), JsonSerializer.SerializeToElement(command, Options), token).ConfigureAwait(false);
        var state = new FlowExecutionState(snapshot, RuntimeOutputs(loaded));
        var result = await FlowExecutionKernel.AdvanceAsync(loaded.Flow, state, services, token).ConfigureAwait(false);
        await ValidatePurposeAsync(loaded, binding, token).ConfigureAwait(false);
        snapshot.TrustedEvidence = JsonSerializer.SerializeToElement(new Evidence(services.BoundaryRegistry?.CaptureEvidence(), services.Outbound?.CaptureEvidence(snapshot.Values)), Options);
        var checkpoint = new StepCheckpoint(ExecutionSnapshotCodec.Encode(snapshot), result.Traces.Concat(memoryTraces).OrderBy(t => t.Timestamp).ToArray());
        await store.PutOnceAsync(snapshot.Scope, "step-result-" + Operation(command.Key), JsonSerializer.SerializeToElement(checkpoint, Options), token).ConfigureAwait(false);
        return await PublishCheckpointAsync(command.Key, checkpoint, token).ConfigureAwait(false);
    }

    public async Task<ExecutionDescriptor?> RecoverAsync(StepKey key, CancellationToken token)
    {
        using var lease = await store.OperationLocks.AcquireAsync(Operation(key), token).ConfigureAwait(false);
        return await RecoverUnderLeaseAsync(key, token).ConfigureAwait(false);
    }
    private async Task<ExecutionDescriptor?> RecoverUnderLeaseAsync(StepKey key, CancellationToken token)
    {
        var reference = await store.FindAsync(key.Scope, "after-" + Operation(key), token).ConfigureAwait(false);
        if (reference is not null) return await DescribeAsync(reference, token).ConfigureAwait(false);
        var checkpointRef = await store.FindAsync(key.Scope, "step-result-" + Operation(key), token).ConfigureAwait(false);
        if (checkpointRef is not null) return await PublishCheckpointAsync(key, await ReadAsync<StepCheckpoint>(checkpointRef, token).ConfigureAwait(false), token).ConfigureAwait(false);
        var intentRef = await store.FindAsync(key.Scope, "intent-" + Operation(key), token).ConfigureAwait(false);
        if (intentRef is null) return null;
        var command = await ReadAsync<StepCommand>(intentRef, token).ConfigureAwait(false);
        if (command.Key != key || command.SnapshotRef.Scope != key.Scope) throw new InvalidDataException("Recovery intent scope mismatch.");
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(command.SnapshotRef, token).ConfigureAwait(false), key.Scope);
        ValidateCommand(snapshot, command, ExecutionStatus.Ready);
        var loaded = await LoadAsync(snapshot.Scope, snapshot.ProjectRef, snapshot.BatchRef, snapshot.StateId, snapshot.EntryKind, token).ConfigureAwait(false);
        var node = IsInitial(key.NodeId) ? null : loaded.Flow.Nodes[key.NodeId]; var effect = node is null ? StepEffect.Inference : FlowExecutionKernel.Effect(node);
        if (effect is StepEffect.Pure or StepEffect.Artifact or StepEffect.Feedback) return null;
        snapshot.Status = effect == StepEffect.Send ? ExecutionStatus.ReconciliationRequired : ExecutionStatus.Failed;
        if (effect == StepEffect.Send)
        {
            var binding = await bindings.ResolveAsync(key.Scope, token).ConfigureAwait(false);
            if (binding.Scope != key.Scope || !JsonElement.DeepEquals(binding.Project, loaded.Project.Document)) throw new InvalidDataException("Recovery host scope mismatch.");
            // A durable provider receipt is a fact. Reading it does not rebind inference, restore expired memory, or rearm dispatch.
            var outbound = binding.OutboundHost?.Begin(binding.Project, key.Scope.ExecutionId, key.Scope.Session, snapshot.EntryKind,
                loaded.Batch.ProviderMessageIds, loaded.Batch.ReplyTargetIds, loaded.Batch.ShadowOnly);
            var result = outbound is null ? null : binding.OutboundHost!.RecoverExecutionSend(outbound);
            snapshot.SendAttempted = true;
            if (result is not null && result.Status != "indeterminate")
            {
                if (result.Status == "sent")
                {
                    if (result.RecoveredText is null || result.ProviderMessageId is null || result.Speaker is null) throw new InvalidDataException("Incomplete durable send receipt.");
                    snapshot.ConfirmedSend = new(key.NodeId, result.ProviderMessageId, result.RecoveredText, result.Speaker) { ReplyToMessageId = result.ReplyToMessageId };
                }
                snapshot.Outputs[key.NodeId] = new()
                {
                    ["status"] = new(node!.Outputs.Single(p => p.Id == "status").Type, JsonSerializer.SerializeToElement(result.Status)),
                    ["diagnostico"] = new(new StringType(), JsonSerializer.SerializeToElement(result.DiagnosticCode)),
                };
                foreach (var output in snapshot.Outputs[key.NodeId]) snapshot.Values[new(key.NodeId, output.Key)] = output.Value;
                snapshot.Steps.Add(new(key.NodeId, "completed", result.Status, null)); snapshot.Ordinal++;
                snapshot.Cursor = loaded.Flow.Graph.ControlEdges.SingleOrDefault(e => e.Source == new PortRef(key.NodeId, result.Status))?.Target.NodeId;
                snapshot.Status = snapshot.Cursor is null ? ExecutionStatus.Completed : ExecutionStatus.Ready;
            }
        }
        return await SaveAsync(snapshot, "after-" + Operation(key), token).ConfigureAwait(false);
    }

    private async Task<ExecutionDescriptor> PublishCheckpointAsync(StepKey key, StepCheckpoint checkpoint, CancellationToken token)
    {
        var snapshot = ExecutionSnapshotCodec.Decode(checkpoint.Snapshot, key.Scope);
        if (snapshot.Ordinal < key.Ordinal || snapshot.Ordinal == key.Ordinal && snapshot.Status != ExecutionStatus.WaitingForHuman)
            throw new InvalidDataException("Invalid step checkpoint ordinal.");
        await store.PutOnceAsync(key.Scope, "traces-" + Operation(key), JsonSerializer.SerializeToElement(checkpoint.Traces, Options), token).ConfigureAwait(false);
        return await SaveAsync(snapshot, "after-" + Operation(key), token).ConfigureAwait(false);
    }

    public async Task<ExecutionDescriptor> CancelAsync(ArtifactRef snapshotRef, CancellationToken token)
    {
        using var lease = await store.OperationLocks.AcquireAsync("cancel-" + snapshotRef.Scope.ExecutionId, token).ConfigureAwait(false);
        var key = "cancelled-" + snapshotRef.Sha256;
        var existing = await store.FindAsync(snapshotRef.Scope, key, token).ConfigureAwait(false);
        if (existing is not null) return await DescribeAsync(existing, token).ConfigureAwait(false);
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(snapshotRef, token).ConfigureAwait(false), snapshotRef.Scope);
        if (snapshot.Status != ExecutionStatus.Ready) throw new InvalidDataException("Only a ready execution can use this cancellation checkpoint.");
        snapshot.Status = ExecutionStatus.Cancelled; snapshot.Cursor = null;
        return await SaveAsync(snapshot, key, token).ConfigureAwait(false);
    }

    public async Task ValidateFeedbackAsync(FeedbackSubmission submission, CancellationToken token)
    {
        if (submission.ResponseRef.Scope != submission.Scope) throw new InvalidDataException("Feedback scope mismatch.");
        var response = await ReadAsync<FeedbackResponseV1>(submission.ResponseRef, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.Text) || response.OperationId != submission.OperationId || response.RequestId != submission.RequestId
            || response.RequestDigest != submission.RequestDigest || response.AuthenticatedActorRef != submission.AuthenticatedActorRef)
            throw new InvalidDataException("Feedback attestation mismatch.");
        var binding = await bindings.ResolveAsync(submission.Scope, token).ConfigureAwait(false);
        if (binding.Scope != submission.Scope || binding.AuthorizeFeedbackAsync is null) throw new UnauthorizedAccessException("Feedback operator is not authorized.");
        await binding.AuthorizeFeedbackAsync(submission, token).ConfigureAwait(false);
    }

    public async Task<ExecutionDescriptor> ResolveFeedbackAsync(StepCommand command, FeedbackResolution resolution, CancellationToken token)
    {
        if (command.SnapshotRef.Scope != command.Key.Scope) throw new InvalidDataException("Feedback command scope mismatch.");
        using var lease = await store.OperationLocks.AcquireAsync("resolve-" + Operation(command.Key), token).ConfigureAwait(false);
        var resolutionKey = "resolution-" + Operation(command.Key);
        var recorded = await store.FindAsync(command.Key.Scope, resolutionKey, token).ConfigureAwait(false);
        if (recorded is not null && await ReadAsync<FeedbackResolution>(recorded, token).ConfigureAwait(false) != resolution) throw new InvalidDataException("Feedback already resolved differently.");
        var existing = await store.FindAsync(command.Key.Scope, "resolved-" + Operation(command.Key), token).ConfigureAwait(false);
        if (existing is not null) return await DescribeAsync(existing, token).ConfigureAwait(false);
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(command.SnapshotRef, token).ConfigureAwait(false), command.Key.Scope);
        ValidateCommand(snapshot, command, ExecutionStatus.WaitingForHuman);
        var pending = snapshot.PendingFeedback ?? throw new InvalidDataException("No feedback request.");
        if (resolution.Request.RequestId != pending.RequestId || resolution.Request.RequestDigest != pending.RequestDigest
            || resolution.Request.RequestRef.Scope != snapshot.Scope) throw new InvalidDataException("Wrong feedback request.");
        var request = await ReadAsync<FeedbackRequestV1>(resolution.Request.RequestRef, token).ConfigureAwait(false);
        if (request != pending) throw new InvalidDataException("Wrong feedback request artifact.");
        var loaded = await LoadAsync(snapshot.Scope, snapshot.ProjectRef, snapshot.BatchRef, snapshot.StateId, snapshot.EntryKind, token).ConfigureAwait(false);
        var settings = HumanFeedbackNode.Read(loaded.Flow.Nodes[snapshot.Cursor!].Source.Config);
        string? exit;
        if (resolution.Status == "answered" && resolution.ResponseRef is { } responseRef)
        {
            var response = await ReadAsync<FeedbackResponseV1>(responseRef, token).ConfigureAwait(false);
            await ValidateFeedbackAsync(new(response.OperationId, snapshot.Scope, response.RequestId, response.RequestDigest, responseRef, response.AuthenticatedActorRef), token).ConfigureAwait(false);
            if (response.RequestId != pending.RequestId || response.RequestDigest != pending.RequestDigest) throw new InvalidDataException("Wrong response identity.");
            var value = new TypedValue(new StringType(), JsonSerializer.SerializeToElement(response.Text));
            snapshot.Values[new(snapshot.Cursor!, "feedback")] = value;
            snapshot.Outputs[snapshot.Cursor!] = new() { ["feedback"] = value };
            exit = "then";
        }
        else if (resolution.Status == "timeout" && settings.TimeoutSeconds is not null && resolution.ResponseRef is null) exit = "timeout";
        else if (resolution.Status == "cancelled" && resolution.ResponseRef is null) exit = null;
        else throw new InvalidDataException("Invalid feedback resolution.");
        await store.PutOnceAsync(snapshot.Scope, resolutionKey, JsonSerializer.SerializeToElement(resolution, Options), token).ConfigureAwait(false);
        snapshot.Steps.Add(new(snapshot.Cursor!, exit is null ? "cancelled" : "completed", exit, null));
        snapshot.Ordinal++; snapshot.PendingFeedback = null;
        snapshot.Cursor = exit is null ? null : loaded.Flow.Graph.ControlEdges.SingleOrDefault(e => e.Source == new PortRef(snapshot.Cursor!, exit))?.Target.NodeId;
        snapshot.Status = exit is null ? ExecutionStatus.Cancelled : snapshot.Cursor is null ? ExecutionStatus.Completed : ExecutionStatus.Ready;
        return await SaveAsync(snapshot, "resolved-" + Operation(command.Key), token).ConfigureAwait(false);
    }

    private async Task<ExecutionDescriptor> SaveAsync(ExecutionSnapshotV1 snapshot, string key, CancellationToken token)
    {
        var reference = await store.PutOnceAsync(snapshot.Scope, key, ExecutionSnapshotCodec.Encode(snapshot), token).ConfigureAwait(false);
        return await DescribeAsync(reference, token).ConfigureAwait(false);
    }
    private async Task<ExecutionDescriptor> DescribeAsync(ArtifactRef reference, CancellationToken token)
    {
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(reference, token).ConfigureAwait(false), reference.Scope);
        var loaded = await LoadAsync(snapshot.Scope, snapshot.ProjectRef, snapshot.BatchRef, snapshot.StateId, snapshot.EntryKind, token).ConfigureAwait(false);
        FeedbackDescriptor? feedback = null;
        if (snapshot.PendingFeedback is { } pending)
        {
            var requestRef = await store.PutOnceAsync(snapshot.Scope, pending.RequestId, JsonSerializer.SerializeToElement(pending, Options), token).ConfigureAwait(false);
            feedback = new(pending.RequestId, requestRef, pending.RequestDigest, pending.Key.NodeId, pending.Key.Ordinal,
                HumanFeedbackNode.Read(loaded.Flow.Nodes[pending.Key.NodeId].Source.Config).TimeoutSeconds);
        }
        return new(snapshot.Scope, reference, snapshot.Cursor, snapshot.Ordinal, snapshot.Status,
            IsInitial(snapshot.Cursor) ? StepEffect.Inference : snapshot.Cursor is { } nodeId ? FlowExecutionKernel.Effect(loaded.Flow.Nodes[nodeId]) : StepEffect.Pure,
            feedback, snapshot.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled or ExecutionStatus.ReconciliationRequired ? reference : null,
            snapshot.Status is ExecutionStatus.Failed ? "flow-execution-failed" : snapshot.Status is ExecutionStatus.ReconciliationRequired ? "reconciliation-required" : null);
    }
    private static void ValidateCommand(ExecutionSnapshotV1 snapshot, StepCommand command, ExecutionStatus expected)
    {
        if (snapshot.Scope != command.Key.Scope || snapshot.Cursor != command.Key.NodeId || snapshot.Ordinal != command.Key.Ordinal || snapshot.Status != expected)
            throw new InvalidDataException("Stale step cursor, ordinal or scope.");
    }
    private async Task<ExecutionHostBinding> BindingAsync(ArtifactScope scope, SessionFlowProject project, CancellationToken token)
    {
        var binding = await bindings.ResolveAsync(scope, token).ConfigureAwait(false);
        if (binding.Scope != scope || !JsonElement.DeepEquals(binding.Project, project.Document)) throw new InvalidDataException("Host does not bind the captured revision.");
        binding.ValidateCurrentContext?.Invoke();
        return binding;
    }
    private async Task<Loaded> LoadAsync(ArtifactScope scope, ArtifactRef projectRef, ArtifactRef batchRef, string stateId, SessionEntryKind kind, CancellationToken token)
    {
        if (projectRef.Scope != scope || batchRef.Scope != scope) throw new InvalidDataException("Wrong captured artifact scope.");
        var project = await ReadAsync<SessionFlowProject>(projectRef, token).ConfigureAwait(false);
        if (project.ProjectRevision != scope.ProjectRevision) throw new InvalidDataException("Wrong captured project revision.");
        var compiled = SessionFlowProjectCompiler.Compile(project);
        var batch = await ReadAsync<ExecutionBatchV1>(batchRef, token).ConfigureAwait(false);
        FlowDomain.Check(batch.Message, FlowDomain.Message);
        if (batch.Message.GetProperty("canal").GetProperty("nativeId").GetString() != scope.Session.NativeConversationId
            || batch.ProviderMessageIds.Count == 0 || batch.ProviderMessageIds.Any(string.IsNullOrWhiteSpace)
            || batch.ProviderMessageIds.Distinct(StringComparer.Ordinal).Count() != batch.ProviderMessageIds.Count)
            throw new InvalidDataException("Private batch does not bind the exact conversation or origin identities.");
        if (!compiled.States.TryGetValue(stateId, out var state) || !state.Entries.TryGetValue(kind, out var flow) || flow.EntryId != batch.EntryId)
            throw new InvalidDataException("Wrong captured state or entry.");
        if (!Enum.IsDefined(batch.Purpose)) throw new InvalidDataException("Unknown execution purpose.");
        if (flow.Nodes.Keys.Any(IsInitial)) throw new InvalidDataException("Node uses a reserved execution identity.");
        DurableParticipationReviewV1? review = null;
        if (batch.Purpose == SessionExecutionPurpose.ParticipationReview)
        {
            var definition = batch.ReviewDefinition ?? throw new InvalidDataException("Missing private review definition.");
            if (definition.ProjectRevision != scope.ProjectRevision) throw new InvalidDataException("Wrong review revision.");
            flow = ParticipationReviewCut.Create(flow, definition).Flow;
        }
        else if (batch.Purpose is SessionExecutionPurpose.ResponsePreview or SessionExecutionPurpose.ReviewedResponse)
        {
            var reference = batch.ReviewRef ?? throw new InvalidDataException("A host-issued review artifact is required.");
            if (reference.Scope.InstallationId != scope.InstallationId || reference.Scope.Session != scope.Session || reference.Scope.ProjectRevision != scope.ProjectRevision)
                throw new InvalidDataException("Foreign trusted review artifact.");
            review = await ReadAsync<DurableParticipationReviewV1>(reference, token).ConfigureAwait(false);
            if (review.Scope != reference.Scope || review.Result.Status != ParticipationReviewStatus.Completed || review.Result.Decision != ParticipationReviewDecision.Respond
                || review.Result.Snapshot != batch.ReviewSnapshot || review.CompletedPromptResponses.Count == 0
                || review.CompletedPromptResponses.Keys.Any(id => !review.Definition.PromptNodeIds.Contains(id))) throw new InvalidDataException("Invalid trusted review completion.");
            _ = ParticipationReviewCut.Create(flow, review.Definition);
        }
        return new(project, batch, state, flow, review);
    }
    private static async Task ValidatePurposeAsync(Loaded loaded, ExecutionHostBinding binding, CancellationToken token)
    {
        if (loaded.Batch.Purpose == SessionExecutionPurpose.Normal) return;
        var expected = loaded.Batch.ReviewSnapshot ?? throw new InvalidDataException("Missing review context.");
        if (expected.Session != binding.Scope.Session || expected.ProjectRevision != binding.Scope.ProjectRevision || binding.ValidateReviewContextAsync is null)
            throw new UnauthorizedAccessException("Review context has no current host attestation.");
        if (loaded.Batch.Purpose == SessionExecutionPurpose.ResponsePreview && (binding.OutboundHost?.EffectiveMode != OutboundMode.Shadow || !loaded.Batch.ShadowOnly)
            || loaded.Batch.Purpose == SessionExecutionPurpose.ReviewedResponse && binding.OutboundHost?.EffectiveMode != OutboundMode.Enabled)
            throw new UnauthorizedAccessException("Review continuation ceiling is not armed.");
        await binding.ValidateReviewContextAsync(expected, token).ConfigureAwait(false);
    }
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>> RuntimeOutputs(Loaded loaded) =>
        loaded.Batch.RuntimeOutputs.ToDictionary(x => x.Key, x => (IReadOnlyDictionary<string, TypedValue>)x.Value.ToDictionary(p => p.Key,
            p => new TypedValue(loaded.Flow.Nodes[x.Key].Outputs.Single(o => o.Id == p.Key).Type, p.Value.Clone())));
    private static FlowExecutionServices Services(ExecutionSnapshotV1 snapshot, Loaded loaded, ExecutionHostBinding binding, Action<TypedNodeExecutionTrace>? memoryTrace = null)
    {
        if (loaded.Batch.ReviewSnapshot is { } expected && binding.ValidateReviewContextAsync is { } validate)
        {
            var hostCheck = binding.ValidateCurrentContext;
            // The physical sink rechecks current context, including after feedback or while another admission races generation.
            binding = binding with { ValidateCurrentContext = () => { hostCheck?.Invoke(); validate(expected, CancellationToken.None).GetAwaiter().GetResult(); } };
        }
        var evidence = snapshot.TrustedEvidence.TryGetProperty("Boundaries", out _) ? snapshot.TrustedEvidence.Deserialize<Evidence>(Options) : null;
        var ns = loaded.Flow.Nodes.Values.Any(n => n.Source.Kind is "memory" or "interpretBoundaries") ? SessionFlowRuntime.ConversationNamespace(snapshot.Scope.Session) : "";
        var registry = evidence?.Boundaries is { } boundary ? BoundaryExecutionRegistry.RestoreEvidence(ns, boundary, binding.Clock.GetUtcNow(), binding.Clock) : new(ns, binding.Clock);
        var outbound = binding.OutboundHost is null ? null : evidence?.Outbound is { } stored
            ? binding.OutboundHost.RestoreExecution(binding, stored, snapshot.Values)
            : binding.OutboundHost.Begin(binding.Project, snapshot.Scope.ExecutionId, snapshot.Scope.Session, snapshot.EntryKind, loaded.Batch.ProviderMessageIds,
                loaded.Batch.ReplyTargetIds, loaded.Batch.ShadowOnly, binding.ValidateCurrentContext);
        IFlowCompletion completion = binding.CompletionFactory.Create(new(snapshot.Scope.Session, snapshot.Scope.ExecutionId, snapshot.BatchId,
            loaded.Project.ProjectId, loaded.Project.ProjectVersion, loaded.Project.ProjectRevision));
        if (loaded.Batch.Purpose == SessionExecutionPurpose.ParticipationReview)
            completion = new SessionFlowRuntime.ParticipationReviewCompletion(completion, binding.ValidateCurrentContext ?? (() => { }),
                loaded.Batch.OriginalTimestamp, loaded.Batch.ContextCapturedAt, snapshot.InitialInterpretations.Values.SingleOrDefault()?.Interpretation, completedPromptResponses: snapshot.PromptResponses);
        else if (loaded.Review is { } review)
            completion = new SessionFlowRuntime.ReviewedParticipationCompletion(new SessionFlowRuntime.ParticipationReviewCompletion(completion,
                binding.ValidateCurrentContext ?? (() => { }), loaded.Batch.OriginalTimestamp, loaded.Batch.ContextCapturedAt, review.Result.Assessment?.Interpretation, participationOnly: false),
                review.CompletedPromptResponses, binding.ValidateCurrentContext ?? (() => { }), _ => { });
        completion = new RecordingCompletion(completion, snapshot.PromptResponses);
        if (snapshot.InitialInterpretations.Count > 0) completion = new SessionFlowRuntime.InitialInterpretationCompletion(completion, snapshot.InitialInterpretations);
        return new(completion) { Clock = binding.Clock, BoundaryRegistry = registry, Outbound = outbound, InstructionContext = loaded.State.InstructionContext,
            MemoryQuery = (node, persons, query, token) => SessionMemoryQuery.QueryAsync(snapshot.Scope.Session, node, persons, query, registry, binding.MemoryReader, binding.Clock, token,
                (name, duration, metadata) => memoryTrace?.Invoke(new(node.Source.Id, name, null, null, binding.Clock.GetUtcNow(), TimeSpan.FromMilliseconds(duration), metadata))),
            InstructionOutput = binding.InstructionOutput is null ? null : (node, outputs, token) => binding.InstructionOutput(new(snapshot.Scope.ExecutionId, snapshot.BatchId, snapshot.Scope.Session,
                snapshot.StateId, snapshot.StateVersion, snapshot.Scope.ProjectRevision, node, outputs), token),
            NodeStarting = binding.NodeStarting is null ? null : (node, outputs, token) => binding.NodeStarting(new(snapshot.Scope.ExecutionId, snapshot.BatchId, snapshot.Scope.Session,
                snapshot.StateId, snapshot.StateVersion, snapshot.Scope.ProjectRevision, node, outputs), token) };
    }
    private sealed class RecordingCompletion(IFlowCompletion inner, IDictionary<string, string> responses) : IFlowCompletion
    {
        public async Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            var response = await inner.CompleteAsync(request, token).ConfigureAwait(false);
            responses[request.NodeId] = response;
            return response;
        }
    }
    private async Task<T> ReadAsync<T>(ArtifactRef reference, CancellationToken token) =>
        (await store.ReadAsync(reference, token).ConfigureAwait(false)).Deserialize<T>(Options) ?? throw new InvalidDataException("Missing private artifact.");
}
