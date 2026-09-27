using Filomena.Evaluation.Contracts;

namespace Filomena.Evaluation.Execution;

public sealed record CallReservation(long InputTokens, long OutputTokens, decimal? Cost);
public sealed record BudgetConsumption(long Calls, long InputTokens, long OutputTokens, decimal Cost);

public static class BudgetReservation
{
    public static BudgetConsumption Add(ExperimentBudgets limit, BudgetConsumption consumed, CallReservation reservation)
    {
        if (limit.MaxCalls <= 0 || limit.MaxInputTokens <= 0 || limit.MaxOutputTokens <= 0 || limit.MaxCost <= 0 ||
            consumed.Calls < 0 || consumed.InputTokens < 0 || consumed.OutputTokens < 0 || consumed.Cost < 0 ||
            reservation.InputTokens < 0 || reservation.OutputTokens < 0 || reservation.Cost is null or < 0)
            throw new InvalidDataException("Invalid or unknown reservation bound.");
        try
        {
            var next = new BudgetConsumption(checked(consumed.Calls + 1), checked(consumed.InputTokens + reservation.InputTokens),
                checked(consumed.OutputTokens + reservation.OutputTokens), checked(consumed.Cost + reservation.Cost.Value));
            if (next.Calls > limit.MaxCalls || next.InputTokens > limit.MaxInputTokens ||
                next.OutputTokens > limit.MaxOutputTokens || next.Cost > limit.MaxCost)
                throw new InvalidOperationException("Evaluation budget exhausted.");
            return next;
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Evaluation budget overflow.");
        }
    }
}
