using System.Net;
using System.Text.Json;
using Filomena.Evaluation.Contracts;
using Filomena.Evaluation.Execution;
using Xunit;

namespace Filomena.Evaluation.Tests;

public sealed class CompletionEvaluationTargetTests
{
    [Fact]
    public void Local_adapter_cannot_remove_host_http_deadline()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(7) };
        var settings = Profile();
        var local = new ConfigurationProfile(1, "completion", "lmstudio", "controlled-local", "local-host",
            "controlled-local", DateTimeOffset.UnixEpoch, Json("""
            {"arbitrationTemperature":0.1,"arbitrationMaxTokens":256,"generationTemperature":0.7,"generationMaxTokens":256,"requestTimeoutSeconds":60}
            """), null, [], settings.Schema, settings.Prompt, null);
        var factory = new EvaluationProviderFactory([
            new("local-host", "lmstudio", new("http://127.0.0.1:1234/v1/"), http, () => null)]);
        Assert.NotNull(factory.Create(local, Access()));
        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Pinned_typed_input_executes_once_through_real_Jev_adapter()
    {
        const string response = """
            {"model":"jev-1.13.0","answers":{"acao":{"type":"choice","choice":"calar","confidence":0.9,"probabilities":{"responder":0.1,"calar":0.9}}},"usage":{"input_tokens":20,"output_tokens":1}}
            """;
        using var transport = new Transport(response: response);
        using var http = new HttpClient(transport);
        var target = new FlowCompletionEvaluationTarget(new([
            new("jev-host", "jev", new("https://api.typesafe.ai/v1/systemone"), http, () => "test-only")]));
        var profile = new ConfigurationProfile(1, "completion", "jev", "jev-1.13.0", "jev-host", "jev-1.13.0",
            DateTimeOffset.UnixEpoch, Json("{}"), 0.5, [], new(1, "schema", Json("""
                {"contractVersion":1,"responseSchema":{"type":"object","additionalProperties":false,"properties":{"acao":{"type":"string","enum":["responder","calar"]}},"required":["acao"]}}
                """)), new(1, "prompt", Json("""{"system":"Avalie participação."}""")), null);
        var access = Access();
        var result = await target.ExecuteAsync(access, Pin(access.Scope, profile), profile, Input(access.Scope),
            TestContext.Current.CancellationToken);
        Assert.Equal("calar", Json(result.Output).GetProperty("acao").GetString());
        Assert.Equal(1, transport.Calls);
        Assert.True(Json(transport.Body).GetProperty("questions").TryGetProperty("acao", out _));
        Assert.Contains("Pergunta de teste", transport.Body);
    }

    [Fact]
    public async Task Proposer_cannot_execute_even_with_valid_hashes()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var access = EvaluationAccess.FromHost("proposer", new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Proposer);
        var profile = Profile();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Target(http).ExecuteAsync(access,
            Pin(access.Scope, profile), profile, Input(access.Scope), TestContext.Current.CancellationToken));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Cancelled_case_never_reaches_transport()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var access = Access();
        var profile = Profile();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Target(http).ExecuteAsync(access,
            Pin(access.Scope, profile), profile, Input(access.Scope), cancellation.Token));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Pinned_input_executes_once_through_real_Luna_adapter()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var access = Access();
        var profile = Profile();
        var input = Input(access.Scope);
        var result = await Target(http).ExecuteAsync(access, Pin(access.Scope, profile), profile, input,
            TestContext.Current.CancellationToken);
        Assert.Equal("Resposta controlada.", result.Output);
        Assert.Equal(input.InputRevision.Sha256, result.InputSha256);
        Assert.Equal(RevisionHasher.Hash(profile.ToRevisionDocument()), result.ProfileSha256);
        Assert.True(result.Elapsed >= TimeSpan.Zero);
        Assert.Equal(1, transport.Calls);
        Assert.Contains("Pergunta de teste", transport.Body);
        Assert.Contains("Instrução pinada", Json(transport.Body).GetProperty("instructions").GetString());
    }

    [Theory]
    [InlineData("profile-hash")]
    [InlineData("input-hash")]
    [InlineData("profile-scope")]
    [InlineData("input-scope")]
    [InlineData("input-kind")]
    [InlineData("input-version")]
    public async Task Invalid_binding_is_rejected_before_transport(string mutation)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var access = Access();
        var profile = Profile();
        var pin = Pin(access.Scope, profile);
        var input = Input(access.Scope);
        switch (mutation)
        {
            case "profile-hash": pin = pin with { Revision = pin.Revision with { Sha256 = new string('0', 64) } }; break;
            case "input-hash": input = input with { InputRevision = input.InputRevision with { Sha256 = new string('0', 64) } }; break;
            case "profile-scope": pin = pin with { Scope = new(Guid.NewGuid(), Guid.NewGuid()) }; break;
            case "input-scope": input = input with { Scope = new(Guid.NewGuid(), Guid.NewGuid()) }; break;
            default:
                var document = new RevisionDocument(mutation == "input-version" ? 2 : 1,
                    mutation == "input-kind" ? "rubric" : "completion-input", input.Input.Payload);
                input = input with { Input = document, InputRevision = new(Guid.NewGuid(),
                    mutation == "input-version" ? input.InputRevision.Sha256 : RevisionHasher.Hash(document)) };
                break;
        }
        var error = await Record.ExceptionAsync(() => Target(http).ExecuteAsync(access, pin, profile, input,
            TestContext.Current.CancellationToken));
        Assert.True(error is InvalidDataException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Outage_is_not_empty_success_and_is_not_retried()
    {
        using var transport = new Transport(HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(transport);
        var access = Access();
        var profile = Profile();
        await Assert.ThrowsAsync<HttpRequestException>(() => Target(http).ExecuteAsync(access,
            Pin(access.Scope, profile), profile, Input(access.Scope), TestContext.Current.CancellationToken));
        Assert.Equal(1, transport.Calls);
    }

    private static EvaluationAccess Access() => EvaluationAccess.FromHost("evaluator",
        new(Guid.NewGuid(), Guid.NewGuid()), EvaluationRole.Evaluator);
    private static FlowCompletionEvaluationTarget Target(HttpClient http) => new(new([
        new("luna-host", "openai", new("https://api.openai.com/v1/responses"), http, () => "test-only")]));
    private static ScopedRevisionRef Pin(EvaluationScope scope, ConfigurationProfile profile) =>
        new(scope, new(Guid.NewGuid(), RevisionHasher.Hash(profile.ToRevisionDocument())));
    private static CompletionEvaluationCase Input(EvaluationScope scope)
    {
        var document = new RevisionDocument(1, "completion-input", Json("""{"mensagem":"Pergunta de teste"}"""));
        return new(scope, "step-under-evaluation", new(Guid.NewGuid(), RevisionHasher.Hash(document)), document);
    }
    private static ConfigurationProfile Profile() => new(1, "completion", "openai", "gpt-6-luna",
        "luna-host", "gpt-6-luna", DateTimeOffset.UnixEpoch,
        Json("""{"reasoningEffort":"high","maxOutputTokens":256}"""), null, [],
        new(1, "schema", Json("""{"contractVersion":1,"responseSchema":null}""")),
        new(1, "prompt", Json("""{"system":"Instrução pinada"}""")), null);
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class Transport(HttpStatusCode status = HttpStatusCode.OK, string? response = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(response ?? """
                {"status":"completed","model":"gpt-6-luna","output":[{"type":"message","content":[{"type":"output_text","text":"Resposta controlada."}]}]}
                """) };
        }
    }
}
