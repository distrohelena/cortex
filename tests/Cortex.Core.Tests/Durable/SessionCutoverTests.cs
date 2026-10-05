using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class SessionCutoverTests
{
    [Fact]
    public async Task ReadOnlyJournalInspectionKeepsUnresolvedReservationAndNeverMutatesSource()
    {
        using var directory = new ArtifactDirectory(); Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "source-outbound.jsonl");
        using (var journal = new OutboundJournal(path)) journal.Reserve("execution", "session", ["input"], "revision", "private candidate", new("Host", "self"));
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        using (var inspector = new OutboundJournal(path, readOnly: true)) Assert.True(inspector.RequiresReconciliation("session"));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdleImportPreservesStateHistoryWatermarkDedupeAndSameManifestRetry()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = DurableDriverTests.Project(); var session = DurableIdentityTests.Scope.Session;
        var snapshot = new SessionSnapshot(session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, "initial", 4, DateTimeOffset.UtcNow, 23, 0, 0, 0);
        var fingerprint = new string('a', 64);
        var manifest = await SessionCutover.CaptureAsync("local", "installation", project, snapshot, [], ["historical-provider"], "epoch", 17, fingerprint, false, false, store, TestContext.Current.CancellationToken);
        await using var backend = new LocalSessionExecutionBackend(new SessionFlowRuntime(TimeProvider.System, new IncrementalKernelTests.Model()));
        Assert.Equal(snapshot, await SessionCutover.ImportAsync(manifest, store, backend, fingerprint, TestContext.Current.CancellationToken));
        Assert.Equal(snapshot, await SessionCutover.ImportAsync(manifest, store, backend, fingerprint, TestContext.Current.CancellationToken));
        var envelope = new SessionMessageEnvelope("historical-provider", session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm,
            JsonSerializer.SerializeToElement(new { texto = "synthetic", remetente = (object?)null, canal = new { nome = "DM", nativeId = session.NativeConversationId }, reply = (object?)null }));
        Assert.Equal(SessionAdmissionStatus.Duplicate, (await backend.ReceiveAsync(session, envelope, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(23, (await backend.ReadSessionAsync(session, TestContext.Current.CancellationToken))!.AdmissionWatermark);
        Assert.Throws<InvalidDataException>(() => SessionCutover.ValidateFingerprint(manifest, new string('b', 64)));
        Assert.Throws<InvalidOperationException>(() => SessionCutover.ValidateRollback(manifest, snapshot with { AdmissionWatermark = 24 }));
        SessionCutover.ValidateRollback(manifest, snapshot);
        foreach (var source in new[] { snapshot with { OpenBufferCount = 1 }, snapshot with { PendingExecutionCount = 1 }, snapshot with { StateVersion = 5 } })
            Assert.Throws<InvalidDataException>(() => SessionCutover.Validate(manifest, source, false, false));
        Assert.Throws<InvalidDataException>(() => SessionCutover.Validate(manifest, snapshot, true, false));
        Assert.Throws<InvalidDataException>(() => SessionCutover.Validate(manifest, snapshot, false, true));
        await backend.ReceiveAsync(session, envelope with { ProviderMessageId = "new-receipt" }, TestContext.Current.CancellationToken);
        await backend.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => SessionCutover.ImportAsync(manifest, store, backend, fingerprint, TestContext.Current.CancellationToken));
    }
}
