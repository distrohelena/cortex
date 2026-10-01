using Filomena.App;
using Cortex.OpenClaw.Observation;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeLiveContinuationTests
{
    [Theory]
    [InlineData("same", true)]
    [InlineData("epoch", false)]
    [InlineData("session", false)]
    [InlineData("version", false)]
    [InlineData("negative", false)]
    [InlineData("future", false)]
    public void Continuation_never_crosses_the_current_native_epoch(string change, bool valid)
    {
        var current = new NativeObservationCursor("inbound-observation:v2", "epoch", "session", 7);
        var after = current with { Sequence = change == "negative" ? -1 : change == "future" ? 8 : 3,
            Epoch = change == "epoch" ? "other" : current.Epoch,
            SessionRef = change == "session" ? "other" : current.SessionRef,
            Version = change == "version" ? "inbound-observation:v1" : current.Version };
        if (valid) Assert.Equal(after, NativeLiveCommand.ValidateContinuation(after, current));
        else Assert.Throws<InvalidDataException>(() => NativeLiveCommand.ValidateContinuation(after, current));
    }

    [Fact]
    public void DM_v1_continuation_requires_exact_session_epoch_and_non_regressing_sequence()
    {
        var current = new NativeObservationCursor("inbound-observation:v1", "epoch", "dm-session", 7);
        Assert.Equal(current with { Sequence = 3 }, NativeLiveCommand.ValidateContinuation(
            current with { Sequence = 3 }, current));
        Assert.Throws<InvalidDataException>(() => NativeLiveCommand.ValidateContinuation(
            current with { SessionRef = "other" }, current));
        Assert.Throws<InvalidDataException>(() => NativeLiveCommand.ValidateContinuation(
            current with { Epoch = "other" }, current));
        Assert.Throws<InvalidDataException>(() => NativeLiveCommand.ValidateContinuation(
            current with { Sequence = 8 }, current));
        Assert.Throws<InvalidDataException>(() => NativeLiveCommand.ValidateContinuation(
            current with { Version = "inbound-observation:v2" }, current));
    }
}
