using System.Collections.Concurrent;
using System.Text.Json;
using Filomena.Core.Boundaries;

namespace Filomena.Core.Flows.Sessions;

public sealed partial class SessionFlowRuntime
{
    private const int RetainedMessageLimit = 10000;
    private const int RetainedCharacterLimit = 1000000;

    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<SessionKey, RuntimeSession> sessions = new();
    private Task? disposalTask;
    private bool disposed;

    private ValueTask<SessionAdmission> ReceiveCore(
        SessionKey session,
        SessionMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rejection = ValidateEnvelope(session, message);
        if (rejection is not null) return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, rejection));

        try { FlowDomain.Check(message.Message, FlowDomain.Message); }
        catch (InvalidDataException) { return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "message-domain-mismatch")); }
        if (!StringComparer.Ordinal.Equals(
                message.Message.GetProperty("canal").GetProperty("nativeId").GetString(),
                message.NativeConversationId))
            return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "message-channel-identity-mismatch"));

        CompiledSessionFlowProject capturedProject;
        lock (administrationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (project is null) return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "project-not-published"));
            capturedProject = project;
        }

        var state = sessions.GetOrAdd(session, key =>
            new RuntimeSession(key, capturedProject.InitialStateId, message.EntryKind, timeProvider.GetUtcNow(), capturedProject));
        lock (state.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
            if (state.EntryKind != message.EntryKind)
            {
                return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "conversation-kind-mismatch"));
            }

            if (state.ProviderMessageIds.Contains(message.ProviderMessageId))
                return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Duplicate, null, null));

            var openBuffer = message.Direction == SessionMessageDirection.Inbound
                ? FindOpenBuffer(state, capturedProject, message.EntryKind)
                : default;
            TypedFlow? flow = null;
            if (openBuffer.Value is null)
            {
                if (!capturedProject.States.TryGetValue(state.CurrentStateId, out var selectedState))
                    return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "current-state-not-in-project"));
                if (message.Direction == SessionMessageDirection.Inbound && !selectedState.Entries.TryGetValue(message.EntryKind, out flow))
                    return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Rejected, null, "entry-not-available"));
            }

            var watermark = ++state.AdmissionWatermark;
            var admitted = new AdmittedMessage(watermark, CloneEnvelope(message));
            state.ProviderMessageIds.Add(message.ProviderMessageId);
            state.History.Add(admitted);
            state.Project = capturedProject;
            TrimRetainedHistory(state);

            if (message.Direction == SessionMessageDirection.Outbound)
                return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Accepted, watermark, null));

            if (openBuffer.Value is not null)
            {
                AppendToAccumulatorLocked(state, openBuffer.Key, openBuffer.Value, admitted);
                return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Accepted, watermark, null));
            }

            var accumulator = flow!.Nodes.Values.SingleOrDefault(node => node.Source.Kind == "accumulate");
            if (accumulator is null)
            {
                var batch = CreateBatch(state, capturedProject, flow!, message.EntryKind, null, [admitted],
                    state.CurrentStateId, state.StateVersion, SessionBatchCloseReason.Immediate);
                QueueBatchLocked(state, batch);
            }
            else
            {
                AdmitToAccumulatorLocked(state, capturedProject, flow!, message.EntryKind, accumulator, admitted);
            }
            return ValueTask.FromResult(new SessionAdmission(SessionAdmissionStatus.Accepted, watermark, null));
        }
    }

    private SessionSnapshot? ReadSessionCore(SessionKey session)
    {
        if (!sessions.TryGetValue(session, out var state)) return null;
        lock (state.Gate)
            return new(state.Key, state.Project.ProjectId, state.Project.ProjectVersion, state.Project.ProjectRevision,
                state.CurrentStateId, state.StateVersion, state.EnteredAt, state.AdmissionWatermark,
                state.History.Count, state.Buffers.Count, state.PendingExecutions);
    }

    private IReadOnlyList<SessionFlowOutcome> ReadOutcomesCore(SessionKey session)
    {
        if (!sessions.TryGetValue(session, out var state)) return [];
        lock (state.Gate) return state.Outcomes.Select(CloneOutcome).ToArray();
    }

    private IReadOnlyList<SessionTraceEvent> ReadTraceCore(SessionKey session)
    {
        if (!sessions.TryGetValue(session, out var state)) return [];
        lock (state.Gate) return state.Trace.ToArray();
    }

    private IReadOnlyList<SessionHistoryEntry> ReadHistoryCore(SessionKey session)
    {
        if (!sessions.TryGetValue(session, out var state)) return [];
        lock (state.Gate)
            return state.History.Select(item => new SessionHistoryEntry(
                item.Watermark,
                item.Envelope.ProviderMessageId,
                item.Envelope.Timestamp,
                item.Envelope.Direction,
                item.Envelope.EntryKind,
                item.Envelope.ChannelId,
                item.Envelope.AccountId,
                item.Envelope.NativeConversationId,
                item.Envelope.Message.Clone())).ToArray();
    }

    private Task WaitForIdleCoreAsync(SessionKey session, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(session, out var state)) return Task.CompletedTask.WaitAsync(cancellationToken);
        Task tail;
        lock (state.Gate) tail = state.Tail;
        return tail.WaitAsync(cancellationToken);
    }

    private ValueTask DisposeCoreAsync()
    {
        TaskCompletionSource? completion = null;
        Task task;
        lock (administrationGate)
        {
            if (disposalTask is not null) return new(disposalTask);
            disposed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            disposalTask = completion.Task;
            task = disposalTask;
        }

        _ = FinishDisposalAsync(completion);
        return new(task);
    }

    private async Task FinishDisposalAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }

        var tails = new List<Task>();
        foreach (var state in sessions.Values)
        {
            lock (state.Gate)
            {
                foreach (var buffer in state.Buffers.Values)
                {
                    try { buffer.DisposeTimers(); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                state.Buffers.Clear();
                tails.Add(state.Tail);
            }
        }

        try { await Task.WhenAll(tails).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { failures.Add(exception); }
        finally { lifetime.Dispose(); }

        if (failures.Count == 0) completion.TrySetResult();
        else completion.TrySetException(failures.Count == 1 ? failures[0] : new AggregateException(failures));
    }

    private void AdmitToAccumulatorLocked(
        RuntimeSession state,
        CompiledSessionFlowProject capturedProject,
        TypedFlow flow,
        SessionEntryKind entryKind,
        TypedNode accumulator,
        AdmittedMessage admitted)
    {
        var key = new BufferKey(capturedProject.ProjectId, capturedProject.ProjectVersion, capturedProject.ProjectRevision,
            state.CurrentStateId, state.StateVersion, entryKind, accumulator.Source.Id);
        if (!state.Buffers.TryGetValue(key, out var buffer))
        {
            buffer = new OpenBuffer(capturedProject, flow, entryKind, accumulator.Source.Id,
                ParseAccumulator(accumulator.Source.Config), state.CurrentStateId, state.StateVersion);
            state.Buffers.Add(key, buffer);
            if (buffer.Options.MaxWait is { } maxWait)
            {
                buffer.MaxWaitDueAt = timeProvider.GetUtcNow() + maxWait;
                buffer.MaxWaitTimer = CreateCloseTimer(state.Key, key, buffer, SessionBatchCloseReason.MaxWait, maxWait);
            }
        }

        AppendToAccumulatorLocked(state, key, buffer, admitted);
    }

    private void AppendToAccumulatorLocked(
        RuntimeSession state,
        BufferKey key,
        OpenBuffer buffer,
        AdmittedMessage admitted)
    {
        buffer.Messages.Add(admitted);
        if (buffer.Options.QuietPeriod is { } quiet)
        {
            buffer.QuietDueAt = timeProvider.GetUtcNow() + quiet;
            buffer.QuietTimer ??= CreateCloseTimer(state.Key, key, buffer, SessionBatchCloseReason.QuietPeriod, quiet);
            buffer.QuietTimer.Change(quiet, Timeout.InfiniteTimeSpan);
        }
        if (buffer.Options.MaxMessages is { } maxMessages && buffer.Messages.Count >= maxMessages)
            CloseBufferLocked(state, key, buffer, SessionBatchCloseReason.MaxMessages);
    }

    private static KeyValuePair<BufferKey, OpenBuffer> FindOpenBuffer(
        RuntimeSession state,
        CompiledSessionFlowProject project,
        SessionEntryKind entryKind) =>
        state.Buffers.SingleOrDefault(item =>
            item.Key.EntryKind == entryKind &&
            item.Key.ProjectVersion == project.ProjectVersion &&
            StringComparer.Ordinal.Equals(item.Key.ProjectId, project.ProjectId) &&
            StringComparer.Ordinal.Equals(item.Key.ProjectRevision, project.ProjectRevision));

    private ITimer CreateCloseTimer(SessionKey session, BufferKey key, OpenBuffer buffer, SessionBatchCloseReason reason, TimeSpan dueTime) =>
        timeProvider.CreateTimer(
            static callbackState =>
            {
                var callback = (TimerCloseState)callbackState!;
                callback.Runtime.CloseBuffer(callback.Session, callback.Key, callback.Buffer, callback.Reason);
            },
            new TimerCloseState(this, session, key, buffer, reason),
            dueTime,
            Timeout.InfiniteTimeSpan);

    private void CloseBuffer(SessionKey session, BufferKey key, OpenBuffer expected, SessionBatchCloseReason reason)
    {
        if (Volatile.Read(ref disposed) || !sessions.TryGetValue(session, out var state)) return;
        lock (state.Gate)
        {
            if (Volatile.Read(ref disposed) || !state.Buffers.TryGetValue(key, out var current) || !ReferenceEquals(current, expected)) return;
            var dueAt = reason == SessionBatchCloseReason.QuietPeriod ? current.QuietDueAt : current.MaxWaitDueAt;
            var remaining = dueAt - timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                var timer = reason == SessionBatchCloseReason.QuietPeriod ? current.QuietTimer : current.MaxWaitTimer;
                timer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            CloseBufferLocked(state, key, current, reason);
        }
    }

    private void CloseBufferLocked(RuntimeSession state, BufferKey key, OpenBuffer expected, SessionBatchCloseReason reason)
    {
        if (!state.Buffers.TryGetValue(key, out var buffer) || !ReferenceEquals(buffer, expected)) return;
        state.Buffers.Remove(key);
        buffer.DisposeTimers();
        var batch = CreateBatch(state, buffer.Project, buffer.Flow, buffer.EntryKind, buffer.AccumulatorNodeId,
            buffer.Messages.ToArray(), buffer.StateId, buffer.StateVersion, reason);
        QueueBatchLocked(state, batch);
    }

    private FlowBatch CreateBatch(
        RuntimeSession state,
        CompiledSessionFlowProject capturedProject,
        TypedFlow flow,
        SessionEntryKind entryKind,
        string? accumulatorNodeId,
        IReadOnlyList<AdmittedMessage> messages,
        string stateId,
        long stateVersion,
        SessionBatchCloseReason reason) =>
        new(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            state.Key,
            capturedProject,
            flow,
            stateId,
            stateVersion,
            entryKind,
            accumulatorNodeId,
            messages,
            state.History.Where(item => item.Watermark <= messages.Max(message => message.Watermark)).ToArray(),
            messages.Max(message => message.Watermark),
            timeProvider.GetUtcNow(),
            reason);

    private void QueueBatchLocked(RuntimeSession state, FlowBatch batch)
    {
        AddTraceLocked(state, Trace(batch, "batch-closed", closeReason: batch.CloseReason));
        state.PendingExecutions++;
        var predecessor = state.Tail;
        state.Tail = Task.Run(async () =>
        {
            try { await predecessor.ConfigureAwait(false); }
            catch (Exception) { }
            try { await ExecuteBatchAsync(batch).ConfigureAwait(false); }
            catch (Exception exception) { RecordUnexpectedBatchFailure(batch, exception); }
        });
    }

    private void RecordUnexpectedBatchFailure(FlowBatch batch, Exception exception)
    {
        if (!sessions.TryGetValue(batch.Session, out var state)) return;
        lock (state.Gate)
        {
            if (state.Outcomes.Any(item => item.ExecutionId == batch.ExecutionId)) return;
            var now = timeProvider.GetUtcNow();
            state.PendingExecutions--;
            state.Outcomes.Add(new(
                batch.ExecutionId, batch.BatchId, batch.Session,
                batch.Project.ProjectId, batch.Project.ProjectVersion, batch.Project.ProjectRevision,
                batch.StateId, batch.StateVersion, batch.EntryKind, batch.CloseReason,
                batch.Messages.Select(item => item.Envelope.ProviderMessageId).ToArray(),
                now, now, false, "runtime-execution-failed", null, false,
                new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)));
            AddTraceLocked(state, Trace(batch, "execution-failed", errorCode: ErrorCode(exception.GetType().Name), timestamp: now));
        }
    }

    private async Task ExecuteBatchAsync(FlowBatch batch)
    {
        var startedAt = timeProvider.GetUtcNow();
        AddTrace(batch.Session, Trace(batch, "execution-started", timestamp: startedAt));
        TypedRunResult? result = null;
        string? executionError = null;
        try
        {
            var boundaryRegistry = new BoundaryExecutionRegistry(batch.Flow.Nodes.Values.Any(node => node.Source.Kind is "memory" or "interpretBoundaries")
                ? ConversationNamespace(batch.Session) : "", timeProvider);
            result = await TypedFlowRunner.RunSessionAsync(
                batch.Flow,
                batch.Messages[0].Envelope.Message,
                completionFactory(batch.Session),
                RuntimeOutputs(batch),
                timeProvider,
                node => AddTrace(batch.Session, Trace(batch, "node-" + node.Status, node.NodeId, node.Exit,
                    duration: node.Duration.TotalMilliseconds, errorCode: node.Error, timestamp: node.Timestamp) with
                { MemoryMetadata = node.Metadata }),
                (node, persons, query, token) => QueryMemoryAsync(batch, node, persons, query, boundaryRegistry, token),
                batch.Project.States[batch.StateId].InstructionContext,
                instructionOutput is null ? null : (nodeId, outputs, token) =>
                    instructionOutput(new(batch.ExecutionId, batch.BatchId, batch.Session, batch.StateId,
                        batch.StateVersion, batch.Project.ProjectRevision, nodeId, outputs), token),
                nodeStarting is null ? null : (nodeId, outputs, token) =>
                    nodeStarting(new(batch.ExecutionId, batch.BatchId, batch.Session, batch.StateId,
                        batch.StateVersion, batch.Project.ProjectRevision, nodeId, outputs), token),
                boundaryRegistry, lifetime.Token,
                outboundHost?.Begin(batch.Project.Document, batch.ExecutionId, batch.Session, batch.EntryKind,
                    batch.Messages.Select(item => item.Envelope.ProviderMessageId))).ConfigureAwait(false);
            if (!result.Success) executionError = result.Steps.LastOrDefault(step => step.Status == "failed")?.Error ?? "flow-execution-failed";
        }
        catch (OperationCanceledException)
        {
            executionError = "cancelled";
        }
        catch (Exception exception)
        {
            executionError = exception.GetType().Name;
        }

        var completedAt = timeProvider.GetUtcNow();
        var target = result?.TargetStateId;
        var transitionApplied = false;
        var success = result?.Success == true && executionError is null;
        if (!sessions.TryGetValue(batch.Session, out var state)) return;
        lock (state.Gate)
        {
            if (Volatile.Read(ref disposed))
            {
                success = false;
                executionError = "cancelled";
            }
            if (success && target is not null)
            {
                if (state.StateVersion == batch.StateVersion && StringComparer.Ordinal.Equals(state.CurrentStateId, batch.StateId))
                {
                    state.CurrentStateId = target;
                    state.StateVersion++;
                    state.EnteredAt = completedAt;
                    transitionApplied = true;
                    AddTraceLocked(state, Trace(batch, "transition-applied", targetStateId: target, timestamp: completedAt));
                }
                else
                {
                    success = false;
                    executionError = "stale-state-transition";
                    AddTraceLocked(state, Trace(batch, "transition-skipped-stale", targetStateId: target,
                        errorCode: executionError, timestamp: completedAt));
                }
            }
            var outcome = new SessionFlowOutcome(
                batch.ExecutionId, batch.BatchId, batch.Session,
                batch.Project.ProjectId, batch.Project.ProjectVersion, batch.Project.ProjectRevision,
                batch.StateId, batch.StateVersion, batch.EntryKind, batch.CloseReason,
                batch.Messages.Select(item => item.Envelope.ProviderMessageId).ToArray(),
                startedAt, completedAt, success, executionError, target, transitionApplied,
                CloneOutputs(result?.Outputs));
            state.Outcomes.Add(outcome);
            state.PendingExecutions--;
            AddTraceLocked(state, Trace(batch, success ? "execution-completed" : "execution-failed",
                targetStateId: target, errorCode: ErrorCode(executionError), timestamp: completedAt));
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>> RuntimeOutputs(FlowBatch batch)
    {
        var outputs = new Dictionary<string, IReadOnlyDictionary<string, TypedValue>>(StringComparer.Ordinal);
        if (batch.AccumulatorNodeId is not null)
        {
            var messages = JsonSerializer.SerializeToElement(batch.Messages.Select(item => item.Envelope.Message).ToArray());
            outputs[batch.AccumulatorNodeId] = new Dictionary<string, TypedValue>
            {
                ["messages"] = new(new ArrayType(FlowDomain.Message), messages),
            };
        }
        foreach (var history in batch.Flow.Nodes.Values.Where(node => node.Source.Kind == "history"))
        {
            var messages = SelectHistory(batch, history.Source.Config);
            outputs[history.Source.Id] = new Dictionary<string, TypedValue>
            {
                ["messages"] = new(new ArrayType(FlowDomain.Message), JsonSerializer.SerializeToElement(messages)),
            };
        }
        return outputs;
    }

    private static JsonElement[] SelectHistory(FlowBatch batch, JsonElement config)
    {
        var settings = config.GetProperty("settings");
        var maxMessages = settings.GetProperty("maxMessages").GetInt32();
        var maxCharacters = settings.GetProperty("maxCharacters").GetInt32();
        var includeCharacter = settings.GetProperty("includeCharacterMessages").GetBoolean();
        var mode = settings.TryGetProperty("mode", out var modeValue) && modeValue.ValueKind == JsonValueKind.String
            ? modeValue.GetString()
            : "before-current-batch";
        var boundary = mode == "through-current-batch" ? batch.HistoryWatermark : batch.Messages.Min(item => item.Watermark) - 1;
        DateTimeOffset? oldest = null;
        if (settings.TryGetProperty("maxAgeMinutes", out var age) && age.ValueKind != JsonValueKind.Null)
            oldest = batch.ClosedAt - TimeSpan.FromMinutes(age.GetDouble());

        var eligible = batch.History
            .Where(item => item.Watermark <= boundary)
            .Where(item => includeCharacter || item.Envelope.Direction != SessionMessageDirection.Outbound)
            .Where(item => oldest is null || item.Envelope.Timestamp >= oldest)
            .ToArray();
        var selected = new List<JsonElement>();
        var characters = 0;
        for (var index = eligible.Length - 1; index >= 0 && selected.Count < maxMessages; index--)
        {
            var textLength = eligible[index].Envelope.Message.GetProperty("texto").GetString()!.Length;
            if (characters + textLength > maxCharacters) break;
            characters += textLength;
            selected.Add(eligible[index].Envelope.Message.Clone());
        }
        selected.Reverse();
        return selected.ToArray();
    }

    private static AccumulatorOptions ParseAccumulator(JsonElement config)
    {
        var settings = config.GetProperty("settings");
        return new(
            Duration(settings, "quietSeconds"),
            settings.GetProperty("maxMessages").ValueKind == JsonValueKind.Null ? null : settings.GetProperty("maxMessages").GetInt32(),
            Duration(settings, "maxWaitSeconds"));
    }

    private static TimeSpan? Duration(JsonElement settings, string key) =>
        settings.GetProperty(key).ValueKind == JsonValueKind.Null
            ? null
            : TimeSpan.FromSeconds(settings.GetProperty(key).GetDouble());

    private static string? ValidateEnvelope(SessionKey session, SessionMessageEnvelope message)
    {
        if (new[] { session.RuntimeId, session.CharacterId, session.ChannelId, session.AccountId, session.NativeConversationId,
                message.ProviderMessageId, message.ChannelId, message.AccountId, message.NativeConversationId }
            .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))) return "invalid-identity";
        if (!StringComparer.Ordinal.Equals(session.ChannelId, message.ChannelId) ||
            !StringComparer.Ordinal.Equals(session.AccountId, message.AccountId) ||
            !StringComparer.Ordinal.Equals(session.NativeConversationId, message.NativeConversationId)) return "session-identity-mismatch";
        if (!Enum.IsDefined(message.Direction) || !Enum.IsDefined(message.EntryKind)) return "invalid-envelope-enum";
        return null;
    }

    private static SessionMessageEnvelope CloneEnvelope(SessionMessageEnvelope source) =>
        source with { Message = source.Message.Clone() };

    private static void TrimRetainedHistory(RuntimeSession state)
    {
        var characters = state.History.Sum(item => item.Envelope.Message.GetProperty("texto").GetString()!.Length);
        while (state.History.Count > RetainedMessageLimit || characters > RetainedCharacterLimit)
        {
            characters -= state.History[0].Envelope.Message.GetProperty("texto").GetString()!.Length;
            state.History.RemoveAt(0);
        }
    }

    private static SessionFlowOutcome CloneOutcome(SessionFlowOutcome source) => source with
    {
        ProviderMessageIds = source.ProviderMessageIds.ToArray(),
        Outputs = source.Outputs.ToDictionary(
            node => node.Key,
            node => (IReadOnlyDictionary<string, JsonElement>)node.Value.ToDictionary(port => port.Key, port => port.Value.Clone()),
            StringComparer.Ordinal),
    };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> CloneOutputs(
        IReadOnlyDictionary<string, Dictionary<string, TypedValue>>? outputs) =>
        outputs is null
            ? new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
            : outputs.ToDictionary(
                node => node.Key,
                node => (IReadOnlyDictionary<string, JsonElement>)node.Value.ToDictionary(port => port.Key, port => port.Value.Value.Clone(), StringComparer.Ordinal),
                StringComparer.Ordinal);

    private void AddTrace(SessionKey session, SessionTraceEvent item)
    {
        if (!sessions.TryGetValue(session, out var state)) return;
        lock (state.Gate) AddTraceLocked(state, item);
    }

    private static void AddTraceLocked(RuntimeSession state, SessionTraceEvent item) =>
        state.Trace.Add(item with
        {
            CurrentSessionStateId = state.CurrentStateId,
            CurrentSessionStateVersion = state.StateVersion,
        });

    private SessionTraceEvent Trace(
        FlowBatch batch,
        string eventName,
        string? nodeId = null,
        string? exit = null,
        string? targetStateId = null,
        SessionBatchCloseReason? closeReason = null,
        double duration = 0,
        string? errorCode = null,
        DateTimeOffset? timestamp = null) =>
        new(eventName, timestamp ?? timeProvider.GetUtcNow(), batch.ExecutionId, batch.BatchId, batch.Session,
            batch.Project.ProjectId, batch.Project.ProjectVersion, batch.Project.ProjectRevision,
            batch.StateId, batch.StateVersion, nodeId, exit, targetStateId, closeReason, duration, ErrorCode(errorCode));

    private static string? ErrorCode(string? error) => error switch
    {
        null => null,
        "cancelled" or "stale-state-transition" or "flow-execution-failed" => error,
        "HttpRequestException" or "InvalidDataException" or "InvalidOperationException" or "JsonException" or
        "KeyNotFoundException" or "SessionFlowProjectException" => error,
        _ => "flow-execution-failed",
    };

    private sealed class RuntimeSession(
        SessionKey key,
        string currentStateId,
        SessionEntryKind entryKind,
        DateTimeOffset enteredAt,
        CompiledSessionFlowProject project)
    {
        public object Gate { get; } = new();
        public SessionKey Key { get; } = key;
        public string CurrentStateId { get; set; } = currentStateId;
        public SessionEntryKind EntryKind { get; } = entryKind;
        public long StateVersion { get; set; }
        public DateTimeOffset EnteredAt { get; set; } = enteredAt;
        public CompiledSessionFlowProject Project { get; set; } = project;
        public long AdmissionWatermark { get; set; }
        public HashSet<string> ProviderMessageIds { get; } = new(StringComparer.Ordinal);
        public List<AdmittedMessage> History { get; } = [];
        public Dictionary<BufferKey, OpenBuffer> Buffers { get; } = [];
        public List<SessionFlowOutcome> Outcomes { get; } = [];
        public List<SessionTraceEvent> Trace { get; } = [];
        public Task Tail { get; set; } = Task.CompletedTask;
        public int PendingExecutions { get; set; }
    }

    private sealed record AdmittedMessage(long Watermark, SessionMessageEnvelope Envelope);

    private sealed record BufferKey(
        string ProjectId,
        long ProjectVersion,
        string ProjectRevision,
        string StateId,
        long StateVersion,
        SessionEntryKind EntryKind,
        string NodeId);

    private sealed record AccumulatorOptions(TimeSpan? QuietPeriod, int? MaxMessages, TimeSpan? MaxWait);

    private sealed class OpenBuffer(
        CompiledSessionFlowProject project,
        TypedFlow flow,
        SessionEntryKind entryKind,
        string accumulatorNodeId,
        AccumulatorOptions options,
        string stateId,
        long stateVersion)
    {
        public CompiledSessionFlowProject Project { get; } = project;
        public TypedFlow Flow { get; } = flow;
        public SessionEntryKind EntryKind { get; } = entryKind;
        public string AccumulatorNodeId { get; } = accumulatorNodeId;
        public AccumulatorOptions Options { get; } = options;
        public string StateId { get; } = stateId;
        public long StateVersion { get; } = stateVersion;
        public List<AdmittedMessage> Messages { get; } = [];
        public DateTimeOffset QuietDueAt { get; set; } = DateTimeOffset.MaxValue;
        public DateTimeOffset MaxWaitDueAt { get; set; } = DateTimeOffset.MaxValue;
        public ITimer? QuietTimer { get; set; }
        public ITimer? MaxWaitTimer { get; set; }
        public void DisposeTimers()
        {
            QuietTimer?.Dispose();
            MaxWaitTimer?.Dispose();
        }
    }

    private sealed record TimerCloseState(
        SessionFlowRuntime Runtime,
        SessionKey Session,
        BufferKey Key,
        OpenBuffer Buffer,
        SessionBatchCloseReason Reason);

    private sealed record FlowBatch(
        string ExecutionId,
        string BatchId,
        SessionKey Session,
        CompiledSessionFlowProject Project,
        TypedFlow Flow,
        string StateId,
        long StateVersion,
        SessionEntryKind EntryKind,
        string? AccumulatorNodeId,
        IReadOnlyList<AdmittedMessage> Messages,
        IReadOnlyList<AdmittedMessage> History,
        long HistoryWatermark,
        DateTimeOffset ClosedAt,
        SessionBatchCloseReason CloseReason);
}
