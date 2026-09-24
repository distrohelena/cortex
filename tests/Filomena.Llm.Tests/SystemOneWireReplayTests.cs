using System.Text.Json;
using Filomena.Llm.SystemOne;
using Xunit;
using static Filomena.Llm.Tests.SystemOneClientTests;

namespace Filomena.Llm.Tests;

public sealed class SystemOneWireReplayTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_REPLAY_DIR") is not null;

    [Fact(SkipUnless = nameof(Enabled), Skip = "Local replay of explicitly selected synthetic experiment evidence; no network.")]
    public async Task Captured_responses_survive_current_validation_without_modification()
    {
        var files = Directory.GetFiles(Environment.GetEnvironmentVariable("FILOMENA_SYSTEMONE_REPLAY_DIR")!, "*.json");
        Assert.NotEmpty(files);
        var count = 0;
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            foreach (var wire in document.RootElement.GetProperty("wireCalls").EnumerateArray())
            {
                var request = wire.GetProperty("request");
                var response = wire.GetProperty("response");
                using var handler = new Handler(response.GetRawText());
                using var http = new HttpClient(handler);
                var client = new SystemOneClient(http, new("http://localhost/v1/systemone"), () => null, false,
                    SystemOneProbabilityPolicy.Jev113TwoDecimals);
                var result = await client.DecideAsync(request.GetProperty("model").GetString()!,
                    request.GetProperty("state"), request.GetProperty("questions"), TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(response.GetProperty("answers"), result.Answers));
                Assert.Equal(1, handler.Calls);
                count++;
            }
        }
        Assert.True(count > 0);
    }
}
