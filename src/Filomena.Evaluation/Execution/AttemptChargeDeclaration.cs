using Filomena.Evaluation.Contracts;
using System.Text.Json.Serialization;

namespace Filomena.Evaluation.Execution;

// An operator attestation referencing billing evidence, not an estimate and not
// proof that the provider never executed a call. No reservation is released here.
public sealed record AttemptChargeDeclaration(
    [property: JsonRequired] int ContractVersion,
    [property: JsonRequired] Guid DecisionId,
    [property: JsonRequired] Guid ExperimentId,
    [property: JsonRequired] Guid TrialId,
    [property: JsonRequired] Guid AttemptId,
    [property: JsonRequired] Guid AttemptOwner,
    [property: JsonRequired] long AttemptGeneration,
    [property: JsonRequired] Guid ApprovalId,
    [property: JsonRequired] string Currency,
    [property: JsonRequired] decimal Charge,
    [property: JsonRequired] EvidenceArtifactRef Evidence);

public sealed record ChargeAttemptBinding(Guid ExperimentId, Guid TrialId, Guid AttemptId, Guid Owner,
    long Generation, Guid? ApprovalId, string? Currency, string State, decimal ReservedCost)
{
    // Derived from persisted fencing/cancellation by the trusted store, never request JSON.
    public bool ExecutionQuarantined { get; init; }
}

public sealed record ChargeDeclarationEffect(decimal RetainedExposure, bool ExceedsReservation)
{
    public string Origin => "operator_billing_attestation";
    public bool ReleasesReservation => false;
    public bool ResolvesExecution => false;
}

public static class AttemptChargePolicy
{
    public static void Validate(AttemptChargeDeclaration request)
    {
        if (request is null || request.ContractVersion != 1 || request.DecisionId == Guid.Empty ||
            request.ExperimentId == Guid.Empty || request.TrialId == Guid.Empty || request.AttemptId == Guid.Empty ||
            request.AttemptOwner == Guid.Empty || request.AttemptGeneration < 1 || request.ApprovalId == Guid.Empty ||
            request.Currency is not { Length: 3 } || request.Currency.Any(c => c is < 'A' or > 'Z') ||
            request.Charge < 0 || request.Evidence is null || request.Evidence.Id == Guid.Empty ||
            request.Evidence.Sha256 is not { Length: 64 } hash || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid explicit billing declaration.");
    }

    public static ChargeDeclarationEffect Evaluate(AttemptChargeDeclaration request, ChargeAttemptBinding binding)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ExperimentId != request.ExperimentId || binding.TrialId != request.TrialId ||
            binding.AttemptId != request.AttemptId || binding.Owner != request.AttemptOwner ||
            binding.Generation != request.AttemptGeneration || binding.ApprovalId != request.ApprovalId ||
            binding.Currency != request.Currency || binding.ReservedCost < 0 ||
            !(binding.State is "observed" or "indeterminate" || binding.State == "started" && binding.ExecutionQuarantined))
            throw new InvalidOperationException("Billing evidence requires the exact attempt binding and non-active or fenced execution.");
        return new(Math.Max(binding.ReservedCost, request.Charge), request.Charge > binding.ReservedCost);
    }
}
