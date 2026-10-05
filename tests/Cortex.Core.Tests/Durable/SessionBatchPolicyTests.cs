using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class SessionBatchPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
    private static DurableSessionState State(int limit = 256, SessionEntryPolicy? entry = null)
    {
        var scope = DurableIdentityTests.Scope;
        var publication = new ProjectPublicationDescriptor(new("project", 1, "revision", "initial", ["initial"]), new(1, scope, "project", new string('0', 64)),
            [new("initial", entry ?? new(SessionEntryKind.Dm, "in", null, null, null, null, false))]);
        return DurableSessionState.Create(scope.InstallationId, scope.Session, publication, limit, Now);
    }
    private static AdmissionDescriptor Input(int n) => new("input-" + n,
        new(1, DurableIdentityTests.Scope, "input-" + n, n.ToString("x64")), n.ToString("x64"), SessionEntryKind.Dm, SessionMessageDirection.Inbound, Now);

    [Fact]
    public void Backlog256Rejects257WithoutWatermarkChange()
    {
        var state = State();
        for (var n = 1; n <= 256; n++) state = SessionBatchPolicy.Admit(state, Input(n), Now).State;
        var rejected = SessionBatchPolicy.Admit(state, Input(257), Now);
        Assert.Equal(SessionAdmissionStatus.Rejected, rejected.Admission.Status); Assert.Equal("backlog-full", rejected.Admission.Reason);
        Assert.Equal(256, rejected.State.AdmissionWatermark); Assert.Equal(256, rejected.State.Queue.Count);
    }
    [Fact]
    public void BufferReservesOneSlotAndCanCloseAtCapacity()
    {
        var state = State(1, new(SessionEntryKind.Dm, "in", "acc", TimeSpan.FromSeconds(5), 2, TimeSpan.FromSeconds(20), false));
        state = SessionBatchPolicy.Admit(state, Input(1), Now).State;
        Assert.Single(state.Buffers); Assert.Empty(state.Queue);
        state = SessionBatchPolicy.Admit(state, Input(2), Now).State;
        Assert.Empty(state.Buffers); Assert.Equal(SessionBatchCloseReason.MaxMessages, Assert.Single(state.Queue).CloseReason);
        var twice = SessionBatchPolicy.CloseBuffer(state, state.Queue[0].BatchId, SessionBatchCloseReason.QuietPeriod, Now);
        Assert.Single(twice.State.Queue);
    }
    [Fact]
    public void BufferKeepsCapturedStateAndEntryAcrossTransition()
    {
        var state = State(4, new(SessionEntryKind.Dm, "in", "acc", TimeSpan.FromSeconds(5), 3, null, false));
        state = SessionBatchPolicy.Admit(state, Input(1), Now).State;
        state = state with { CurrentStateId = "later", StateVersion = 1 };
        state = SessionBatchPolicy.Admit(state, Input(2), Now).State;
        var buffer = Assert.Single(state.Buffers); Assert.Equal("initial", buffer.Batch.StateId); Assert.Equal(2, buffer.Batch.Inputs.Count);
        state = SessionBatchPolicy.CloseBuffer(state, buffer.Batch.BatchId, SessionBatchCloseReason.QuietPeriod, Now).State;
        Assert.Equal("initial", Assert.Single(state.Queue).StateId);
    }
    [Theory] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, true)]
    public void RotationRequiresNoActiveChildBufferOrHandler(bool active, bool buffer, bool handler)
    {
        var state = State(4, new(SessionEntryKind.Dm, "in", buffer ? "acc" : null, buffer ? TimeSpan.FromSeconds(1) : null, null, null, false));
        state = SessionBatchPolicy.Admit(state, Input(1), Now).State;
        if (active) state = state with { ActiveBatch = state.Queue[0], Queue = [] };
        Assert.False(SessionBatchPolicy.CanRotate(state, !handler));
        if (!active && !buffer) Assert.True(SessionBatchPolicy.CanRotate(state, true));
    }
}
