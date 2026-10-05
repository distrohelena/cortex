using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;

namespace Cortex.Core.Flows.Durable;

/// <summary>Host-only services and current authority; never a Workflow payload or persisted grant.</summary>
public sealed record ExecutionHostBinding(ArtifactScope Scope, JsonElement Project,
    ISessionFlowCompletionFactory CompletionFactory, IMemoryReader MemoryReader,
    ShadowOutboundHost? OutboundHost, TimeProvider Clock)
{
    public IReadOnlyDictionary<StepEffect, TimeSpan> StartToCloseTimeouts { get; init; } = new Dictionary<StepEffect, TimeSpan>();
    public Func<SessionInstructionOutput, CancellationToken, Task>? InstructionOutput { get; init; }
    public Func<SessionNodeVisit, CancellationToken, Task>? NodeStarting { get; init; }
    public Func<FeedbackSubmission, CancellationToken, Task>? AuthorizeFeedbackAsync { get; init; }
    public Action? ValidateCurrentContext { get; init; }
    public Func<SessionSnapshot, CancellationToken, Task>? ValidateReviewContextAsync { get; init; }
    public bool ShadowOnly { get; init; }
    public Func<ParticipationReassessmentContext, CancellationToken, Task<ParticipationReassessment>>? AnalyzeInitialAsync { get; init; }
}
