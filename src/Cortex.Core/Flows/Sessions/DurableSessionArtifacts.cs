using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows.Durable;

namespace Cortex.Core.Flows.Sessions;

/// <summary>Private artifact materialization. Only refs and control metadata cross the workflow boundary.</summary>
public sealed class DurableSessionArtifacts(FileExecutionArtifactStore store, ISessionLedger ledger, IExecutionHostBindings bindings) : ISessionArtifactServices
{
    private sealed record HistoryRecord(SessionHistoryEntry Entry, string? ConfirmedSendOf);
    private static ArtifactScope LedgerScope(string installation, SessionKey session) => new(installation, session, "session-ledger", "session-ledger-v1");

    public async Task<ProjectPublicationDescriptor> ReadPublicationAsync(ArtifactRef reference, CancellationToken token)
    {
        var project = await Read<SessionFlowProject>(reference, token).ConfigureAwait(false);
        if (project.ProjectRevision != reference.Scope.ProjectRevision) throw new InvalidDataException("Publication revision mismatch.");
        var compiled = SessionFlowProjectCompiler.Compile(project);
        var entries = compiled.States.Values.SelectMany(s => s.Entries.Select(e =>
        {
            var accumulator = e.Value.Nodes.Values.SingleOrDefault(n => n.Source.Kind == "accumulate");
            var settings = accumulator?.Source.Config.GetProperty("settings");
            TimeSpan? Duration(string name) => settings is { } value && value.GetProperty(name).ValueKind != JsonValueKind.Null ? TimeSpan.FromSeconds(value.GetProperty(name).GetDouble()) : null;
            int? max = settings is { } set && set.GetProperty("maxMessages").ValueKind != JsonValueKind.Null ? set.GetProperty("maxMessages").GetInt32() : null;
            return new SessionStatePolicy(s.Id, new(e.Key, e.Value.EntryId, accumulator?.Source.Id, Duration("quietSeconds"), max, Duration("maxWaitSeconds"), e.Value.Nodes.Values.Any(n => n.Source.Kind == "send")));
        })).ToArray();
        return new(compiled.Snapshot(), reference, entries);
    }

    public async Task<CommittedSessionAdmission?> FindAdmissionAsync(string installation, SessionKey session, AdmissionDescriptor input, CancellationToken token)
    {
        var receipt = await ledger.FindAdmissionAsync(installation, session, input.InputKey, token).ConfigureAwait(false);
        if (receipt is null)
        {
            var baselineRef = await store.FindAsync(LedgerScope(installation, session), "import-baseline", token).ConfigureAwait(false);
            if (baselineRef is null) return null;
            var baseline = await Read<SessionImportRequest>(baselineRef, token).ConfigureAwait(false);
            var original = baseline.SeenProviderIds.SingleOrDefault(id => DurableIdentity.AdmissionInputKey(installation, session, id) == input.InputKey);
            if (original is null) return null;
            var importedRef = await store.FindAsync(LedgerScope(installation, session), "imported-state", token).ConfigureAwait(false) ?? throw new InvalidDataException("Import not finished.");
            var imported = await Read<DurableSessionState>(importedRef, token).ConfigureAwait(false);
            var watermark = baseline.History.SingleOrDefault(h => h.ProviderMessageId == original)?.AdmissionWatermark ?? baseline.Snapshot.AdmissionWatermark;
            return new(input, new(imported, new(SessionAdmissionStatus.Duplicate, watermark, null), null),
                new(SessionAdmissionStatus.Duplicate, watermark, "import", "import", input.InputKey, input.Digest, input.InputRef));
        }
        if (receipt.InputRef != input.InputRef || receipt.Digest != input.Digest) throw new InvalidDataException("Conflicting admission input.");
        var plan = await store.FindAsync(LedgerScope(installation, session), "plan-" + input.InputKey, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing committed admission plan.");
        var command = await Read<AdmissionCommitCommand>(plan, token).ConfigureAwait(false);
        if (command.Input != input) throw new InvalidDataException("Admission metadata mismatch.");
        await CommitHistory(input.InputRef, receipt.Watermark, null, token).ConfigureAwait(false);
        return new(input, command.Decision, receipt);
    }

    public async Task<DurableSessionState> ImportIdleAsync(ArtifactRef importRef, ProjectPublicationDescriptor publication, CancellationToken token)
    {
        var request = await Read<SessionImportRequest>(importRef, token).ConfigureAwait(false);
        var snapshot = request.Snapshot;
        if (request.InstallationId != importRef.Scope.InstallationId || snapshot.Session != importRef.Scope.Session || snapshot.ProjectRevision != importRef.Scope.ProjectRevision
            || string.IsNullOrWhiteSpace(request.CutoverId) || string.IsNullOrWhiteSpace(request.SourceEpoch) || request.SourceCursor < 0
            || request.JournalFingerprint.Length != 64 || request.JournalFingerprint.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid idle import identity.");
        var project = await Read<SessionFlowProject>(publication.ProjectRef, token).ConfigureAwait(false);
        await using (var validator = new SessionFlowRuntime(TimeProvider.System, new ImportCompletion()))
        {
            validator.UpdateProject(project);
            validator.RestoreContext(snapshot.Session, new(snapshot, request.History, request.SeenProviderIds), request.History.FirstOrDefault()?.EntryKind ?? SessionEntryKind.Dm);
        }
        using var lease = await store.OperationLocks.AcquireAsync("admit-" + DurableIdentity.SessionWorkflowId(request.InstallationId, snapshot.Session), token).ConfigureAwait(false);
        if (ledger is not DurableSessionLedger fileLedger) throw new NotSupportedException("Idle import needs a durable baseline ledger.");
        var imported = DurableSessionState.Create(request.InstallationId, snapshot.Session, publication, 256, snapshot.EnteredAt) with
        { CurrentStateId = snapshot.CurrentStateId, StateVersion = snapshot.StateVersion, AdmissionWatermark = snapshot.AdmissionWatermark,
            HistoryCount = snapshot.HistoryCount, EntryKind = request.History.FirstOrDefault()?.EntryKind, ImportedManifest = importRef };
        // Hydration never invokes ReceiveAsync or starts a child for retained messages.
        await fileLedger.SeedImportAsync(request, token).ConfigureAwait(false);
        foreach (var history in request.History)
        {
            var reference = await Put(importRef.Scope, "import-history-" + DurableIdentity.AdmissionInputKey(request.InstallationId, snapshot.Session, history.ProviderMessageId), new HistoryRecord(history, null), token).ConfigureAwait(false);
            await ledger.RecordOnceAsync(new(request.InstallationId, snapshot.Session, "history", DurableIdentity.AdmissionInputKey(request.InstallationId, snapshot.Session, history.ProviderMessageId), reference), token).ConfigureAwait(false);
        }
        await Put(LedgerScope(request.InstallationId, snapshot.Session), "imported-state", imported, token).ConfigureAwait(false);
        return imported;
    }
    private sealed class ImportCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Import cannot execute a model.");
    }

    public async Task<CommittedSessionAdmission> CommitAdmissionAsync(AdmissionCommitCommand command, CancellationToken token)
    {
        var state = command.Before;
        var input = command.Input;
        using var lease = await store.OperationLocks.AcquireAsync("admit-" + DurableIdentity.SessionWorkflowId(state.InstallationId, state.Session), token).ConfigureAwait(false);
        var previous = await FindAdmissionAsync(state.InstallationId, state.Session, input, token).ConfigureAwait(false);
        if (previous is not null) return previous;
        var envelope = await Read<SessionMessageEnvelope>(input.InputRef, token).ConfigureAwait(false);
        ValidateEnvelope(state.InstallationId, state.Session, input, envelope);
        var batch = command.Decision.Batch ?? throw new InvalidDataException("No accepted admission batch.");
        if (command.Decision.Admission.Status != SessionAdmissionStatus.Accepted || command.Decision.State.AdmissionWatermark != state.AdmissionWatermark + 1)
            throw new InvalidDataException("Invalid admission decision.");
        // Commit the original decision before the atomic receipt so a lost response can restore the same IDs.
        await Put(LedgerScope(state.InstallationId, state.Session), "plan-" + input.InputKey, command, token).ConfigureAwait(false);
        var receipt = await ledger.AdmitAsync(new(state.InstallationId, state.Session, input.InputKey, input.InputRef, input.Digest,
            batch.BatchId, batch.ExecutionId, command.Decision.State.AdmissionWatermark), token).ConfigureAwait(false);
        await CommitHistory(input.InputRef, receipt.Watermark, null, token).ConfigureAwait(false);
        return new(input, command.Decision, receipt);
    }

    private static void ValidateEnvelope(string installation, SessionKey session, AdmissionDescriptor input, SessionMessageEnvelope envelope)
    {
        if (input.InputRef.Scope.InstallationId != installation || input.InputRef.Scope.Session != session || input.Digest != input.InputRef.Sha256
            || envelope.ChannelId != session.ChannelId || envelope.AccountId != session.AccountId || envelope.NativeConversationId != session.NativeConversationId
            || input.EntryKind != envelope.EntryKind || input.Direction != envelope.Direction || input.Timestamp != envelope.Timestamp
            || input.InputKey != DurableIdentity.AdmissionInputKey(installation, session, envelope.ProviderMessageId)
            || !Enum.IsDefined(envelope.EntryKind) || !Enum.IsDefined(envelope.Direction)) throw new InvalidDataException("Private admission envelope mismatch.");
        FlowDomain.Check(envelope.Message, FlowDomain.Message);
        if (envelope.Message.GetProperty("canal").GetProperty("nativeId").GetString() != session.NativeConversationId) throw new InvalidDataException("Private message conversation mismatch.");
    }

    private async Task CommitHistory(ArtifactRef inputRef, long watermark, string? executionId, CancellationToken token)
    {
        var envelope = await Read<SessionMessageEnvelope>(inputRef, token).ConfigureAwait(false);
        var entry = new SessionHistoryEntry(watermark, envelope.ProviderMessageId, envelope.Timestamp, envelope.Direction, envelope.EntryKind,
            envelope.ChannelId, envelope.AccountId, envelope.NativeConversationId, envelope.Message);
        var reference = await Put(inputRef.Scope, "history", new HistoryRecord(entry, executionId), token).ConfigureAwait(false);
        await ledger.RecordOnceAsync(new(inputRef.Scope.InstallationId, inputRef.Scope.Session, "history",
            DurableIdentity.AdmissionInputKey(inputRef.Scope.InstallationId, inputRef.Scope.Session, envelope.ProviderMessageId), reference), token).ConfigureAwait(false);
    }

    public async Task<PreparedSessionExecution> PrepareExecutionAsync(DurableSessionBatch batch, string currentState, long currentVersion,
        string installation, SessionKey session, CancellationToken token)
    {
        var scope = new ArtifactScope(installation, session, batch.ExecutionId, batch.Publication.Snapshot.ProjectRevision);
        var existing = await store.FindAsync(scope, "prepared", token).ConfigureAwait(false);
        if (existing is not null) return await Read<PreparedSessionExecution>(existing, token).ConfigureAwait(false);
        var project = await Read<SessionFlowProject>(batch.Publication.ProjectRef, token).ConfigureAwait(false);
        var compiled = SessionFlowProjectCompiler.Compile(project);
        var selectedState = batch.StateId; var selectedVersion = batch.StateVersion; string? diagnostic = null;
        if (batch.Entry.HasSend && (currentState != batch.StateId || currentVersion != batch.StateVersion))
        {
            if (!compiled.States.TryGetValue(currentState, out var confirmed) || !confirmed.Entries.TryGetValue(batch.Entry.Kind, out var repinned)) diagnostic = "stale-state-entry-unavailable";
            else if (repinned.Nodes.Values.Any(n => n.Source.Kind == "accumulate") != (batch.Entry.AccumulatorNodeId is not null)) diagnostic = "stale-state-batch-shape-mismatch";
            else { selectedState = currentState; selectedVersion = currentVersion; }
        }
        var flow = compiled.States[selectedState].Entries[batch.Entry.Kind];
        var host = await bindings.ResolveAsync(scope, token).ConfigureAwait(false);
        if (host.Scope != scope || !JsonElement.DeepEquals(host.Project, project.Document)) throw new InvalidDataException("Captured project binding mismatch.");
        host.ValidateCurrentContext?.Invoke();
        var expose = host.OutboundHost?.Begin(project.Document, scope.ExecutionId, session, batch.Entry.Kind,
            batch.Inputs.Select(i => i.InputKey), []).SupportsReplySelection == true;
        var messages = new List<SessionMessageEnvelope>();
        foreach (var input in batch.Inputs)
        {
            var envelope = await Read<SessionMessageEnvelope>(input.InputRef, token).ConfigureAwait(false);
            ValidateEnvelope(installation, session, input, envelope);
            messages.Add(envelope);
        }
        var history = await History(installation, session, token).ConfigureAwait(false);
        history = history.Where(h => h.Entry.AdmissionWatermark <= batch.HistoryWatermark || h.ConfirmedSendOf is not null).ToList();
        var outputs = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal);
        var accumulator = flow.Nodes.Values.SingleOrDefault(n => n.Source.Kind == "accumulate");
        if (accumulator is not null) outputs[accumulator.Source.Id] = new Dictionary<string, JsonElement> { ["messages"] = JsonSerializer.SerializeToElement(messages.Select(m => Expose(m, expose))) };
        foreach (var node in flow.Nodes.Values.Where(n => n.Source.Kind == "history"))
        {
            var settings = node.Source.Config.GetProperty("settings");
            var maxMessages = settings.GetProperty("maxMessages").GetInt32(); var maxCharacters = settings.GetProperty("maxCharacters").GetInt32();
            var through = settings.TryGetProperty("mode", out var mode) && mode.GetString() == "through-current-batch";
            var boundary = through ? batch.HistoryWatermark : batch.FirstWatermark - 1;
            DateTimeOffset? oldest = settings.TryGetProperty("maxAgeMinutes", out var age) && age.ValueKind != JsonValueKind.Null ? batch.ClosedAt - TimeSpan.FromMinutes(age.GetDouble()) : null;
            var eligible = history.Where(h => h.Entry.AdmissionWatermark <= boundary || h.ConfirmedSendOf is not null)
                .Where(h => settings.GetProperty("includeCharacterMessages").GetBoolean() || h.Entry.Direction != SessionMessageDirection.Outbound)
                .Where(h => oldest is null || h.Entry.Timestamp >= oldest).ToArray();
            var selected = new List<JsonElement>(); var characters = 0;
            for (var n = eligible.Length - 1; n >= 0 && selected.Count < maxMessages; n--)
            {
                var item = eligible[n].Entry; var length = item.Message.GetProperty("texto").GetString()!.Length;
                if (characters + length > maxCharacters) break;
                characters += length;
                selected.Add(Expose(new(item.ProviderMessageId, item.ChannelId, item.AccountId, item.NativeConversationId, item.Timestamp, item.Direction, item.EntryKind, item.Message), expose));
            }
            selected.Reverse(); outputs[node.Source.Id] = new Dictionary<string, JsonElement> { ["messages"] = JsonSerializer.SerializeToElement(selected) };
        }
        var projectRef = await Put(scope, "project", project, token).ConfigureAwait(false);
        var payload = new ExecutionBatchV1(flow.EntryId, Expose(messages[0], expose), outputs, messages.Select(m => m.ProviderMessageId).ToArray(),
            history.Select(h => h.Entry.ProviderMessageId).Concat(messages.Select(m => m.ProviderMessageId)).Distinct(StringComparer.Ordinal).ToArray(), false);
        var batchRef = await Put(scope, "batch", payload, token).ConfigureAwait(false);
        var prepared = new PreparedSessionExecution(new(scope, batch.BatchId, projectRef, batchRef, selectedState, selectedVersion, batch.Entry.Kind), host.Clock.GetUtcNow(), diagnostic);
        await Put(scope, "prepared", prepared, token).ConfigureAwait(false);
        return prepared;
    }

    public async Task<CommittedSessionOutcome> CommitOutcomeAsync(OutcomeCommitCommand command, CancellationToken token)
    {
        var scope = command.Execution.Scope;
        if (scope.ExecutionId != command.Batch.ExecutionId || scope.ProjectRevision != command.Batch.Publication.Snapshot.ProjectRevision) throw new InvalidDataException("Outcome scope mismatch.");
        using var lease = await store.OperationLocks.AcquireAsync("outcome-" + scope.ExecutionId, token).ConfigureAwait(false);
        var existing = await store.FindAsync(scope, "committed-outcome", token).ConfigureAwait(false);
        if (existing is not null) return await Read<CommittedSessionOutcome>(existing, token).ConfigureAwait(false);
        var snapshot = ExecutionSnapshotCodec.Decode(await store.ReadAsync(command.Execution.SnapshotRef, token).ConfigureAwait(false), scope);
        var prepared = await Read<PreparedSessionExecution>(await store.FindAsync(scope, "prepared", token).ConfigureAwait(false) ?? throw new InvalidDataException("No prepared batch."), token).ConfigureAwait(false);
        var payload = await Read<ExecutionBatchV1>(snapshot.BatchRef, token).ConfigureAwait(false);
        var success = command.Execution.Status == ExecutionStatus.Completed && prepared.Diagnostic is null;
        var target = snapshot.TargetStateId;
        var applied = success && target is not null && command.CurrentStateId == snapshot.StateId && command.CurrentStateVersion == snapshot.StateVersion;
        var error = prepared.Diagnostic ?? command.Execution.Diagnostic;
        if (success && target is not null && !applied) { success = false; error = "stale-state-transition"; }
        var watermark = command.AdmissionWatermark;
        if (snapshot.ConfirmedSend is { } speech)
        {
            var inputKey = DurableIdentity.AdmissionInputKey(scope.InstallationId, scope.Session, speech.ProviderMessageId);
            var existingSpeech = await ledger.FindAdmissionAsync(scope.InstallationId, scope.Session, inputKey, token).ConfigureAwait(false);
            var message = JsonSerializer.SerializeToElement(new { texto = speech.Text, remetente = new { nome = speech.Speaker.Name, stableId = speech.Speaker.StableId },
                canal = payload.Message.GetProperty("canal"), reply = speech.ReplyToMessageId is null ? null : new { id = speech.ReplyToMessageId } });
            var envelope = new SessionMessageEnvelope(speech.ProviderMessageId, scope.Session.ChannelId, scope.Session.AccountId, scope.Session.NativeConversationId,
                command.CompletedAt, SessionMessageDirection.Outbound, snapshot.EntryKind, message);
            var speechRef = await Put(scope with { ExecutionId = "speech-" + scope.ExecutionId }, "speech", envelope, token).ConfigureAwait(false);
            var receipt = existingSpeech ?? await ledger.AdmitAsync(new(scope.InstallationId, scope.Session, inputKey, speechRef, speechRef.Sha256,
                command.Batch.BatchId, scope.ExecutionId, checked(watermark + 1)), token).ConfigureAwait(false);
            watermark = Math.Max(watermark, receipt.Watermark);
            await CommitHistory(speechRef, receipt.Watermark, scope.ExecutionId, token).ConfigureAwait(false);
        }
        var project = command.Batch.Publication.Snapshot;
        var outcome = new SessionFlowOutcome(scope.ExecutionId, command.Batch.BatchId, scope.Session, project.ProjectId, project.ProjectVersion, project.ProjectRevision,
            snapshot.StateId, snapshot.StateVersion, snapshot.EntryKind, command.Batch.CloseReason, payload.ProviderMessageIds, command.StartedAt, command.CompletedAt,
            success, error, target, applied, snapshot.Outputs.ToDictionary(n => n.Key, n => (IReadOnlyDictionary<string, JsonElement>)n.Value.ToDictionary(p => p.Key, p => p.Value.Value)));
        var outcomeRef = await Put(scope, "outcome", outcome, token).ConfigureAwait(false);
        await ledger.RecordOnceAsync(new(scope.InstallationId, scope.Session, "outcomes", scope.ExecutionId, outcomeRef), token).ConfigureAwait(false);
        await CommitTraces(command, snapshot, success, error, token).ConfigureAwait(false);
        if (snapshot.ConfirmedSend is not null)
        {
            var binding = await bindings.ResolveAsync(scope, token).ConfigureAwait(false);
            if (binding.Scope != scope) throw new InvalidDataException("Speech commit host scope mismatch.");
            if (binding.OutboundHost is { } host)
            {
                var projectSource = await Read<SessionFlowProject>(snapshot.ProjectRef, token).ConfigureAwait(false);
                var outbound = host.Begin(projectSource.Document, scope.ExecutionId, scope.Session, snapshot.EntryKind, payload.ProviderMessageIds, payload.ReplyTargetIds, payload.ShadowOnly);
                var receipt = host.RecoverExecutionSend(outbound);
                if (receipt?.JournalRef is { } attempt && host.PendingSpeechCommits(scope.Session).Any(p => p.AttemptRef == attempt)) host.CompleteSpeechCommit(scope.Session, attempt, applied);
            }
        }
        var committed = new CommittedSessionOutcome(outcomeRef, target, applied, watermark, (await History(scope.InstallationId, scope.Session, token).ConfigureAwait(false)).Count);
        await Put(scope, "committed-outcome", committed, token).ConfigureAwait(false);
        return committed;
    }

    private async Task CommitTraces(OutcomeCommitCommand command, ExecutionSnapshotV1 snapshot, bool success, string? error, CancellationToken token)
    {
        var scope = snapshot.Scope; var project = command.Batch.Publication.Snapshot;
        var traces = new List<(string Id, SessionTraceEvent Trace)>();
        SessionTraceEvent Trace(string name, DateTimeOffset time, string? node = null, string? exit = null, double duration = 0) => new(name, time, scope.ExecutionId,
            command.Batch.BatchId, scope.Session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, snapshot.StateId, snapshot.StateVersion,
            node, exit, snapshot.TargetStateId, command.Batch.CloseReason, duration, error);
        traces.Add(("closed", Trace("batch-closed", command.Batch.ClosedAt)));
        traces.Add(("started", Trace("execution-started", command.StartedAt)));
        foreach (var reference in await store.ListAsync(scope, "traces-", token).ConfigureAwait(false))
        {
            var nodes = await Read<List<TypedNodeExecutionTrace>>(reference, token).ConfigureAwait(false);
            for (var n = 0; n < nodes.Count; n++)
            {
                var node = nodes[n];
                traces.Add((reference.Key + "-" + n, Trace(node.Status.StartsWith("MemoryQuery", StringComparison.Ordinal) ? node.Status : "node-" + node.Status, node.Timestamp, node.NodeId, node.Exit, node.Duration.TotalMilliseconds) with { MemoryMetadata = node.Metadata }));
            }
        }
        traces = traces.OrderBy(t => t.Trace.Timestamp).ToList();
        traces.Add(("completed", Trace(success ? "execution-completed" : "execution-failed", command.CompletedAt)));
        foreach (var item in traces)
        {
            var reference = await Put(scope, "trace-" + item.Id, item.Trace, token).ConfigureAwait(false);
            await ledger.RecordOnceAsync(new(scope.InstallationId, scope.Session, "traces", scope.ExecutionId + "-" + item.Id, reference), token).ConfigureAwait(false);
        }
    }

    private static JsonElement Expose(SessionMessageEnvelope message, bool expose)
    {
        if (!expose) return message.Message;
        var copy = JsonNode.Parse(message.Message.GetRawText())!.AsObject(); copy["id"] = message.ProviderMessageId;
        return JsonSerializer.SerializeToElement(copy);
    }
    private async Task<List<HistoryRecord>> History(string installation, SessionKey session, CancellationToken token)
    {
        var result = new List<HistoryRecord>(); string? cursor = null;
        do
        {
            var page = await ledger.ReadPageAsync(installation, session, "history", new(cursor, 1000), token).ConfigureAwait(false);
            foreach (var reference in page.Items) result.Add(await Read<HistoryRecord>(reference, token).ConfigureAwait(false));
            cursor = page.NextCursor;
        } while (cursor is not null);
        result.Sort((a, b) => a.Entry.AdmissionWatermark.CompareTo(b.Entry.AdmissionWatermark));
        var chars = result.Sum(h => h.Entry.Message.GetProperty("texto").GetString()!.Length);
        while (result.Count > 10000 || chars > 1000000) { chars -= result[0].Entry.Message.GetProperty("texto").GetString()!.Length; result.RemoveAt(0); }
        return result;
    }
    public async Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(string installation, SessionKey session, PageRequest page, CancellationToken token)
    {
        var records = await ledger.ReadPageAsync(installation, session, "history", page, token).ConfigureAwait(false);
        var result = new List<SessionHistoryEntry>(); foreach (var reference in records.Items) result.Add((await Read<HistoryRecord>(reference, token).ConfigureAwait(false)).Entry);
        return new(result, records.NextCursor);
    }
    public Task<ArtifactPage<SessionFlowOutcome>> ReadOutcomesAsync(string installation, SessionKey session, PageRequest page, CancellationToken token) => ReadPage<SessionFlowOutcome>(installation, session, "outcomes", page, token);
    public Task<ArtifactPage<SessionTraceEvent>> ReadTraceAsync(string installation, SessionKey session, PageRequest page, CancellationToken token) => ReadPage<SessionTraceEvent>(installation, session, "traces", page, token);
    private async Task<ArtifactPage<T>> ReadPage<T>(string installation, SessionKey session, string collection, PageRequest page, CancellationToken token)
    {
        var records = await ledger.ReadPageAsync(installation, session, collection, page, token).ConfigureAwait(false);
        var result = new List<T>(); foreach (var reference in records.Items) result.Add(await Read<T>(reference, token).ConfigureAwait(false));
        return new(result, records.NextCursor);
    }
    private async Task<T> Read<T>(ArtifactRef reference, CancellationToken token) => (await store.ReadAsync(reference, token).ConfigureAwait(false)).Deserialize<T>() ?? throw new InvalidDataException("Empty private artifact.");
    private async Task<ArtifactRef> Put<T>(ArtifactScope scope, string key, T payload, CancellationToken token) => await store.PutOnceAsync(scope, key, JsonSerializer.SerializeToElement(payload), token).ConfigureAwait(false);
}
