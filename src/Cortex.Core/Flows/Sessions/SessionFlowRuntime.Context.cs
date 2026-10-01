namespace Cortex.Core.Flows.Sessions;

// Host-only context, not work to replay, project configuration or a model output.
internal sealed record SessionRuntimeContext(SessionSnapshot Snapshot,
    IReadOnlyList<SessionHistoryEntry> History, IReadOnlyList<string> SeenProviderIds);

public sealed partial class SessionFlowRuntime
{
    internal SessionRuntimeContext? CaptureContext(SessionKey session)
    {
        if (!sessions.TryGetValue(session, out var state)) return null;
        lock (state.Gate)
        {
            if (state.Buffers.Count != 0 || state.PendingExecutions != 0)
                throw new InvalidOperationException("Session must be idle before checkpoint.");
            return new(ReadSessionCore(session)!, ReadHistoryCore(session), state.ProviderMessageIds.ToArray());
        }
    }

    internal void RestoreContext(SessionKey expectedSession, SessionRuntimeContext context,
        SessionEntryKind expectedKind = SessionEntryKind.Dm)
    {
        lock (administrationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var snapshot = context.Snapshot;
            if (expectedKind is not (SessionEntryKind.Dm or SessionEntryKind.Group)
                || project is null || snapshot.Session != expectedSession || snapshot.ProjectId != project.ProjectId
                || snapshot.ProjectVersion != project.ProjectVersion || snapshot.ProjectRevision != project.ProjectRevision
                || !project.States.ContainsKey(snapshot.CurrentStateId) || snapshot.StateVersion < 0
                || snapshot.AdmissionWatermark < 0 || snapshot.OpenBufferCount != 0 || snapshot.PendingExecutionCount != 0
                || snapshot.HistoryCount != context.History.Count || context.History.Count > RetainedMessageLimit
                || context.SeenProviderIds.Any(string.IsNullOrWhiteSpace)
                || context.SeenProviderIds.Distinct(StringComparer.Ordinal).Count() != context.SeenProviderIds.Count)
                throw new InvalidDataException("Invalid live context binding or state.");
            var restored = new RuntimeSession(expectedSession, snapshot.CurrentStateId, expectedKind,
                snapshot.EnteredAt, project) { StateVersion = snapshot.StateVersion, AdmissionWatermark = snapshot.AdmissionWatermark };
            restored.ProviderMessageIds.UnionWith(context.SeenProviderIds);
            long previous = 0;
            var retainedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in context.History)
            {
                var envelope = new SessionMessageEnvelope(item.ProviderMessageId, item.ChannelId, item.AccountId,
                    item.NativeConversationId, item.Timestamp, item.Direction, item.EntryKind, item.Message);
                if (ValidateEnvelope(expectedSession, envelope) is not null || item.EntryKind != expectedKind
                    || item.AdmissionWatermark <= previous || item.AdmissionWatermark > snapshot.AdmissionWatermark
                    || !restored.ProviderMessageIds.Contains(item.ProviderMessageId) || !retainedIds.Add(item.ProviderMessageId))
                    throw new InvalidDataException("Invalid live context history.");
                FlowDomain.Check(item.Message, FlowDomain.Message);
                if (item.Message.GetProperty("canal").GetProperty("nativeId").GetString() != expectedSession.NativeConversationId)
                    throw new InvalidDataException("Invalid live context channel.");
                restored.History.Add(new(item.AdmissionWatermark, CloneEnvelope(envelope)));
                previous = item.AdmissionWatermark;
            }
            if (restored.History.Sum(m => m.Envelope.Message.GetProperty("texto").GetString()!.Length) > RetainedCharacterLimit)
                throw new InvalidDataException("Live context retention exceeded.");
            // No ReceiveAsync, timers, completions, memory calls, transitions or sends during hydration.
            if (!sessions.TryAdd(expectedSession, restored)) throw new InvalidOperationException("Cannot overwrite an existing session.");
        }
    }
}
