using Filomena.Core.Memory;

namespace Filomena.Core.Flows.Sessions;

public sealed partial class SessionFlowRuntime : IAsyncDisposable
{
    private readonly object administrationGate = new();
    private readonly TimeProvider timeProvider;
    private readonly Func<SessionKey, IFlowCompletion> completionFactory;
    private readonly IMemoryReader memoryReader;
    private CompiledSessionFlowProject? project;

    public SessionFlowRuntime(TimeProvider timeProvider, IFlowCompletion completion, IMemoryReader? memoryReader = null)
        : this(timeProvider, ConstantCompletion(completion), memoryReader)
    {
    }

    public SessionFlowRuntime(TimeProvider timeProvider, Func<SessionKey, IFlowCompletion> completionFactory, IMemoryReader? memoryReader = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(completionFactory);
        this.timeProvider = timeProvider;
        this.completionFactory = completionFactory;
        this.memoryReader = memoryReader ?? new UnavailableMemoryReader();
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
}
