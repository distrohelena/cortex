namespace Cortex.Core.Flows.Sessions;

public static class SessionBatchPolicy
{
    public static SessionPolicyDecision Admit(DurableSessionState state, AdmissionDescriptor input, DateTimeOffset now)
    {
        SessionPolicyDecision Reject(string reason) => new(state, new(SessionAdmissionStatus.Rejected, null, reason), null);
        if (input.InputRef.Scope.InstallationId != state.InstallationId || input.InputRef.Scope.Session != state.Session || input.Digest != input.InputRef.Sha256)
            return Reject("admission-scope-mismatch");
        if (!Enum.IsDefined(input.EntryKind) || !Enum.IsDefined(input.Direction)) return Reject("invalid-envelope-enum");
        if (state.EntryKind is { } kind && kind != input.EntryKind) return Reject("conversation-kind-mismatch");
        var open = input.Direction == SessionMessageDirection.Inbound ? state.Buffers.SingleOrDefault(b =>
            b.Batch.Publication.ProjectRef == state.Publication.ProjectRef && b.Batch.Entry.Kind == input.EntryKind) : null;
        var entry = open?.Batch.Entry ?? state.Publication.Entries.SingleOrDefault(e => e.StateId == state.CurrentStateId && e.Entry.Kind == input.EntryKind)?.Entry;
        if (!state.Publication.Snapshot.StateIds.Contains(state.CurrentStateId) && open is null) return Reject("current-state-not-in-project");
        if (entry is null && input.Direction == SessionMessageDirection.Inbound) return Reject("entry-not-available");
        if (input.Direction == SessionMessageDirection.Inbound && open is null && state.Queue.Count + state.Buffers.Count >= state.MaxPendingBatches)
            return Reject("backlog-full");
        var watermark = checked(state.AdmissionWatermark + 1);
        var next = state with { AdmissionWatermark = watermark, HistoryCount = Math.Min(10000, state.HistoryCount + 1), EntryKind = input.EntryKind };
        var batch = open?.Batch ?? new DurableSessionBatch(
            Durable.DurableIdentity.BatchId(state.InstallationId, state.Session, state.Publication.Snapshot.ProjectRevision, state.NextBatchOrdinal),
            "", state.Publication, state.CurrentStateId, state.StateVersion,
            entry ?? new(input.EntryKind, "outbound-history", null, null, null, null, false), [], watermark, watermark, now, SessionBatchCloseReason.Immediate);
        if (open is null)
        {
            batch = batch with { ExecutionId = Durable.DurableIdentity.ExecutionId(state.InstallationId, state.Session, batch.BatchId, "flow") };
            next = next with { NextBatchOrdinal = checked(state.NextBatchOrdinal + 1) };
        }
        batch = batch with { Inputs = batch.Inputs.Append(input).ToArray(), HistoryWatermark = watermark };
        if (input.Direction == SessionMessageDirection.Outbound) return new(next, new(SessionAdmissionStatus.Accepted, watermark, null), batch);
        if (batch.Entry.AccumulatorNodeId is null)
            return new(next with { Queue = next.Queue.Append(batch).ToArray() }, new(SessionAdmissionStatus.Accepted, watermark, null), batch);
        var buffer = new DurableSessionBuffer(batch, batch.Entry.QuietPeriod is { } quiet ? now + quiet : null,
            open?.MaxWaitDueAt ?? (batch.Entry.MaxWait is { } wait ? now + wait : null));
        next = next with { Buffers = next.Buffers.Where(b => b.Batch.BatchId != batch.BatchId).Append(buffer).ToArray() };
        if (batch.Entry.MaxMessages is { } max && batch.Inputs.Count >= max)
            next = CloseBuffer(next, batch.BatchId, SessionBatchCloseReason.MaxMessages, now).State;
        return new(next, new(SessionAdmissionStatus.Accepted, watermark, null), batch);
    }

    public static SessionPolicyDecision CloseBuffer(DurableSessionState state, string bufferId, SessionBatchCloseReason reason, DateTimeOffset now)
    {
        var buffer = state.Buffers.SingleOrDefault(b => b.Batch.BatchId == bufferId);
        if (buffer is null) return new(state, new(SessionAdmissionStatus.Duplicate, null, null), null);
        var batch = buffer.Batch with { CloseReason = reason, ClosedAt = now };
        return new(state with { Buffers = state.Buffers.Where(b => b.Batch.BatchId != bufferId).ToArray(), Queue = state.Queue.Append(batch).ToArray() },
            new(SessionAdmissionStatus.Accepted, state.AdmissionWatermark, null), batch);
    }

    public static bool CanRotate(DurableSessionState state, bool allHandlersFinished) => state.ActiveBatch is null && state.Buffers.Count == 0 && allHandlersFinished;
}
