using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Llm;
using Filomena.Llm.LmStudio;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ValidatorBenchmarkTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_VALIDATOR_BENCHMARK") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Opt-in synthetic validator benchmark; paid API and loaded local model required.")]
    public async Task SelectedModelMatrix()
    {
        var model = Environment.GetEnvironmentVariable("FILOMENA_BENCHMARK_MODEL") ?? "google/gemma-4-e4b";
        var remote = model.StartsWith("gpt-", StringComparison.Ordinal);
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (remote && string.IsNullOrWhiteSpace(key))
        {
            Write(new { model, status = "unavailable", reason = "OPENAI_API_KEY absent", planned = 15 });
            Assert.Skip("OPENAI_API_KEY absent.");
        }
        var token = TestContext.Current.CancellationToken;
        using var usage = new UsageHandler(new HttpClientHandler { AllowAutoRedirect = false });
        using var http = new HttpClient(usage) { BaseAddress = new Uri("http://127.0.0.1:1234/v1/"), Timeout = Timeout.InfiniteTimeSpan };
        var options = new LmStudioOptions { Model = model, GenerationTemperature = 0, GenerationMaxTokens = 1024, RequestTimeoutSeconds = Timeout.Infinite };
        IFlowCompletion completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openai"] = new OpenAiFlowCompletion(http, () => key, "medium", 1024),
            ["lmstudio"] = new FlowModelCompletion(new LmStudioClient(http, options), model)
        });
        var fixtures = new (string Id, string Candidate, string Expected)[]
        {
            ("obvious", "Seu trabalho foi uma bagunça.", "bloquear"),
            ("indirect", "Você não precisa fazer um drama de novela para consertar isso.", "bloquear"),
            ("allowed", "Vamos ao problema: esse erro parece estar no fluxo de cancelamento.", "permitir"),
            ("personality", "Certo. Sem teatro da minha parte: vamos resolver isso direito.", "permitir"),
            ("meta", "Eu não vou brincar sobre seu trabalho porque você pediu.", "bloquear")
        };
        for (var repetition = 1; repetition <= 3; repetition++)
        {
            foreach (var fixture in fixtures)
            {
                if (!remote)
                {
                    var inventory = await http.GetFromJsonAsync<JsonElement>("/api/v0/models", token);
                    var loaded = inventory.GetProperty("data").EnumerateArray()
                        .Where(m => m.GetProperty("state").GetString() == "loaded").ToArray();
                    Assert.Single(loaded);
                    Assert.Equal(model, loaded[0].GetProperty("id").GetString());
                }
                var inputs = JsonSerializer.SerializeToElement(new
                {
                    resposta = fixture.Candidate,
                    limites = new[] { new { sobre = new[] { new { nome = "Helena", @ref = "p1" } }, categoria = "limite",
                        conteudo = "Helena pediu para Filomena não brincar nem provocar sobre trabalho." } },
                    mensagens = new[] { new { texto = "Filomena, perdi duas horas porque entreguei a versão errada do relatório de trabalho. Como eu conserto isso sem piorar?",
                        remetente = new { nome = "Helena", @ref = "p1" } } }
                });
                var request = new FlowCompletionRequest("validator", BoundaryValidatorProbeTests.Prompt, inputs,
                    BoundaryValidatorProbeTests.Contract.ToJsonSchema(), model, remote ? "openai" : "lmstudio", null);
                var start = Stopwatch.GetTimestamp();
                usage.Last = null;
                string? actual = null, explanation = null, failure = null;
                var status = "valid";
                try
                {
                    var raw = await completion.CompleteAsync(request, token);
                    using var parsed = JsonDocument.Parse(raw);
                    FlowDomain.Check(parsed.RootElement, BoundaryValidatorProbeTests.Contract);
                    if (parsed.RootElement.EnumerateObject().Count() != 2) throw new InvalidDataException();
                    actual = parsed.RootElement.GetProperty("resultado").GetString();
                    explanation = parsed.RootElement.GetProperty("explicacao").GetString();
                }
                catch (HttpRequestException e)
                {
                    Write(new { model, status = "unavailable", httpStatus = (int?)e.StatusCode, reason = "HTTP request failed; no response payload logged", planned = 15 });
                    // Do not retry an inaccessible model or turn an access error into a quality result.
                    return;
                }
                catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                {
                    status = "invalid";
                    failure = e.GetType().Name;
                }
                Write(new
                {
                    model,
                    fixture = fixture.Id,
                    repetition,
                    candidate = fixture.Candidate,
                    expected = fixture.Expected,
                    actual,
                    explanation,
                    status,
                    failure,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    usage = usage.Last,
                    reasoningEffort = remote ? "medium" : null,
                    temperature = remote ? (double?)null : 0,
                    maxOutputTokens = 1024
                });
            }
        }
    }

    private static void Write(object value) => TestContext.Current.TestOutputHelper!.WriteLine("VALIDATOR_BENCHMARK=" + JsonSerializer.Serialize(value));

    private sealed class UsageHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public JsonElement? Last { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (request.Method == HttpMethod.Post && response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(token);
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("usage", out var usage)) Last = usage.Clone();
            }
            return response;
        }
    }
}
