using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Execution;

public sealed record RecoveryQuarantineRequest(Guid ExperimentId, Guid TrialId, Guid ExpectedOwner,
    long ExpectedGeneration, Guid DecisionId, EvidenceArtifactRef Evidence);
public sealed record RecoveryQuarantineReceipt(Guid DecisionId, Guid TrialId, long FencedGeneration, bool AlreadyRecorded);

public static class RecoveryQuarantine
{
    public static void Validate(RecoveryQuarantineRequest request)
    {
        if (request is null || request.ExperimentId == Guid.Empty || request.TrialId == Guid.Empty ||
            request.ExpectedOwner == Guid.Empty || request.ExpectedGeneration is < 1 or long.MaxValue ||
            request.DecisionId == Guid.Empty || request.Evidence is null || request.Evidence.Id == Guid.Empty ||
            request.Evidence.Sha256 is not { Length: 64 } hash || hash.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid recovery quarantine binding.");
    }

    public static long NextGeneration(string experimentState, string trialState, Guid? actualOwner,
        long actualGeneration, Guid expectedOwner, long expectedGeneration)
    {
        if (experimentState is not ("running" or "cancelled" or "indeterminate") ||
            trialState is not ("running" or "indeterminate") || expectedOwner == Guid.Empty ||
            expectedGeneration is < 1 or long.MaxValue || actualOwner != expectedOwner || actualGeneration != expectedGeneration)
            throw new InvalidOperationException("Recovery requires the current started owner and generation.");
        return expectedGeneration + 1;
    }
}
