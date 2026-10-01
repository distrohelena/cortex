using System.Text.Json;
using Filomena.App;
using Cortex.OpenClaw.Observation;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeReplayPlanTests
{
    [Fact]
    public void Group_closed_epoch_retains_native_identity_and_requires_matching_version()
    {
        var after = Cursor(0) with { Version = "inbound-observation:v2" };
        var plan = new NativeReplayPlan(after, 1);
        var batch = plan.Next(new([Event(1)], after with { Sequence = 1 }, true), after);
        Assert.Equal("native-1", Assert.Single(batch.Records).NativeMessageId);
        Assert.Equal("inbound-observation:v2", batch.Cursor.Version);
        Assert.Throws<InvalidDataException>(() => plan.Next(new([Event(1)], Cursor(1), true), Cursor(0)));
    }
    private static NativeObservationCursor Cursor(long sequence, string epoch = "old") => new("inbound-observation:v1", epoch, "session", sequence);
    private static NativeObservationEvent Event(long sequence) => new(sequence, "native-" + sequence, "session", "peer", false, "self",
        DateTimeOffset.UnixEpoch, JsonSerializer.SerializeToElement(new { text = "fixture" }), "old");

    [Fact]
    public void Closed_epoch_recovery_preserves_ID_epoch_and_order_one_record_at_a_time()
    {
        var plan = new NativeReplayPlan(Cursor(0), 2);
        plan.Validate("session");
        var first = plan.Next(new([Event(1), Event(2)], Cursor(2), true), Cursor(0));
        Assert.Equal(Event(1).NativeMessageId, Assert.Single(first.Records).NativeMessageId);
        Assert.Equal(Cursor(1), first.Cursor);
        var second = plan.Next(new([Event(2)], Cursor(2), true), first.Cursor);
        Assert.Equal("native-2", Assert.Single(second.Records).NativeMessageId);
        Assert.Equal(Cursor(2), second.Cursor);
    }

    [Theory]
    [InlineData("wrong_session")]
    [InlineData("version")]
    [InlineData("negative")]
    [InlineData("empty")]
    [InlineData("too_large")]
    public void Invalid_recovery_scope_fails_before_transport(string scenario)
    {
        var cursor = Cursor(scenario == "negative" ? -1 : 0) with {
            SessionRef = scenario == "wrong_session" ? "other" : "session",
            Version = scenario == "version" ? "v2" : "inbound-observation:v1" };
        var plan = new NativeReplayPlan(cursor, scenario == "empty" ? 0 : scenario == "too_large" ? 21 : 2);
        Assert.Throws<InvalidDataException>(() => plan.Validate("session"));
    }

    [Theory]
    [InlineData("open_epoch")]
    [InlineData("changed_tail")]
    [InlineData("gap")]
    [InlineData("wrong_epoch")]
    [InlineData("wrong_record_session")]
    public void Unproven_recovery_batch_never_reaches_execution(string scenario)
    {
        var plan = new NativeReplayPlan(Cursor(0), 1);
        var item = Event(scenario == "gap" ? 2 : 1) with { SessionRef = scenario == "wrong_record_session" ? "other" : "session" };
        var batch = new NativeObservationBatch([item], Cursor(scenario == "changed_tail" ? 2 : 1,
            scenario == "wrong_epoch" ? "other" : "old"), scenario != "open_epoch");
        Assert.Throws<InvalidDataException>(() => plan.Next(batch, Cursor(0)));
    }
}
