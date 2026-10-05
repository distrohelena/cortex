using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Activities;
using Cortex.Temporal.Client;
using Cortex.Temporal.Workflows;
using Temporalio.Worker;
using Xunit;

namespace Cortex.Temporal.Tests;

public sealed class TemporalCutoverTests
{
    [Fact]
    public async Task SyntheticDryRunImportRetryAndRestartPreserveHistoryAndDoNotStartChildren()
    {
        var token = TestContext.Current.CancellationToken;
        await using var environment = await TemporalTestEnvironment.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "cortex-cutover-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new FileExecutionArtifactStore(directory, "installation");
            var project = SessionWorkflowTests.Project(); var session = SessionWorkflowTests.Session("cutover");
            var message = SessionWorkflowTests.Message(session, "retained");
            var snapshot = new SessionSnapshot(session, project.ProjectId, project.ProjectVersion, project.ProjectRevision, "initial", 3, DateTimeOffset.UtcNow, 42, 1, 0, 0);
            var history = new SessionHistoryEntry(42, message.ProviderMessageId, message.Timestamp, message.Direction, message.EntryKind, message.ChannelId, message.AccountId, message.NativeConversationId, message.Message);
            var fingerprint = new string('a', 64);
            var manifest = await SessionCutover.CaptureAsync("local", "installation", project, snapshot, [history], ["retained", "no-longer-retained"], "epoch", 17, fingerprint, false, false, store, token);
            var dryRun = await SessionCutover.ReadRequestAsync(manifest, store, fingerprint, token);
            Assert.Equal(snapshot, dryRun.Snapshot); Assert.Equal(2, dryRun.SeenProviderIds.Count);
            var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), new NoBinding());
            var queue = "cutover-" + Guid.NewGuid().ToString("N");
            await using var backend = new TemporalSessionExecutionBackend(environment.Client, new("installation", "unused:7233", "default", queue, queue + "-effects", directory), store, artifacts, ActivityPolicyTests.Policy() with { EffectTaskQueue = queue });
            var workerOptions = new TemporalWorkerOptions(queue).AddWorkflow<SessionWorkflowV1>().AddAllActivities(new SessionActivities(artifacts));
            using (var worker = new TemporalWorker(environment.Client, workerOptions))
                await worker.ExecuteAsync(async () =>
                {
                    Assert.Equal(snapshot, await SessionCutover.ImportAsync(manifest, store, backend, fingerprint, token));
                    await SessionCutover.RecordVerifiedImportAsync(manifest, snapshot, store, token);
                    Assert.Equal(manifest, await SessionCutover.ReadVerifiedImportAsync("installation", session, project.ProjectRevision, store, token));
                    Assert.Equal(SessionAdmissionStatus.Duplicate, (await backend.ReceiveAsync(session, message, token)).Status);
                    Assert.Equal(SessionAdmissionStatus.Duplicate, (await backend.ReceiveAsync(session, message with { ProviderMessageId = "no-longer-retained" }, token)).Status);
                }, token);
            using (var worker = new TemporalWorker(environment.Client, workerOptions))
                await worker.ExecuteAsync(async () =>
                {
                    Assert.Equal(snapshot, await SessionCutover.ImportAsync(manifest, store, backend, fingerprint, token));
                    Assert.Equal("retained", Assert.Single((await backend.ReadHistoryAsync(session, new(), token)).Items).ProviderMessageId);
                    Assert.Null(await backend.ReadActiveExecutionAsync(session, token));
                    Assert.Empty((await backend.ReadOutcomesAsync(session, new(), token)).Items);
                    var handle = environment.Client.GetWorkflowHandle<SessionWorkflowV1>(DurableIdentity.SessionWorkflowId("installation", session));
                    Assert.DoesNotContain((await handle.FetchHistoryAsync()).Events, e => e.StartChildWorkflowExecutionInitiatedEventAttributes is not null);
                    await Assert.ThrowsAsync<InvalidDataException>(() => SessionCutover.ImportAsync(manifest, store, backend, new string('b', 64), token));
                }, token);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class NoBinding : IExecutionHostBindings
    {
        public ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token) => throw new InvalidOperationException("Import must not bind or execute effects.");
    }
}
