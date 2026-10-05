using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Inference;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;

namespace Cortex.Temporal.Hosting;

public sealed record HostExecutionSessionBinding(string InstallationId, SessionKey Session, SessionFlowProject Project,
    ISessionFlowCompletionFactory CompletionFactory, IMemoryReader MemoryReader, ShadowOutboundHost? OutboundHost, TimeProvider Clock)
{
    public Func<FeedbackSubmission, CancellationToken, Task>? AuthorizeFeedbackAsync { get; init; }
    public Action? ValidateCurrentContext { get; init; }
    public Func<SessionInstructionOutput, CancellationToken, Task>? InstructionOutput { get; init; }
    public Func<SessionNodeVisit, CancellationToken, Task>? NodeStarting { get; init; }
}

public sealed class HostBoundExecutionBindings : IExecutionHostBindings
{
    private readonly Dictionary<(string Installation, SessionKey Session, string Revision), HostExecutionSessionBinding> bindings = [];
    private readonly object gate = new();
    private readonly bool allowHarnessCompletions;
    public HostBoundExecutionBindings(IReadOnlyList<HostExecutionSessionBinding> bindings, bool allowHarnessCompletions = false)
    {
        this.allowHarnessCompletions = allowHarnessCompletions;
        foreach (var binding in bindings) Register(binding);
    }
    public void Register(HostExecutionSessionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding.MemoryReader); ArgumentNullException.ThrowIfNull(binding.Clock);
        _ = DurableIdentity.SessionWorkflowId(binding.InstallationId, binding.Session);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Project.ProjectRevision);
        if (!allowHarnessCompletions && binding.CompletionFactory is not HostBoundSessionCompletionFactory)
            throw new ArgumentException("Production requires host-bound inference; harness completions need explicit opt-in.");
        lock (gate)
            if (!bindings.TryAdd((binding.InstallationId, binding.Session, binding.Project.ProjectRevision), binding))
                throw new ArgumentException("Duplicate host session/revision binding.");
    }
    public ValueTask<ExecutionHostBinding> ResolveAsync(ArtifactScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        HostExecutionSessionBinding binding;
        lock (gate)
            if (!bindings.TryGetValue((scope.InstallationId, scope.Session, scope.ProjectRevision), out binding!))
                throw new UnauthorizedAccessException("Execution has no exact host session/revision binding.");
        return ValueTask.FromResult(new ExecutionHostBinding(scope, binding.Project.Document.Clone(), binding.CompletionFactory, binding.MemoryReader, binding.OutboundHost, binding.Clock)
        { AuthorizeFeedbackAsync = binding.AuthorizeFeedbackAsync, ValidateCurrentContext = binding.ValidateCurrentContext,
            InstructionOutput = binding.InstructionOutput, NodeStarting = binding.NodeStarting });
    }
}
