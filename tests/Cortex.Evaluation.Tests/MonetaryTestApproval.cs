using Cortex.Evaluation.Contracts;
using Cortex.Evaluation.Execution;
using Npgsql;

namespace Cortex.Evaluation.Tests;

internal static class MonetaryTestApproval
{
    // Test fixture only. Operational code must resolve a real host operator.
    internal static Task<MonetaryBudgetReceipt> GrantAsync(NpgsqlDataSource source, EvaluationScope scope,
        ExperimentPlanReceipt plan, decimal ceiling, CancellationToken token) =>
        new MonetaryBudgetStore(source).ApproveAsync(EvaluationAccess.FromHost("fixture-operator", scope, EvaluationRole.Operator),
            new(1, Guid.NewGuid(), plan.ExperimentId, plan.PlanSha256, "BRL", ceiling), token);
}
