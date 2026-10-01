using Cortex.Core.Memory;

namespace Cortex.Core.Flows.Sessions;

public sealed record SessionInstructionOutput(
    string ExecutionId, string BatchId, SessionKey Session, string StateId, long StateVersion,
    string ProjectRevision, string NodeId, IReadOnlyDictionary<string, TypedValue> Outputs);

public sealed record SessionNodeVisit(
    string ExecutionId, string BatchId, SessionKey Session, string StateId, long StateVersion,
    string ProjectRevision, string NodeId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypedValue>> Outputs);

public sealed partial class SessionFlowRuntime : IAsyncDisposable
{
    private readonly object administrationGate = new();
    private readonly TimeProvider timeProvider;
    private readonly ISessionFlowCompletionFactory completionFactory;
    private readonly IMemoryReader memoryReader;
    private readonly Func<SessionInstructionOutput, CancellationToken, Task>? instructionOutput;
    private readonly Func<SessionNodeVisit, CancellationToken, Task>? nodeStarting;
    private readonly Outbound.ShadowOutboundHost? outboundHost;
    private CompiledSessionFlowProject? project;

    public SessionFlowRuntime(TimeProvider timeProvider, IFlowCompletion completion, IMemoryReader? memoryReader = null,
        Func<SessionInstructionOutput, CancellationToken, Task>? instructionOutput = null,
        Func<SessionNodeVisit, CancellationToken, Task>? nodeStarting = null,
        Outbound.ShadowOutboundHost? outboundHost = null)
        : this(timeProvider, ConstantCompletion(completion), memoryReader, instructionOutput, nodeStarting, outboundHost)
    {
    }

    public SessionFlowRuntime(TimeProvider timeProvider, Func<SessionKey, IFlowCompletion> completionFactory, IMemoryReader? memoryReader = null,
        Func<SessionInstructionOutput, CancellationToken, Task>? instructionOutput = null,
        Func<SessionNodeVisit, CancellationToken, Task>? nodeStarting = null,
        Outbound.ShadowOutboundHost? outboundHost = null)
        : this(timeProvider, new LegacyCompletionFactory(completionFactory), memoryReader, instructionOutput, nodeStarting, outboundHost)
    {
    }

    public SessionFlowRuntime(TimeProvider timeProvider, ISessionFlowCompletionFactory completionFactory, IMemoryReader? memoryReader = null,
        Func<SessionInstructionOutput, CancellationToken, Task>? instructionOutput = null,
        Func<SessionNodeVisit, CancellationToken, Task>? nodeStarting = null,
        Outbound.ShadowOutboundHost? outboundHost = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(completionFactory);
        this.timeProvider = timeProvider;
        this.completionFactory = completionFactory;
        this.memoryReader = memoryReader ?? new UnavailableMemoryReader();
        this.instructionOutput = instructionOutput;
        this.nodeStarting = nodeStarting;
        this.outboundHost = outboundHost;
    }

    public SessionFlowProjectSnapshot UpdateProject(SessionFlowProject project)
    {
        var compiled = SessionFlowProjectCompiler.Compile(project);
        lock (administrationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (this.project is { } current)
            {
                if (!StringComparer.Ordinal.Equals(current.ProjectId, compiled.ProjectId))
                    throw new InvalidOperationException("A runtime cannot change project identity.");
                if (compiled.ProjectVersion <= current.ProjectVersion)
                    throw new InvalidOperationException("ProjectVersion must increase on every publication.");
                if (StringComparer.Ordinal.Equals(compiled.ProjectRevision, current.ProjectRevision))
                    throw new InvalidOperationException("ProjectRevision must be unique on every publication.");
            }
            this.project = compiled;
            return compiled.Snapshot();
        }
    }

    public ValueTask<SessionAdmission> ReceiveAsync(
        SessionKey session,
        SessionMessageEnvelope message,
        CancellationToken cancellationToken = default) =>
        ReceiveCore(session, message, cancellationToken);

    public SessionSnapshot? ReadSession(SessionKey session) => ReadSessionCore(session);

    public IReadOnlyList<SessionFlowOutcome> ReadOutcomes(SessionKey session) => ReadOutcomesCore(session);

    public IReadOnlyList<SessionTraceEvent> ReadTrace(SessionKey session) => ReadTraceCore(session);

    public IReadOnlyList<SessionHistoryEntry> ReadHistory(SessionKey session) => ReadHistoryCore(session);

    public Task WaitForIdleAsync(SessionKey session, CancellationToken cancellationToken = default) =>
        WaitForIdleCoreAsync(session, cancellationToken);

    public ValueTask DisposeAsync() => DisposeCoreAsync();

    private static Func<SessionKey, IFlowCompletion> ConstantCompletion(IFlowCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return _ => completion;
    }

    private IFlowCompletion CreateCompletion(FlowBatch batch) => completionFactory.Create(new(batch.Session,
        batch.ExecutionId, batch.BatchId, batch.Project.ProjectId, batch.Project.ProjectVersion, batch.Project.ProjectRevision));

    private sealed class LegacyCompletionFactory : ISessionFlowCompletionFactory
    {
        private readonly Func<SessionKey, IFlowCompletion> create;
        internal LegacyCompletionFactory(Func<SessionKey, IFlowCompletion> create)
        {
            ArgumentNullException.ThrowIfNull(create);
            this.create = create;
        }
        public IFlowCompletion Create(SessionCompletionContext context) => create(context.Session);
    }
}
