using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeShadowCommandTests
{
    [Fact]
    public async Task Missing_host_document_fails_before_any_service_or_execution()
    {
        using var output = new StringWriter();
        Assert.Equal(2, await NativeShadowCommand.RunAsync(["--native-shadow"], new StringReader("/poll"), output, TestContext.Current.CancellationToken));
        Assert.Contains("invalid_host_configuration", output.ToString());
        Assert.DoesNotContain("run_started", output.ToString());
    }

    [Fact]
    public async Task Project_shaped_authority_cannot_enable_a_native_run()
    {
        var directory = Directory.CreateTempSubdirectory("native-shadow-command-");
        try
        {
            var file = Path.Combine(directory.FullName, "host.json");
            await File.WriteAllTextAsync(file, """{"project":{},"outboundMode":"enabled"}""", TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            Assert.Equal(2, await NativeShadowCommand.RunAsync(["--native-shadow", file], new StringReader("/poll"), output, TestContext.Current.CancellationToken));
            Assert.Contains("invalid_host_configuration", output.ToString());
        }
        finally { directory.Delete(true); }
    }
}
