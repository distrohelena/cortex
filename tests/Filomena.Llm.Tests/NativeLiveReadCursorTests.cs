using Filomena.App;
using Filomena.OpenClaw.Observation;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeLiveReadCursorTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("live-read-cursor-").FullName;
    private static NativeObservationCursor Cursor(long sequence, string version = "inbound-observation:v2") => new(version, "epoch", "session", sequence);

    [Fact]
    public void Reopen_uses_last_processed_cursor_not_latest_snapshot_tail()
    {
        new NativeLiveReadCursorStore(directory, "binding", "project").Save(Cursor(3));
        var reopened = new NativeLiveReadCursorStore(directory, "binding", "project");
        Assert.Equal(Cursor(3), reopened.Resolve(Cursor(8), Cursor(0)));
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("project")]
    [InlineData("epoch")]
    [InlineData("session")]
    [InlineData("future")]
    [InlineData("corrupt")]
    public void Invalid_position_never_falls_back_to_the_snapshot_tail(string change)
    {
        new NativeLiveReadCursorStore(directory, "binding", "project").Save(Cursor(3));
        if (change == "corrupt") File.WriteAllText(Path.Combine(directory, "native-live-read-cursor.json"), "{}");
        var store = new NativeLiveReadCursorStore(directory, change == "binding" ? "other" : "binding",
            change == "project" ? "other" : "project");
        var current = Cursor(change == "future" ? 2 : 8) with {
            Epoch = change == "epoch" ? "other" : "epoch", SessionRef = change == "session" ? "other" : "session" };
        if (change is "session" or "future") Assert.Throws<InvalidDataException>(() => store.Resolve(current, current));
        else Assert.Throws<NativeObservationException>(() => store.Resolve(current, current));
    }

    [Fact]
    public void Missing_checkpoint_without_explicit_initial_anchor_cannot_skip_backlog()
    {
        var store = new NativeLiveReadCursorStore(directory, "binding", "project");
        Assert.Throws<NativeObservationException>(() => store.Resolve(Cursor(9), null));
        Assert.Equal(Cursor(0), store.Resolve(Cursor(9), Cursor(0)));
    }

    [Fact]
    public void Checkpoint_regression_is_rejected_without_replacing_good_position()
    {
        var store = new NativeLiveReadCursorStore(directory, "binding", "project");
        store.Save(Cursor(3));
        Assert.Throws<NativeObservationException>(() => store.Save(Cursor(2)));
        Assert.Equal(Cursor(3), store.Resolve(Cursor(9), Cursor(0)));
    }

    [Fact]
    public void DM_v1_checkpoint_resumes_exact_session_and_rejects_version_change_or_regression()
    {
        var store = new NativeLiveReadCursorStore(directory, "dm-binding", "project");
        store.Save(Cursor(3, "inbound-observation:v1"));
        Assert.Equal(Cursor(3, "inbound-observation:v1"),
            store.Resolve(Cursor(8, "inbound-observation:v1"), Cursor(0, "inbound-observation:v1")));
        Assert.Throws<NativeObservationException>(() => store.Save(Cursor(2, "inbound-observation:v1")));
        Assert.Throws<InvalidDataException>(() => store.Resolve(Cursor(8), Cursor(0)));
        Assert.Throws<InvalidDataException>(() => store.Resolve(
            Cursor(8, "inbound-observation:v1") with { SessionRef = "other" }, Cursor(0, "inbound-observation:v1")));
        Assert.Equal(Cursor(3, "inbound-observation:v1"),
            store.Resolve(Cursor(8, "inbound-observation:v1"), Cursor(0, "inbound-observation:v1")));
    }
    public void Dispose() => Directory.Delete(directory, true);
}
