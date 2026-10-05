using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Temporal.Hosting;
using Cortex.Temporal.Workflows;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Cortex.Temporal.Client;

public sealed class TemporalSessionExecutionBackend : ISessionExecutionBackend
{
    private readonly ITemporalClient client;
    private readonly TemporalRuntimeOptions options;
    private readonly FileExecutionArtifactStore store;
    private readonly DurableSessionArtifacts artifacts;
    private readonly WorkflowActivityPolicy policy;
    private readonly int rotationEventLimit;
    private readonly SemaphoreSlim publications = new(1);
    private readonly HashSet<SessionKey> knownSessions = [];
    private ProjectPublicationDescriptor? publication;
    private bool disposed;

    public TemporalSessionExecutionBackend(ITemporalClient client, TemporalRuntimeOptions options, FileExecutionArtifactStore store,
        DurableSessionArtifacts artifacts, WorkflowActivityPolicy policy, int rotationEventLimit = 10000)
    {
        options.Validate(); policy.Validate();
        if (rotationEventLimit < 20) throw new ArgumentOutOfRangeException(nameof(rotationEventLimit));
        this.client = client; this.options = options; this.store = store; this.artifacts = artifacts; this.policy = policy; this.rotationEventLimit = rotationEventLimit;
    }

    public async Task<SessionFlowProjectSnapshot> PublishAsync(SessionFlowProject project, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await publications.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var scope = new ArtifactScope(options.InstallationId, new("publication", "publication", "publication", "publication", project.ProjectId), "publication", project.ProjectRevision);
            var reference = await store.PutOnceAsync(scope, "project", JsonSerializer.SerializeToElement(project), token).ConfigureAwait(false);
            var validated = await artifacts.ReadPublicationAsync(reference, token).ConfigureAwait(false);
            if (publication is { } old && validated.ProjectRef != old.ProjectRef && (old.Snapshot.ProjectId != project.ProjectId || project.ProjectVersion <= old.Snapshot.ProjectVersion))
                throw new InvalidOperationException("Project identity must stay fixed and version must increase.");
            foreach (var session in knownSessions.ToArray())
                await Handle(session).ExecuteUpdateAsync(w => w.PublishAsync(validated), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
            publication = validated;
            return validated.Snapshot;
        }
        finally { publications.Release(); }
    }

    private async Task<WorkflowHandle<SessionWorkflowV1>> EnsureSession(SessionKey session, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await publications.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var project = publication ?? throw new InvalidOperationException("Publish a project before admitting a session.");
            var id = DurableIdentity.SessionWorkflowId(options.InstallationId, session);
            var input = new SessionWorkflowInput(options.InstallationId, session, project.ProjectRef, options.WorkflowTaskQueue, policy, options.MaxPendingBatches, rotationEventLimit);
            try { await client.StartWorkflowAsync((SessionWorkflowV1 w) => w.RunAsync(input), new(id, options.WorkflowTaskQueue) { Rpc = Rpc(token) }).ConfigureAwait(false); }
            catch (WorkflowAlreadyStartedException) { }
            var handle = Handle(session);
            await handle.ExecuteUpdateAsync(w => w.PublishAsync(project), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
            knownSessions.Add(session);
            return handle;
        }
        finally { publications.Release(); }
    }

    public async Task<SessionAdmission> ReceiveAsync(SessionKey session, SessionMessageEnvelope message, CancellationToken token)
    {
        var handle = await EnsureSession(session, token).ConfigureAwait(false);
        var inputKey = DurableIdentity.AdmissionInputKey(options.InstallationId, session, message.ProviderMessageId);
        var scope = new ArtifactScope(options.InstallationId, session, inputKey, "inbound-v1");
        var reference = await store.PutOnceAsync(scope, "input", JsonSerializer.SerializeToElement(message), token).ConfigureAwait(false);
        var admission = new AdmissionDescriptor(inputKey, reference, reference.Sha256, message.EntryKind, message.Direction, message.Timestamp);
        // Domain dedupe is in the ledger; a new transport Update may recover a previously failed/lost RPC.
        return await handle.ExecuteUpdateAsync(w => w.ReceiveAsync(admission), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
    }

    public async Task<SessionSnapshot?> ReadSessionAsync(SessionKey session, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            while (true)
            {
                var snapshot = await Handle(session).QueryAsync(w => w.Snapshot(), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
                if (snapshot is not null) return snapshot;
                await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
            }
        }
        catch (RpcException error) when (error.Code == RpcException.StatusCode.NotFound) { return null; }
    }
    public Task<ArtifactPage<SessionHistoryEntry>> ReadHistoryAsync(SessionKey session, PageRequest page, CancellationToken token) => artifacts.ReadHistoryAsync(options.InstallationId, session, page, token);
    public Task<ArtifactPage<SessionFlowOutcome>> ReadOutcomesAsync(SessionKey session, PageRequest page, CancellationToken token) => artifacts.ReadOutcomesAsync(options.InstallationId, session, page, token);
    public Task<ArtifactPage<SessionTraceEvent>> ReadTraceAsync(SessionKey session, PageRequest page, CancellationToken token) => artifacts.ReadTraceAsync(options.InstallationId, session, page, token);

    public async Task WaitForIdleAsync(SessionKey session, CancellationToken token)
    {
        while (await ReadSessionAsync(session, token).ConfigureAwait(false) is { } state && (state.PendingExecutionCount > 0 || state.OpenBufferCount > 0))
            await Task.Delay(TimeSpan.FromMilliseconds(50), token).ConfigureAwait(false);
    }
    public Task CancelExecutionAsync(SessionKey session, string executionId, CancellationToken token) => Handle(session).ExecuteUpdateAsync(w => w.CancelExecutionAsync(executionId), new() { Rpc = Rpc(token) });

    public async Task<ArtifactPage<FeedbackDescriptor>> ReadFeedbackAsync(SessionKey session, PageRequest page, CancellationToken token)
    {
        page.Validate();
        if (page.Cursor is not null) throw new InvalidDataException("Active feedback has a single page.");
        var active = await Handle(session).QueryAsync(w => w.ActiveExecution(), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
        if (active is null) return new([], null);
        var descriptor = await client.GetWorkflowHandle<FlowExecutionWorkflowV1>(active.WorkflowId).QueryAsync(w => w.Status(), new() { Rpc = Rpc(token) }).ConfigureAwait(false);
        return descriptor?.Status == ExecutionStatus.WaitingForHuman && descriptor.Feedback is { } request ? new([request], null) : new([], null);
    }
    public async Task<FeedbackReceipt> SubmitFeedbackAsync(FeedbackSubmission submission, CancellationToken token)
    {
        if (submission.Scope.InstallationId != options.InstallationId) throw new InvalidDataException("Foreign feedback installation.");
        var reference = await store.FindAsync(submission.Scope, "prepared", token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Feedback execution is unknown.");
        var prepared = (await store.ReadAsync(reference, token).ConfigureAwait(false)).Deserialize<PreparedSessionExecution>() ?? throw new InvalidDataException("Missing execution locator.");
        if (prepared.Start.Scope != submission.Scope) throw new InvalidDataException("Foreign execution locator.");
        var workflowId = DurableIdentity.ExecutionWorkflowId(submission.Scope, prepared.Start.BatchId);
        return await new TemporalFeedbackClient(client, _ => workflowId).SubmitAsync(submission, token).ConfigureAwait(false);
    }
    public async Task<SessionSnapshot> ImportIdleAsync(SessionImportRequest request, CancellationToken token)
    {
        if (request.InstallationId != options.InstallationId) throw new InvalidDataException("Foreign import installation.");
        var handle = await EnsureSession(request.Snapshot.Session, token).ConfigureAwait(false);
        var scope = new ArtifactScope(options.InstallationId, request.Snapshot.Session, request.CutoverId, request.Snapshot.ProjectRevision);
        var reference = await store.PutOnceAsync(scope, "import-manifest", JsonSerializer.SerializeToElement(request), token).ConfigureAwait(false);
        return await handle.ExecuteUpdateAsync(w => w.ImportIdleAsync(reference), new(request.CutoverId) { Rpc = Rpc(token) }).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() { disposed = true; return ValueTask.CompletedTask; }
    private WorkflowHandle<SessionWorkflowV1> Handle(SessionKey session) => client.GetWorkflowHandle<SessionWorkflowV1>(DurableIdentity.SessionWorkflowId(options.InstallationId, session));
    private static RpcOptions Rpc(CancellationToken token) => new() { CancellationToken = token };
}
