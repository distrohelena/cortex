namespace Filomena.Evaluation.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            // No datasource, host principal or credential is bootstrapped from
            // argv/stdin. Standalone persisted mode remains unavailable until a
            // separately configured host composition supplies its binding.
            return await new CommandDispatcher().RunAsync(args, Console.In, Console.Out, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
