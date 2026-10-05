using Cortex.Core.Flows;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class LocalBackendCompatibilityTests
{
    [Fact]
    public async Task AsyncBackendPagesHistoryOutcomesAndTracesWithoutDuplicatingAdmission()
    {
        var model = new IncrementalKernelTests.Model();
        await using var backend = new LocalSessionExecutionBackend(new SessionFlowRuntime(TimeProvider.System, model));
        await backend.PublishAsync(DurableDriverTests.Project(), TestContext.Current.CancellationToken);
        var key = DurableIdentityTests.Session with { NativeConversationId = "fixture" };
        var message = new SessionMessageEnvelope("input", key.ChannelId, key.AccountId, key.NativeConversationId, DateTimeOffset.UtcNow,
            SessionMessageDirection.Inbound, SessionEntryKind.Dm, IncrementalKernelTests.Message());
        Assert.Equal(SessionAdmissionStatus.Accepted, (await backend.ReceiveAsync(key, message, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(SessionAdmissionStatus.Duplicate, (await backend.ReceiveAsync(key, message, TestContext.Current.CancellationToken)).Status);
        await backend.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        Assert.Single((await backend.ReadHistoryAsync(key, new(), TestContext.Current.CancellationToken)).Items);
        Assert.Single((await backend.ReadOutcomesAsync(key, new(), TestContext.Current.CancellationToken)).Items);
        var traces = new List<SessionTraceEvent>();
        string? cursor = null;
        do { var page = await backend.ReadTraceAsync(key, new(cursor, 2), TestContext.Current.CancellationToken); traces.AddRange(page.Items); cursor = page.NextCursor; }
        while (cursor is not null);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(traces, trace => trace.EventName == "execution-completed");
    }

    [Fact]
    public async Task IdleImportIsIdempotentAndCannotOverwriteDivergentState()
    {
        var project = DurableDriverTests.Project();
        await using var source = new SessionFlowRuntime(TimeProvider.System, new IncrementalKernelTests.Model());
        source.UpdateProject(project);
        var key = DurableIdentityTests.Session with { NativeConversationId = "fixture" };
        await source.ReceiveAsync(key, new("input", key.ChannelId, key.AccountId, key.NativeConversationId, DateTimeOffset.UtcNow,
            SessionMessageDirection.Inbound, SessionEntryKind.Dm, IncrementalKernelTests.Message()), TestContext.Current.CancellationToken);
        await source.WaitForIdleAsync(key, TestContext.Current.CancellationToken);
        var captured = source.CaptureContext(key)!;
        var request = new SessionImportRequest("cutover", "installation", captured.Snapshot, captured.History, captured.SeenProviderIds, "epoch", 1, "journal-fingerprint");
        await using var backend = new LocalSessionExecutionBackend(new SessionFlowRuntime(TimeProvider.System, new IncrementalKernelTests.Model()));
        await backend.PublishAsync(project, TestContext.Current.CancellationToken);
        Assert.Equal(captured.Snapshot, await backend.ImportIdleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(captured.Snapshot, await backend.ImportIdleAsync(request, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => backend.ImportIdleAsync(request with { Snapshot = request.Snapshot with { StateVersion = 99 } }, TestContext.Current.CancellationToken));
    }
}
