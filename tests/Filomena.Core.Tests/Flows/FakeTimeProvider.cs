namespace Filomena.Core.Tests.Flows;

internal sealed class FakeTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private readonly object gate = new();
    private readonly List<FakeTimer> timers = [];
    private DateTimeOffset now = start ?? DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (gate) return now.UtcDateTime.Ticks; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period);
        lock (gate) timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        var target = GetUtcNow() + elapsed;
        while (true)
        {
            FakeTimer? next;
            lock (gate)
            {
                next = timers.Where(timer => timer.IsDueAtOrBefore(target)).OrderBy(timer => timer.Due).FirstOrDefault();
                if (next is null) { now = target; return; }
                now = next.Due;
                next.ScheduleAfterFire(now);
            }
            next.Callback(next.State);
        }
    }

    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private bool disposed;
        private TimeSpan period = period;
        internal TimerCallback Callback { get; } = callback;
        internal object? State { get; } = state;
        internal DateTimeOffset Due { get; private set; } = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.GetUtcNow() + dueTime;
        internal bool IsDueAtOrBefore(DateTimeOffset at) => !disposed && Due <= at;
        internal void ScheduleAfterFire(DateTimeOffset at) => Due = period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : at + period;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed) return false;
                this.period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + dueTime;
                return true;
            }
        }
        public void Dispose() { lock (owner.gate) disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
