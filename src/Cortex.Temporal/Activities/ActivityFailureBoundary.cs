using Temporalio.Activities;
using Temporalio.Exceptions;

namespace Cortex.Temporal.Activities;

internal static class ActivityFailureBoundary
{
    internal static async Task<T> RunAsync<T>(Func<Task<T>> run, string code)
    {
        try { return await run().ConfigureAwait(false); }
        catch (OperationCanceledException) when (ActivityExecutionContext.Current.CancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(ActivityExecutionContext.Current.CancellationToken); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Provider, store and authorization exceptions may embed prompts, credentials or private text.
            // Never attach the original exception as inner failure/details in Temporal history.
            throw new ApplicationFailureException(code, nonRetryable: error is not IOException);
        }
    }
    internal static Task RunAsync(Func<Task> run, string code) => RunAsync(async () => { await run().ConfigureAwait(false); return true; }, code);
}
