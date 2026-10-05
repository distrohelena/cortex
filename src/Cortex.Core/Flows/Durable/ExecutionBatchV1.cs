using System.Text.Json;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

/// <summary>Private input artifact, never a Temporal payload.</summary>
public sealed record ExecutionBatchV1(string EntryId, JsonElement Message,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> RuntimeOutputs,
    IReadOnlyList<string> ProviderMessageIds, IReadOnlyList<string> ReplyTargetIds, bool ShadowOnly)
{
    public SessionExecutionPurpose Purpose { get; init; }
    public SessionSnapshot? ReviewSnapshot { get; init; }
    public ParticipationReviewDefinition? ReviewDefinition { get; init; }
    public ArtifactRef? ReviewRef { get; init; }
    public DateTimeOffset OriginalTimestamp { get; init; }
    public DateTimeOffset ContextCapturedAt { get; init; }
    public IReadOnlyList<ParticipationReassessmentContext> InitialContexts { get; init; } = [];
}
public sealed record FeedbackResponseV1(string OperationId, string RequestId, string RequestDigest,
    string Text, string AuthenticatedActorRef);
