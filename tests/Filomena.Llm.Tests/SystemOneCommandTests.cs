using Filomena.App;
using Xunit;
using static Filomena.Llm.Tests.SystemOneClientTests;

namespace Filomena.Llm.Tests;

public sealed class SystemOneCommandTests
{
    [Fact]
    public async Task Command_returns_typed_decision_and_never_constructs_a_conversational_host()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "{\"state\":{\"text\":\"hello\"},\"questions\":" + Questions + "}", TestContext.Current.CancellationToken);
            using var http = new HttpClient(new Handler(Response));
            using var output = new StringWriter();
            var code = await SystemOneCommand.RunAsync(["--system-one", "--provider", "laya", "--model", "multilingual", "--input", file],
                output, TestContext.Current.CancellationToken, http, _ => null);
            Assert.Equal(0, code);
            var result = Json(output.ToString());
            Assert.Equal("reply", result.GetProperty("Answers").GetProperty("action").GetProperty("choice").GetString());
            Assert.Equal(42, result.GetProperty("InputTokens").GetInt32());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Missing_arguments_do_not_call_service()
    {
        using var handler = new Handler(Response);
        using var http = new HttpClient(handler);
        using var output = new StringWriter();
        Assert.Equal(2, await SystemOneCommand.RunAsync(["--system-one"], output, TestContext.Current.CancellationToken, http, _ => null));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(null, "jev-1.13.0", "0.79", 2)]
    [InlineData("jev-1.13-two-decimal-v1", "jev-1.13.0", "0.79", 0)]
    [InlineData("jev-1.13-two-decimal-v1", "jev-1.13.0", "0.81", 0)]
    [InlineData("jev-1.13-two-decimal-v1", "jev-1.13.0", "0.77", 2)]
    [InlineData("jev-1.13-two-decimal-v1", "jev-1.13.0", "0.791", 2)]
    [InlineData("jev-1.13-two-decimal-v1", "jev-2.0.0", "0.79", 2)]
    [InlineData("unknown-profile", "jev-1.13.0", "0.8", 2)]
    public async Task Jev_rounded_mass_requires_explicit_version_bound_policy(string? policy, string returnedModel, string probability, int expectedExit)
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "{\"state\":{},\"questions\":" + Questions + "}", TestContext.Current.CancellationToken);
            var response = Response.Replace("laya-rl-agent", returnedModel, StringComparison.Ordinal)
                .Replace("\"reply\":0.8", "\"reply\":" + probability, StringComparison.Ordinal);
            using var handler = new Handler(response);
            using var http = new HttpClient(handler);
            using var output = new StringWriter();
            string? EnvironmentValue(string name) => name switch
            {
                "TYPESAFE_API_KEY" => "synthetic-test-credential",
                "FILOMENA_JEV_PROBABILITY_POLICY" => policy,
                _ => null
            };
            var code = await SystemOneCommand.RunAsync(["--system-one", "--provider", "jev", "--model", "jev-1.13.0", "--input", file],
                output, TestContext.Current.CancellationToken, http, EnvironmentValue);
            Assert.Equal(expectedExit, code);
            if (expectedExit == 0)
            {
                var result = Json(output.ToString());
                Assert.Equal(double.Parse(probability, System.Globalization.CultureInfo.InvariantCulture),
                    result.GetProperty("Answers").GetProperty("action").GetProperty("probabilities").GetProperty("reply").GetDouble());
                Assert.Equal("Jev113TwoDecimals", result.GetProperty("ProbabilityPolicy").GetString());
                Assert.Equal(1, handler.Calls);
            }
            if (policy == "unknown-profile") Assert.Equal(0, handler.Calls);
        }
        finally { File.Delete(file); }
    }
}
