using System.Text.Json;

namespace Cortex.Core.Flows.Durable;

/// <summary>Private input artifact, never a Temporal payload.</summary>
public sealed record ExecutionBatchV1(string EntryId, JsonElement Message,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> RuntimeOutputs,
    IReadOnlyList<string> ProviderMessageIds, IReadOnlyList<string> ReplyTargetIds, bool ShadowOnly);
public sealed record FeedbackResponseV1(string OperationId, string RequestId, string RequestDigest,
    string Text, string AuthenticatedActorRef);
