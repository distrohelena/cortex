namespace Cortex.Core.Flows.Durable;

public sealed record FeedbackRequestV1(StepKey Key, string RequestId, string Question, string? Context, string RequestDigest);
public sealed record FeedbackResolution(FeedbackDescriptor Request, string Status, ArtifactRef? ResponseRef);
public sealed record FeedbackReceipt(string OperationId, string RequestId, string Status, string? Diagnostic);
