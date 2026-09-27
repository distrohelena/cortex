using Filomena.Evaluation.Tools;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class AssessmentToolTests
{
    [Fact]
    public void Preview_reports_failure_without_persisting_or_executing()
    {
        var result = OfflineEvaluationTools.Invoke("""
            {"contractVersion":1,"operation":"assessment.preview","payload":{
              "output":"{\"acao\":\"responder\"}",
              "criteria":[{"id":"action","field":"acao","acceptedValues":["calar"],"critical":true}],
              "manualCriteria":[]}}
            """);
        Assert.True(result.Success);
        Assert.False(result.Persisted);
        Assert.Equal("fail", result.Result!.Value.GetProperty("outcome").GetString());
        Assert.True(result.Result.Value.GetProperty("criteria")[0].GetProperty("criticalFailure").GetBoolean());
    }

    [Theory]
    [InlineData("null", "[]", "[]")]
    [InlineData("\"{}\"", "null", "[]")]
    [InlineData("\"{}\"", "[null]", "[]")]
    [InlineData("\"{}\"", "[]", "null")]
    [InlineData("\"{}\"", "[{\"id\":\"x\",\"field\":\"x\",\"acceptedValues\":null,\"critical\":true}]", "[]")]
    public void Malformed_input_returns_safe_error(string output, string criteria, string manual)
    {
        var result = OfflineEvaluationTools.Invoke($$$"""
            {"contractVersion":1,"operation":"assessment.preview","payload":{"output":{{{output}}},"criteria":{{{criteria}}},"manualCriteria":{{{manual}}}}}
            """);
        Assert.False(result.Success);
        Assert.Equal("invalid_request", result.ErrorCode);
    }
}
