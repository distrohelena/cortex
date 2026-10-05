using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows.Sessions;

public sealed partial class DurableSessionArtifacts : ISessionReviewArtifactServices
{
    private sealed record ReviewBudget(string OperationId, string InputKey, long OriginalWatermark, long AttemptWatermark);

    public static bool ReviewContextMatches(SessionSnapshot expected, SessionSnapshot current) => expected.Session == current.Session
        && expected.ProjectId == current.ProjectId && expected.ProjectVersion == current.ProjectVersion && expected.ProjectRevision == current.ProjectRevision
        && expected.CurrentStateId == current.CurrentStateId && expected.StateVersion == current.StateVersion
        && expected.EnteredAt == current.EnteredAt && expected.AdmissionWatermark == current.AdmissionWatermark;

    public async Task<PreparedSessionReview> PrepareReviewAsync(ReviewPreparationCommand command, CancellationToken token)
    {
        var context = command.Snapshot;
        if (command.RequestRef.Scope.InstallationId != command.InstallationId || command.RequestRef.Scope.Session != context.Session
            || command.RequestRef.Scope.ProjectRevision != context.ProjectRevision || context.OpenBufferCount != 0 || context.PendingExecutionCount != 0)
            throw new InvalidDataException("Review requires an idle exact session scope.");
        var operation = await Read<SessionReviewOperationV1>(command.RequestRef, token).ConfigureAwait(false);
        if (operation.ExpectedSnapshot is { } expected && !ReviewContextMatches(expected, context)) throw new InvalidOperationException("interpretation-context-stale");
        if (operation.Purpose is not (SessionExecutionPurpose.ParticipationReview or SessionExecutionPurpose.ResponsePreview or SessionExecutionPurpose.ReviewedResponse))
            throw new InvalidDataException("Invalid review operation purpose.");
        operation.Policy.Validate();
        DurableIdentity.Require(operation.OperationId); DurableIdentity.Require(operation.OriginalMessageId);
        var scope = new ArtifactScope(command.InstallationId, context.Session,
            DurableIdentity.ExecutionId(command.InstallationId, context.Session, operation.OperationId, "review-" + operation.Purpose), context.ProjectRevision);
        using var lease = await store.OperationLocks.AcquireAsync("prepare-review-" + scope.ExecutionId, token).ConfigureAwait(false);
        var prior = await store.FindAsync(scope, "prepared-review", token).ConfigureAwait(false);
        if (prior is not null)
        {
            var previousPreparation = await Read<PreparedSessionReview>(prior, token).ConfigureAwait(false);
            if (previousPreparation.RequestRef != command.RequestRef || previousPreparation.Snapshot != command.Snapshot) throw new InvalidDataException("Conflicting review preparation.");
            return previousPreparation;
        }
        var publication = await ReadPublicationAsync(command.PublicationRef, token).ConfigureAwait(false);
        if (publication.Snapshot.ProjectId != context.ProjectId || publication.Snapshot.ProjectVersion != context.ProjectVersion || publication.Snapshot.ProjectRevision != context.ProjectRevision)
            throw new InvalidDataException("Review publication is stale.");
        var history = await History(command.InstallationId, context.Session, token).ConfigureAwait(false);
        var original = history.SingleOrDefault(h => h.Entry.ProviderMessageId == operation.OriginalMessageId)?.Entry;
        if (original is null || original.Direction != SessionMessageDirection.Inbound) throw new InvalidOperationException("retained-inbound-unavailable");
        var entry = publication.Entries.SingleOrDefault(e => e.StateId == context.CurrentStateId && e.Entry.Kind == original.EntryKind)?.Entry
            ?? throw new InvalidOperationException("current-state-entry-unavailable");
        var binding = await bindings.ResolveAsync(scope, token).ConfigureAwait(false);
        if (binding.Scope != scope || binding.OutboundHost?.RequiresReconciliation(context.Session) == true) throw new InvalidOperationException("context-unavailable");
        var outcomes = new List<SessionFlowOutcome>(); string? cursor = null;
        do
        {
            var page = await ReadOutcomesAsync(command.InstallationId, context.Session, new(cursor, 1000), token).ConfigureAwait(false);
            outcomes.AddRange(page.Items); cursor = page.NextCursor;
        } while (cursor is not null);
        var executions = outcomes.Where(o => o.ProviderMessageIds.Contains(operation.OriginalMessageId)).Select(o => o.ExecutionId).ToHashSet(StringComparer.Ordinal);
        if (history.Any(h => h.ConfirmedSendOf is not null && executions.Contains(h.ConfirmedSendOf))) throw new InvalidOperationException("already-answered");
        if (operation.Purpose == SessionExecutionPurpose.ParticipationReview)
        {
            var definition = operation.Definition ?? throw new InvalidDataException("Review cut required.");
            if (context.AdmissionWatermark - original.AdmissionWatermark >= operation.Policy.MessageHorizon) throw new InvalidOperationException("distance-exceeded");
            var project = await Read<SessionFlowProject>(publication.ProjectRef, token).ConfigureAwait(false);
            var compiled = SessionFlowProjectCompiler.Compile(project);
            if (definition.ProjectRevision != context.ProjectRevision) throw new InvalidDataException("Review cut revision mismatch.");
            _ = ParticipationReviewCut.Create(compiled.States[context.CurrentStateId].Entries[original.EntryKind], definition);
            var inputKey = DurableIdentity.AdmissionInputKey(command.InstallationId, context.Session, operation.OriginalMessageId);
            var budgets = new List<ReviewBudget>();
            foreach (var reference in await store.ListAsync(LedgerScope(command.InstallationId, context.Session), "review-budget-", token).ConfigureAwait(false))
                budgets.Add(await Read<ReviewBudget>(reference, token).ConfigureAwait(false));
            var own = budgets.Where(b => b.InputKey == inputKey).ToArray();
            if (!own.Any(b => b.OperationId == operation.OperationId))
            {
                if (own.Length >= operation.Policy.AttemptBudget || own.Any(b => b.AttemptWatermark >= context.AdmissionWatermark)) throw new InvalidOperationException("attempt-budget-exhausted");
                if (own.Length == 0 && budgets.Where(b => context.AdmissionWatermark - b.OriginalWatermark < operation.Policy.MessageHorizon).Select(b => b.InputKey).Distinct().Count() >= operation.Policy.Capacity)
                    throw new InvalidOperationException("capacity-exhausted");
                await Put(LedgerScope(command.InstallationId, context.Session), "review-budget-" + scope.ExecutionId,
                    new ReviewBudget(operation.OperationId, inputKey, original.AdmissionWatermark, context.AdmissionWatermark), token).ConfigureAwait(false);
            }
        }
        else
        {
            var reference = operation.ReviewRef ?? throw new InvalidDataException("Host-issued review artifact required.");
            if (reference.Scope.InstallationId != scope.InstallationId || reference.Scope.Session != scope.Session || reference.Scope.ProjectRevision != scope.ProjectRevision)
                throw new InvalidDataException("Foreign review artifact.");
            var review = await Read<DurableParticipationReviewV1>(reference, token).ConfigureAwait(false);
            if (review.Result.OriginalMessageId != operation.OriginalMessageId || review.Result.Snapshot != context || review.Result.Status != ParticipationReviewStatus.Completed
                || review.Result.Decision != ParticipationReviewDecision.Respond || review.CompletedPromptResponses.Count == 0)
                throw new InvalidOperationException("reviewed-response-context-stale");
            if (operation.Purpose == SessionExecutionPurpose.ResponsePreview && binding.OutboundHost?.EffectiveMode != OutboundMode.Shadow
                || operation.Purpose == SessionExecutionPurpose.ReviewedResponse && binding.OutboundHost?.EffectiveMode != OutboundMode.Enabled)
                throw new InvalidOperationException("review-continuation-ceiling-required");
            // Preview and live continuation share the once-only response reservation for the original message.
            await Put(LedgerScope(command.InstallationId, context.Session), "response-attempt-" + DurableIdentity.AdmissionInputKey(command.InstallationId, context.Session, operation.OriginalMessageId), operation, token).ConfigureAwait(false);
        }
        var envelope = new SessionMessageEnvelope(original.ProviderMessageId, original.ChannelId, original.AccountId, original.NativeConversationId, original.Timestamp, original.Direction, original.EntryKind, original.Message);
        var inputRef = await Put(scope, "review-original", envelope, token).ConfigureAwait(false);
        var admission = new AdmissionDescriptor(DurableIdentity.AdmissionInputKey(scope.InstallationId, scope.Session, original.ProviderMessageId), inputRef, inputRef.Sha256, original.EntryKind, original.Direction, original.Timestamp);
        var batchId = DurableIdentity.ExecutionId(command.InstallationId, context.Session, operation.OperationId, "review-batch-" + command.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var batch = new DurableSessionBatch(batchId, scope.ExecutionId, publication, context.CurrentStateId, context.StateVersion, entry, [admission], original.AdmissionWatermark,
            context.AdmissionWatermark, binding.Clock.GetUtcNow(), SessionBatchCloseReason.Immediate);
        var prepared = await PrepareExecutionCoreAsync(batch, context.CurrentStateId, context.StateVersion, command.InstallationId, context.Session, token, operation, context).ConfigureAwait(false);
        var result = new PreparedSessionReview(prepared.Start, context, operation.Purpose, command.RequestRef, operation.OperationId);
        await Put(scope, "review-batch-control", batch, token).ConfigureAwait(false);
        await Put(scope, "prepared-review", result, token).ConfigureAwait(false);
        return result;
    }

    public async Task<CommittedSessionReview> CommitReviewAsync(ReviewCommitCommand command, CancellationToken token)
    {
        var scope = command.Prepared.Start.Scope;
        if (command.Execution.Scope != scope || command.Execution.SnapshotRef.Scope != scope || command.Current.Session != scope.Session) throw new InvalidDataException("Review completion scope mismatch.");
        using var lease = await store.OperationLocks.AcquireAsync("review-commit-" + scope.ExecutionId, token).ConfigureAwait(false);
        var existing = await store.FindAsync(scope, "committed-review", token).ConfigureAwait(false);
        if (existing is not null) return await Read<CommittedSessionReview>(existing, token).ConfigureAwait(false);
        var operation = await Read<SessionReviewOperationV1>(command.Prepared.RequestRef, token).ConfigureAwait(false);
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(command.Execution.SnapshotRef, token).ConfigureAwait(false), scope);
        var current = ReviewContextMatches(command.Prepared.Snapshot, command.Current);
        var status = !current ? ParticipationReviewStatus.Stale : command.Execution.Status == ExecutionStatus.Cancelled ? ParticipationReviewStatus.Cancelled
            : command.Execution.Status == ExecutionStatus.Completed ? ParticipationReviewStatus.Completed : ParticipationReviewStatus.Failed;
        var outputs = snapshot.Outputs.ToDictionary(n => n.Key, n => (IReadOnlyDictionary<string, JsonElement>)n.Value.ToDictionary(p => p.Key, p => p.Value.Value));
        ArtifactRef resultRef; CommittedSessionOutcome? outcome = null;
        if (operation.Purpose == SessionExecutionPurpose.ParticipationReview)
        {
            var project = await Read<SessionFlowProject>(command.Prepared.Start.ProjectRef, token).ConfigureAwait(false);
            var compiled = SessionFlowProjectCompiler.Compile(project);
            var cut = ParticipationReviewCut.Create(compiled.States[snapshot.StateId].Entries[snapshot.EntryKind], operation.Definition!);
            var last = snapshot.Steps.LastOrDefault(); ParticipationReviewDecision? decision = null;
            var assessment = snapshot.InitialInterpretations.Values.SingleOrDefault();
            var analysisOnly = operation.Reassess && assessment is not null && (!assessment.Confident || assessment.Opportunity != ReassessmentOpportunity.Relevant);
            if (status == ParticipationReviewStatus.Completed && last?.Exit is { } exit && cut.Decisions.TryGetValue(new(last.NodeId, exit), out var selected)
                && snapshot.TargetStateId is null && snapshot.ConfirmedSend is null) decision = selected;
            else if (status == ParticipationReviewStatus.Completed && !analysisOnly) status = ParticipationReviewStatus.Failed;
            var review = new SessionParticipationReviewResult(status, status == ParticipationReviewStatus.Completed ? "participation-review-only" : status == ParticipationReviewStatus.Stale ? "review-stale" : "review-not-completed",
                operation.OriginalMessageId, command.Prepared.Snapshot, decision, snapshot.Steps.ToArray(), outputs) { Assessment = assessment };
            var responses = snapshot.PromptResponses.Where(p => operation.Definition!.PromptNodeIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            resultRef = await Put(scope, "review-result", new DurableParticipationReviewV1(scope, operation.Definition!, review, responses), token).ConfigureAwait(false);
        }
        else if (operation.Purpose == SessionExecutionPurpose.ResponsePreview)
        {
            if (snapshot.ConfirmedSend is not null) status = ParticipationReviewStatus.Failed;
            var trace = new List<SessionTraceEvent>();
            foreach (var reference in await store.ListAsync(scope, "traces-", token).ConfigureAwait(false))
                foreach (var node in await Read<List<TypedNodeExecutionTrace>>(reference, token).ConfigureAwait(false))
                    trace.Add(new("node-" + node.Status, node.Timestamp, scope.ExecutionId, snapshot.BatchId, scope.Session, command.Prepared.Snapshot.ProjectId,
                        command.Prepared.Snapshot.ProjectVersion, scope.ProjectRevision, snapshot.StateId, snapshot.StateVersion, node.NodeId, node.Exit, null, SessionBatchCloseReason.Immediate, node.Duration.TotalMilliseconds, node.Error)
                    { MemoryMetadata = node.Metadata });
            resultRef = await Put(scope, "preview-result", new SessionResponsePreviewResult(status, status == ParticipationReviewStatus.Completed ? "shadow-response-preview-only" : "preview-not-completed",
                operation.OriginalMessageId, command.Prepared.Snapshot, snapshot.Steps.ToArray(), outputs, trace.OrderBy(t => t.Timestamp).ToArray()), token).ConfigureAwait(false);
        }
        else
        {
            var batchRef = await store.FindAsync(scope, "review-batch-control", token).ConfigureAwait(false) ?? throw new InvalidDataException("Missing review batch control.");
            var batch = await Read<DurableSessionBatch>(batchRef, token).ConfigureAwait(false);
            var preparedRef = await store.FindAsync(scope, "prepared", token).ConfigureAwait(false) ?? throw new InvalidDataException("Missing review preparation.");
            var prepared = await Read<PreparedSessionExecution>(preparedRef, token).ConfigureAwait(false);
            var execution = current ? command.Execution : command.Execution with { Status = ExecutionStatus.Failed, Diagnostic = "review-context-stale" };
            outcome = await CommitOutcomeAsync(new(batch, execution, prepared.StartedAt, command.CompletedAt, command.Current.CurrentStateId, command.Current.StateVersion, command.Current.AdmissionWatermark), token).ConfigureAwait(false);
            var full = await Read<SessionFlowOutcome>(outcome.OutcomeRef, token).ConfigureAwait(false);
            resultRef = await Put(scope, "continuation-result", new SessionReviewedResponseResult(full.Success ? ParticipationReviewStatus.Completed : ParticipationReviewStatus.Failed, full.Error ?? "reviewed-response-completed", full), token).ConfigureAwait(false);
        }
        var committed = new CommittedSessionReview(resultRef, outcome?.TransitionTargetStateId, outcome?.TransitionApplied == true,
            outcome?.AdmissionWatermark ?? command.Current.AdmissionWatermark, outcome?.HistoryCount ?? command.Current.HistoryCount);
        await Put(scope, "committed-review", committed, token).ConfigureAwait(false);
        return committed;
    }
    public async Task<SessionParticipationReviewResult> ReadParticipationReviewAsync(ArtifactRef reference, CancellationToken token)
    {
        var review = await Read<DurableParticipationReviewV1>(reference, token).ConfigureAwait(false);
        if (review.Scope != reference.Scope) throw new InvalidDataException("Review artifact scope mismatch.");
        return review.Result with { ReviewRef = reference, CompletedPromptResponses = review.CompletedPromptResponses };
    }
    public Task<SessionResponsePreviewResult> ReadResponsePreviewAsync(ArtifactRef reference, CancellationToken token) => Read<SessionResponsePreviewResult>(reference, token);
    public Task<SessionReviewedResponseResult> ReadReviewedResponseAsync(ArtifactRef reference, CancellationToken token) => Read<SessionReviewedResponseResult>(reference, token);
}
