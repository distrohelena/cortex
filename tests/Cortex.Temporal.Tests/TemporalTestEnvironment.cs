using Temporalio.Testing;

namespace Cortex.Temporal.Tests;

internal static class TemporalTestEnvironment
{
    public static Task<WorkflowEnvironment> StartAsync(bool timeSkipping = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-temporal-test-server");
        Directory.CreateDirectory(directory);
        return timeSkipping ? WorkflowEnvironment.StartTimeSkippingAsync(new() { DownloadDirectory = directory })
            : WorkflowEnvironment.StartLocalAsync(new() { DownloadDirectory = directory });
    }
}
