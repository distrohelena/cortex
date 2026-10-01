using Cortex.Evaluation.Contracts;
using System.Text.Json.Serialization;

namespace Cortex.Evaluation.Execution;

public sealed record MonetaryBudgetApprovalRequest(
    [property: JsonRequired] int ContractVersion,
    [property: JsonRequired] Guid ApprovalId,
    [property: JsonRequired] Guid ExperimentId,
    [property: JsonRequired] string PlanSha256,
    [property: JsonRequired] string Currency,
    [property: JsonRequired] decimal MaximumCost);
public sealed record UnstartedMonetaryPlan(EvaluationScope Scope, Guid ExperimentId, string PlanSha256,
    string ExecutionState, long ExistingAttempts, BudgetConsumption Reservations, decimal MaximumCost);
public sealed record MonetaryBudgetApproval(Guid ApprovalId, EvaluationScope Scope, Guid ExperimentId,
    string PlanSha256, string Currency, decimal MaximumCost, string ApprovedBy);

public static class MonetaryBudgetContract
{
    public static void DemandForAttempt(MonetaryBudgetApproval? approval, EvaluationScope scope,
        Guid experiment, string planSha256, string? currency, decimal maximumCost)
    {
        // The durable caller loads this declaration under the experiment lock.
        // It remains an additional prerequisite, not a replacement for admission,
        // cancellation, lease fencing, current host authority or available budget.
        if (approval is null || approval.ApprovalId == Guid.Empty || approval.Scope != scope ||
            approval.ExperimentId != experiment || experiment == Guid.Empty || !IsHash(planSha256) ||
            approval.PlanSha256 != planSha256 || currency is not { Length: 3 } ||
            currency.Any(c => c is < 'A' or > 'Z') || approval.Currency != currency ||
            maximumCost <= 0 || approval.MaximumCost != maximumCost || string.IsNullOrWhiteSpace(approval.ApprovedBy))
            throw new InvalidOperationException("An exact stored monetary approval and explicit matching reservation currency are required.");
    }

    public static void Validate(MonetaryBudgetApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContractVersion != 1 || request.ApprovalId == Guid.Empty || request.ExperimentId == Guid.Empty ||
            !IsHash(request.PlanSha256) || request.Currency is not { Length: 3 } ||
            request.Currency.Any(c => c is < 'A' or > 'Z') || request.MaximumCost < 0)
            throw new InvalidDataException("Invalid explicit monetary approval contract.");
    }

    // Pure validation only. The host must resolve and lock the stored plan and
    // attempt/reservation state in the approval transaction before using this.
    // Neither this record nor caller-supplied plan data grants execution authority.
    public static MonetaryBudgetApproval Approve(EvaluationAccess access, MonetaryBudgetApprovalRequest request,
        UnstartedMonetaryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.Demand(EvaluationOperation.Operate);
        Validate(request);
        ArgumentNullException.ThrowIfNull(plan);
        access.DemandScope(plan.Scope);
        if (plan.ExperimentId != request.ExperimentId || plan.PlanSha256 != request.PlanSha256 ||
            plan.ExecutionState != "planned" || plan.ExistingAttempts != 0 || plan.Reservations is null ||
            plan.Reservations.Calls != 0 || plan.Reservations.InputTokens != 0 ||
            plan.Reservations.OutputTokens != 0 || plan.Reservations.Cost != 0 || plan.MaximumCost != request.MaximumCost)
            throw new InvalidOperationException("Monetary approval requires the exact unstarted, unreserved plan and unchanged ceiling.");

        return new(request.ApprovalId, access.Scope, request.ExperimentId, request.PlanSha256,
            request.Currency, request.MaximumCost, access.Principal);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
