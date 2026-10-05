using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableSessionArtifactsTests
{
    [Fact]
    public async Task HistoryPagesHonorCharacterRetentionAndFreezeAcrossNewAdmissions()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var ledger = new DurableSessionLedger(store); var scope = DurableIdentityTests.Scope;
        var artifacts = new DurableSessionArtifacts(store, ledger, new DurableDriverTests.Bindings(DurableDriverTests.Project(), new IncrementalKernelTests.Model()));
        async Task Append(int ordinal)
        {
            var value = JsonSerializer.SerializeToElement(new { texto = new string('x', 400000), remetente = (object?)null, canal = new { nome = "DM", nativeId = scope.Session.NativeConversationId }, reply = (object?)null });
            var entry = new SessionHistoryEntry(ordinal, "provider-" + ordinal, DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm, scope.Session.ChannelId, scope.Session.AccountId, scope.Session.NativeConversationId, value);
            var reference = await store.PutOnceAsync(scope, "retention-history-" + ordinal, JsonSerializer.SerializeToElement(new { Entry = entry, ConfirmedSendOf = (string?)null }), TestContext.Current.CancellationToken);
            await ledger.RecordOnceAsync(new(scope.InstallationId, scope.Session, "history", "entry-" + ordinal, reference), TestContext.Current.CancellationToken);
        }
        await Append(1); await Append(2); await Append(3);
        var first = await artifacts.ReadHistoryAsync(scope.InstallationId, scope.Session, new(Limit: 1), TestContext.Current.CancellationToken);
        Assert.Equal("provider-2", Assert.Single(first.Items).ProviderMessageId); Assert.NotNull(first.NextCursor);
        await Append(4);
        var second = await artifacts.ReadHistoryAsync(scope.InstallationId, scope.Session, new(first.NextCursor, 1), TestContext.Current.CancellationToken);
        Assert.Equal("provider-3", Assert.Single(second.Items).ProviderMessageId); Assert.Null(second.NextCursor);
        var current = await artifacts.ReadHistoryAsync(scope.InstallationId, scope.Session, new(), TestContext.Current.CancellationToken);
        Assert.Equal(["provider-3", "provider-4"], current.Items.Select(h => h.ProviderMessageId));
        await Assert.ThrowsAsync<InvalidDataException>(() => artifacts.ReadHistoryAsync(scope.InstallationId, scope.Session with { NativeConversationId = "foreign" }, new(first.NextCursor), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportRestoresWatermarkAndDedupeWithoutExecutingHistoricalInput()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = DurableDriverTests.Project(); var model = new IncrementalKernelTests.Model();
        var bindings = new DurableDriverTests.Bindings(project, model);
        var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), bindings);
        var start = await DurableDriverTests.PrepareAsync(store, project);
        var publication = await artifacts.ReadPublicationAsync(start.ProjectRef, TestContext.Current.CancellationToken);
        var snapshot = new SessionSnapshot(start.Scope.Session, "project", 1, "revision", "initial", 3, DateTimeOffset.UtcNow, 42, 0, 0, 0);
        var manifest = new SessionImportRequest("cutover", start.Scope.InstallationId, snapshot, [], ["old-provider"], "epoch", 8, new string('0', 64));
        var reference = await store.PutOnceAsync(start.Scope, "import", JsonSerializer.SerializeToElement(manifest), TestContext.Current.CancellationToken);
        var restored = await artifacts.ImportIdleAsync(reference, publication, TestContext.Current.CancellationToken);
        Assert.Equal(42, restored.AdmissionWatermark); Assert.Empty(model.Requests);
        var envelope = new SessionMessageEnvelope("old-provider", snapshot.Session.ChannelId, snapshot.Session.AccountId, snapshot.Session.NativeConversationId,
            DateTimeOffset.UtcNow, SessionMessageDirection.Inbound, SessionEntryKind.Dm, (await store.ReadAsync(start.BatchRef, TestContext.Current.CancellationToken)).Deserialize<ExecutionBatchV1>()!.Message);
        var inputRef = await store.PutOnceAsync(start.Scope, "old-input", JsonSerializer.SerializeToElement(envelope), TestContext.Current.CancellationToken);
        var input = new AdmissionDescriptor(DurableIdentity.AdmissionInputKey(start.Scope.InstallationId, snapshot.Session, envelope.ProviderMessageId), inputRef, inputRef.Sha256,
            envelope.EntryKind, envelope.Direction, envelope.Timestamp);
        Assert.Equal(SessionAdmissionStatus.Duplicate, (await artifacts.FindAdmissionAsync(start.Scope.InstallationId, snapshot.Session, input, TestContext.Current.CancellationToken))!.Receipt.Status);
        envelope = envelope with { ProviderMessageId = "new-provider" };
        inputRef = await store.PutOnceAsync(start.Scope, "new-input", JsonSerializer.SerializeToElement(envelope), TestContext.Current.CancellationToken);
        input = input with { InputKey = DurableIdentity.AdmissionInputKey(start.Scope.InstallationId, snapshot.Session, envelope.ProviderMessageId), InputRef = inputRef, Digest = inputRef.Sha256 };
        var committed = await artifacts.CommitAdmissionAsync(new(restored, input, SessionBatchPolicy.Admit(restored, input, DateTimeOffset.UtcNow)), TestContext.Current.CancellationToken);
        Assert.Equal(43, committed.Receipt.Watermark); Assert.Empty(model.Requests);
    }
    [Fact]
    public async Task LostAdmissionResultRecoversOriginalDecisionAndHistoryOnce()
    {
        using var directory = new ArtifactDirectory(); await using var store = directory.Open();
        var project = DurableDriverTests.Project();
        var bindings = new DurableDriverTests.Bindings(project, new IncrementalKernelTests.Model());
        var artifacts = new DurableSessionArtifacts(store, new DurableSessionLedger(store), bindings);
        var start = await DurableDriverTests.PrepareAsync(store, project);
        var publication = await artifacts.ReadPublicationAsync(start.ProjectRef, TestContext.Current.CancellationToken);
        var state = DurableSessionState.Create(start.Scope.InstallationId, start.Scope.Session, publication, 256, DateTimeOffset.UtcNow);
        var message = JsonSerializer.Deserialize<ExecutionBatchV1>(await store.ReadAsync(start.BatchRef, TestContext.Current.CancellationToken))!.Message;
        var envelope = new SessionMessageEnvelope("provider", state.Session.ChannelId, state.Session.AccountId, state.Session.NativeConversationId, DateTimeOffset.UtcNow,
            SessionMessageDirection.Inbound, SessionEntryKind.Dm, message);
        var inputRef = await store.PutOnceAsync(start.Scope, "inbound", JsonSerializer.SerializeToElement(envelope), TestContext.Current.CancellationToken);
        var input = new AdmissionDescriptor(DurableIdentity.AdmissionInputKey(state.InstallationId, state.Session, envelope.ProviderMessageId), inputRef, inputRef.Sha256, envelope.EntryKind, envelope.Direction, envelope.Timestamp);
        var decision = SessionBatchPolicy.Admit(state, input, DateTimeOffset.UtcNow);
        var commit = new AdmissionCommitCommand(state, input, decision);
        var first = await artifacts.CommitAdmissionAsync(commit, TestContext.Current.CancellationToken);
        var retry = await new DurableSessionArtifacts(store, new DurableSessionLedger(store), bindings).CommitAdmissionAsync(commit, TestContext.Current.CancellationToken);
        Assert.Equal(first.Receipt, retry.Receipt);
        Assert.Single((await artifacts.ReadHistoryAsync(state.InstallationId, state.Session, new(), TestContext.Current.CancellationToken)).Items);
        var prepared = await artifacts.PrepareExecutionAsync(first.Decision.State.Queue[0], "initial", 0, state.InstallationId, state.Session, TestContext.Current.CancellationToken);
        Assert.Equal(first.Receipt.ExecutionId, prepared.Start.Scope.ExecutionId);
        var driver = new FlowExecutionDriver(store, bindings);
        var execution = await driver.InitializeAsync(prepared.Start, TestContext.Current.CancellationToken);
        while (execution.Status == ExecutionStatus.Ready)
            execution = await driver.AdvanceAsync(DurableDriverTests.Command(execution), TestContext.Current.CancellationToken);
        var command = new OutcomeCommitCommand(first.Decision.State.Queue[0], execution, prepared.StartedAt,
            DateTimeOffset.UtcNow, "initial", 0, 1);
        var outcome = await artifacts.CommitOutcomeAsync(command, TestContext.Current.CancellationToken);
        Assert.Equal(outcome, await artifacts.CommitOutcomeAsync(command, TestContext.Current.CancellationToken));
        Assert.Single((await artifacts.ReadOutcomesAsync(state.InstallationId, state.Session, new(), TestContext.Current.CancellationToken)).Items);
    }
}
