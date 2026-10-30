using System.Text;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ProviderObservationTests
{
    [Fact]
    public void Responses_usage_preserves_total_output_and_reasoning_as_subset()
    {
        var result = Parse("openai", """
            {"id":"resp_test","model":"gpt-6-luna","status":"completed",
             "usage":{"input_tokens":100,"output_tokens":40,
               "input_tokens_details":{"cached_tokens":20},
               "output_tokens_details":{"reasoning_tokens":30}}}
            """);
        Assert.Equal("available", result.UsageStatus);
        Assert.Equal("gpt-6-luna", result.Model);
        Assert.Equal("resp_test", result.ResponseId);
        Assert.Equal("completed", result.ProviderStatus);
        Assert.Equal(new ProviderTokenUsage(100, 40, 20, 30), result.Usage);
    }

    [Theory]
    [InlineData("lmstudio")]
    [InlineData("openjev")]
    public void Chat_usage_uses_prompt_completion_names_without_inventing_missing_details(string provider)
    {
        var result = Parse(provider, """{"model":"google/gemma-4-e4b","usage":{"prompt_tokens":12,"completion_tokens":7}}""");
        Assert.Equal(new ProviderTokenUsage(12, 7, null, null), result.Usage);
        Assert.Equal("available", result.UsageStatus);
    }

    [Theory]
    [InlineData("jev")]
    [InlineData("laya")]
    public void System_one_zero_output_is_observed_not_assumed(string provider)
    {
        var result = Parse(provider, """{"model":"model-test","usage":{"input_tokens":25,"output_tokens":0}}""");
        Assert.Equal(new ProviderTokenUsage(25, 0, null, null), result.Usage);
        Assert.Equal("available", result.UsageStatus);
    }

    [Theory]
    [InlineData("{}", "usage_missing")]
    [InlineData("{\"usage\":null}", "usage_missing")]
    [InlineData("{\"usage\":{\"input_tokens\":-1,\"output_tokens\":2}}", "usage_invalid")]
    [InlineData("{\"usage\":{\"input_tokens\":1.5,\"output_tokens\":2}}", "usage_invalid")]
    [InlineData("{\"usage\":{\"input_tokens\":1,\"input_tokens\":2,\"output_tokens\":3}}", "response_ambiguous")]
    [InlineData("not-json-sensitive-content", "response_invalid")]
    public void Missing_or_invalid_usage_never_turns_into_zero(string body, string diagnostic)
    {
        var result = Parse("openai", body);
        Assert.Equal("unavailable", result.UsageStatus);
        Assert.Null(result.Usage);
        Assert.Equal(diagnostic, result.Diagnostic);
        Assert.DoesNotContain("sensitive-content", result.ToString());
    }

    [Fact]
    public void Partial_usage_remains_partial_and_does_not_assume_zero_input()
    {
        var result = Parse("openai", """{"usage":{"output_tokens":4}}""");
        Assert.Equal("partial", result.UsageStatus);
        Assert.Equal(new ProviderTokenUsage(null, 4, null, null), result.Usage);
    }

    [Fact]
    public void Http_error_and_incomplete_are_not_misrepresented_as_completion()
    {
        var result = ProviderObservationParser.Parse("openai", 429, Encoding.UTF8.GetBytes("""{"status":"incomplete","error":{"message":"private-text"}}"""));
        Assert.Equal(429, result.HttpStatus);
        Assert.False(result.HttpSucceeded);
        Assert.Equal("incomplete", result.ProviderStatus);
        Assert.DoesNotContain("private-text", result.ToString());
    }

    [Fact]
    public void Oversized_body_is_not_parsed_or_retained()
    {
        var result = ProviderObservationParser.Parse("openai", 200, new byte[1_048_577]);
        Assert.Equal("body_too_large", result.Diagnostic);
        Assert.Null(result.Usage);
    }

    [Theory]
    [InlineData("{\"usage\":{\"input_tokens\":2,\"output_tokens\":1,\"input_tokens_details\":{\"cached_tokens\":3}}}")]
    [InlineData("{\"usage\":{\"input_tokens\":2,\"output_tokens\":1,\"output_tokens_details\":{\"reasoning_tokens\":3}}}")]
    public void Inconsistent_subsets_are_not_costable(string body)
    {
        var result = Parse("openai", body);
        Assert.Null(result.Usage);
        Assert.Equal("usage_invalid", result.Diagnostic);
    }

    [Fact]
    public void Unknown_provider_does_not_guess_a_wire_contract()
    {
        var result = Parse("future-provider", """{"usage":{"input_tokens":25,"output_tokens":0}}""");
        Assert.Null(result.Usage);
        Assert.Equal("provider_unsupported", result.Diagnostic);
    }

    [Fact]
    public void Service_tier_is_observed_for_pricing_and_missing_is_not_defaulted()
    {
        Assert.Equal("priority", Parse("openai", """{"service_tier":"priority","usage":{"input_tokens":1,"output_tokens":1}}""").ServiceTier);
        Assert.Null(Parse("openai", """{"usage":{"input_tokens":1,"output_tokens":1}}""").ServiceTier);
    }

    private static ProviderObservation Parse(string provider, string body) =>
        ProviderObservationParser.Parse(provider, 200, Encoding.UTF8.GetBytes(body));
}
