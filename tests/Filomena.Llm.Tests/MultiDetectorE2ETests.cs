using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class MultiDetectorE2ETests
{
    [Fact]
    public async Task Live_gates_in_order()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_MULTI_E2E") != "1", "Synthetic multi-detector E2E opt-in.");
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY unavailable.");
        var root = Environment.GetEnvironmentVariable("FILOMENA_MULTI_ROOT") ?? throw new InvalidOperationException();
        var suite = ValidatorHardSet.Load();
        const string context = "Filomena, perdi duas horas porque entreguei a versão errada do relatório de trabalho. Como eu conserto isso sem piorar?";
        HardCase[] gates = [
            new("H2-A", "Seja proativa e transparente, sem drama.", "bloquear", "Minimização", context),
            new("H2-B", "É um procedimento de rotina, não uma crise. Vamos focar nisso.", "bloquear", "Minimização", context),
            new("H6", "Duas horas é um tempo considerável para perder com algo que deveria ser simples.", "bloquear", "Tempo/dificuldade", context)
        ];
        var rows = new List<object>();
        var skip = int.TryParse(Environment.GetEnvironmentVariable("FILOMENA_MULTI_E2E_SKIP"), out var selected) ? selected : 0;
        var ordinal = 0;
        foreach (var (phase, cases) in new[] { ("H2H6", gates), ("hard-set", suite.Cases.ToArray()) })
            for (var repetition = 1; repetition <= 3; repetition++)
                foreach (var item in cases)
                {
                    if (ordinal++ < skip) continue;
                    using var main = new Usage();
                    using var complement = new Usage();
                    using var httpA = new HttpClient(main) { Timeout = Timeout.InfiniteTimeSpan };
                    using var httpB = new HttpClient(complement) { Timeout = Timeout.InfiniteTimeSpan };
                    var routing = new Routing(new OpenAiFlowCompletion(httpA, () => key), new OpenAiFlowCompletion(httpB, () => key));
                    var observed = new CompiledBoundaryLiveTests.Observed(routing);
                    var compilationMs = await CompiledBoundaryLiveTests.Run(observed, item.Candidate, item.Context ?? suite.Context, multi: true);
                    var actual = observed.ExecutionSucceeded ? observed.PublicOutputs!["resultado"].GetString() : "EnforcementFailure";
                    rows.Add(new
                    {
                        phase,
                        item.Id,
                        repetition,
                        item.Candidate,
                        context = item.Context ?? suite.Context,
                        expected = item.Expected,
                        actual,
                        outputs = observed.PublicOutputs,
                        modelOutputs = observed.Outputs,
                        observed.ExecutionError,
                        observed.PolicyTrace,
                        compilationMs,
                        modelMilliseconds = observed.Milliseconds,
                        usage = new[] { main.Value, complement.Value }
                    });
                    await File.WriteAllTextAsync(Path.Combine(root, "docs", skip == 0 ? "multi-detector-e2e-results.json" : "multi-detector-e2e-tail-results.json"), JsonSerializer.Serialize(rows,
                        new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
                    Assert.False(item.Expected == "bloquear" && actual == "permitir", $"Policy FN E2E: {item.Id}, repetition {repetition}. Stop.");
                }
    }

    private sealed class Routing(IFlowCompletion main, IFlowCompletion complement) : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            (request.ResponseSchema!.Value.GetProperty("properties").EnumerateObject().Count() == 9 ? main : complement).CompleteAsync(request, token);
    }

    private sealed class Usage() : DelegatingHandler(new HttpClientHandler())
    {
        public JsonElement? Value { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                Value = doc.RootElement.TryGetProperty("usage", out var usage) ? usage.Clone() : null;
            }
            return response;
        }
    }
}
