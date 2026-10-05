using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

public sealed record LedgerAdmissionRequest(string InstallationId, SessionKey Session, string InputKey,
    ArtifactRef InputRef, string Digest, string BatchId, string ExecutionId, long ProposedWatermark);
public sealed record LedgerAdmission(SessionAdmissionStatus Status, long Watermark, string BatchId,
    string ExecutionId, string InputKey, string Digest, ArtifactRef InputRef);
public sealed record LedgerRecord(string InstallationId, SessionKey Session, string Collection, string OperationId, ArtifactRef Reference);
public interface ISessionLedger
{
    ValueTask<LedgerAdmission> AdmitAsync(LedgerAdmissionRequest request, CancellationToken token);
    ValueTask<LedgerAdmission?> FindAdmissionAsync(string installationId, SessionKey session, string inputKey, CancellationToken token);
    ValueTask<ArtifactPage<ArtifactRef>> ReadPageAsync(string installationId, SessionKey session, string collection, PageRequest page, CancellationToken token);
    ValueTask RecordOnceAsync(LedgerRecord record, CancellationToken token);
}
