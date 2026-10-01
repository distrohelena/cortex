using static Cortex.TestSupport.SystemOneTestSupport;
using System.Net;
using System.Text;
using System.Text.Json;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Cortex.Inference.Tests;

public sealed class SystemOneClientTests
{
    [Fact]
    public async Task Uses_systemone_wire_contract_and_preserves_probabilities_and_score_scale()
    {
        using var handler = new Handler(Response);
        using var http = new HttpClient(handler);
        var client = new SystemOneClient(http, new Uri("http://127.0.0.1:8000/v1/systemone"), () => "test-key", true);
        var result = await client.DecideAsync("typed-decisions", Json("{\"text\":\"Hello\"}"), Json(Questions), TestContext.Current.CancellationToken);
        Assert.Equal("/v1/systemone", handler.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal("typed-decisions", handler.Body.GetProperty("model").GetString());
        Assert.Equal("Hello", handler.Body.GetProperty("state").GetProperty("text").GetString());
        Assert.Equal(3, handler.Body.GetProperty("questions").EnumerateObject().Count());
        Assert.Equal(1.5, result.Answers.GetProperty("priority").GetProperty("score").GetDouble());
        Assert.Equal(0.8, result.Answers.GetProperty("action").GetProperty("confidence").GetDouble());
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
    }

    [Theory]
    [InlineData("\"reply\":0.8", "\"reply\":1.8")]
    [InlineData("\"reply\":0.8", "\"reply\":0.1")]
    [InlineData("\"choice\":\"reply\"", "\"choice\":\"invented\"")]
    [InlineData("\"noul\":0.7", "\"noul\":-0.1")]
    [InlineData("\"score\":1.5", "\"score\":10")]
    [InlineData("\"care\":{", "\"wrong\":{")]
    [InlineData("\"input_tokens\":42", "\"input_tokens\":-1")]
    [InlineData("\"type\":\"choice\"", "\"type\":\"score\"")]
    public async Task Invalid_response_never_becomes_a_decision(string before, string after)
    {
        foreach (var policy in Enum.GetValues<SystemOneProbabilityPolicy>())
        {
            using var http = new HttpClient(new Handler(Response.Replace("laya-rl-agent", "jev-1.13.0", StringComparison.Ordinal).Replace(before, after, StringComparison.Ordinal)));
            var client = new SystemOneClient(http, new Uri("http://localhost/v1/systemone"), () => null, false, policy);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync("typed-decisions", Json("{}"), Json(Questions), TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData(SystemOneProbabilityPolicy.LayaFourDecimals)]
    [InlineData(SystemOneProbabilityPolicy.Jev113TwoDecimals)]
    public async Task Selected_choice_must_be_a_highest_probability_option(SystemOneProbabilityPolicy policy)
    {
        using var http = new HttpClient(new Handler(Response.Replace("laya-rl-agent", "jev-1.13.0", StringComparison.Ordinal)
            .Replace("\"choice\":\"reply\"", "\"choice\":\"wait\"", StringComparison.Ordinal)));
        var client = new SystemOneClient(http, new("http://localhost/v1/systemone"), () => null, false, policy);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync("jev-1.13.0", Json("{}"), Json(Questions), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(529)]
    public async Task Http_failure_has_no_fallback_and_does_not_leak_error_body(int status)
    {
        using var handler = new Handler("sensitive-remote-error", (HttpStatusCode)status);
        using var http = new HttpClient(handler);
        var client = new SystemOneClient(http, new Uri("http://localhost/v1/systemone"), () => null, false);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.DecideAsync("typed-decisions", Json("{}"), Json(Questions), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_required_credential_fails_before_network()
    {
        using var handler = new Handler(Response);
        using var http = new HttpClient(handler);
        var client = new SystemOneClient(http, new Uri("https://api.typesafe.ai/v1/systemone"), () => null, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DecideAsync("jev-latest", Json("{}"), Json(Questions), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Captured_jev_point_99_mass_requires_opt_in_and_is_never_normalized(bool compatibility)
    {
        // Exact motivo answer captured from Jev 1.13.0; no conversation content or credential.
        const string response = """
            {"model":"jev-1.13.0","answers":{"motivo":{"type":"choice","choice":"continuidade","confidence":0.63,
              "probabilities":{"conflito_sem_convite":0.0,"sem_contribuicao":0.0,"interesse_no_assunto":0.01,
                "acabou_de_falar":0.0,"oportunidade_social":0.0,"pergunta_direta":0.28,"continuidade":0.67,
                "pensamento_incompleto":0.0,"conversa_em_andamento":0.01,"conversa_migrou":0.0,
                "conversa_entre_outros":0.01,"chamada_direta":0.01}}},"usage":{"input_tokens":3327,"output_tokens":217}}
            """;
        const string questions = """
            {"motivo":{"type":"choice","instructions":"Escolha o motivo.","criteria":{
              "conflito_sem_convite":null,"sem_contribuicao":null,"interesse_no_assunto":null,
              "acabou_de_falar":null,"oportunidade_social":null,"pergunta_direta":null,"continuidade":null,
              "pensamento_incompleto":null,"conversa_em_andamento":null,"conversa_migrou":null,
              "conversa_entre_outros":null,"chamada_direta":null}}}
            """;
        using var handler = new Handler(response);
        using var http = new HttpClient(handler);
        var client = new SystemOneClient(http, new Uri("http://localhost/v1/systemone"), () => null, false,
            compatibility ? SystemOneProbabilityPolicy.Jev113TwoDecimals : SystemOneProbabilityPolicy.LayaFourDecimals);
        if (compatibility)
        {
            var result = await client.DecideAsync("jev-1.13.0", Json("{}"), Json(questions), TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(Json(response).GetProperty("answers"), result.Answers));
            Assert.Equal(0.99, result.Answers.GetProperty("motivo").GetProperty("probabilities").EnumerateObject().Sum(p => p.Value.GetDouble()), 8);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DecideAsync("jev-latest", Json("{}"), Json(questions), TestContext.Current.CancellationToken));
            Assert.Equal("Invalid System 1 decision contract: probability_mass_mismatch.", error.Message);
        }
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(2, 0.78)] // Mass .98 is outside two half-cent rounding intervals.
    [InlineData(255, 0.75)] // Many zero options must not grant a huge error budget.
    public async Task Rounded_policy_does_not_accept_arbitrary_mass(int count, double first)
    {
        var criteria = Enumerable.Range(0, count).ToDictionary(i => "c" + i, i => (string?)null);
        var probabilities = Enumerable.Range(0, count).ToDictionary(i => "c" + i, i => i == 0 ? first : i == 1 ? 0.2 : 0);
        var questions = JsonSerializer.SerializeToElement(new { q = new { type = "choice", instructions = "Choose", criteria } });
        var response = JsonSerializer.Serialize(new { model = "jev-1.13.0", answers = new { q = new { type = "choice", choice = "c0", confidence = 0.5, probabilities } }, usage = new { input_tokens = 5, output_tokens = 1 } });
        using var http = new HttpClient(new Handler(response));
        var client = new SystemOneClient(http, new("http://localhost/v1/systemone"), () => null, false, SystemOneProbabilityPolicy.Jev113TwoDecimals);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DecideAsync("jev-1.13.0", Json("{}"), questions, TestContext.Current.CancellationToken));
    }

}
