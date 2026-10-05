using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Outbound;

internal sealed record OutboundExecutionEvidence(int Version, string ProjectFingerprint, string RolesFingerprint,
    string ExecutionId, SessionKey Session, SessionEntryKind Kind, IReadOnlyList<string> InboundIds,
    IReadOnlyList<string> ReplyTargetIds, bool ShadowOnly, PortRef? Recall, PortRef? Compiled,
    PortRef? Current, PortRef? Approved, string? EnforcementRevision, int Attempts,
    string? AttemptRef, string? ProviderMessageId, bool ReplySelectionValid, string? ReplyToMessageId,
    string? GenerationRef, string? BoundarySetRef, string ReceiptAccount);
