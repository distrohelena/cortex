using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Runs <see cref="LocalDecisionCase"/>s against a local <c>laya.serve</c> through <see cref="ProviderFlowCompletion"/> →
/// <see cref="SystemOneFlowCompletion"/> with the strict <see cref="SystemOneClient"/>, checking the structure of every
/// answer (policy, usage, routing to the requested checkpoint, probabilities over exactly the enum, choice consistent).
/// </summary>
/// <remarks>
/// A choice is rotated through the schema: the enum order becomes the order of the <c>criteria</c> Laya receives. A
/// boolean is a single <c>noul</c> question without criteria, so it has only one order.
/// </remarks>
internal sealed class LayaCaseRunner : IDisposable
{
    internal const double BooleanThreshold = 0.5;
    private readonly string model;
    private readonly HttpClient http;
    private readonly ResponseRecorder recorder;
    private readonly List<SystemOneResult> results = [];
    private readonly ProviderFlowCompletion completion;

    internal LayaCaseRunner(Uri baseUri, string model)
    {
        this.model = model;
        Timer = new InferenceCallTimer(new HttpClientHandler(), request => request.RequestUri!.AbsolutePath.EndsWith("/v1/systemone", StringComparison.Ordinal));
        recorder = new ResponseRecorder(Timer);
        http = new HttpClient(recorder) { Timeout = TimeSpan.FromMinutes(2) };
        completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["laya"] = new SystemOneFlowCompletion(
                new SystemOneClient(http, new Uri(baseUri, "/v1/systemone"), () => null, requireCredential: false,
                    SystemOneProbabilityPolicy.LayaFourDecimals),
                "laya", BooleanThreshold, observe: (_, result) => results.Add(result)),
        });
    }

    internal InferenceCallTimer Timer { get; }

    /// <summary>The <see cref="SystemOneResult"/> of the last <see cref="RunAsync"/>.</summary>
    internal SystemOneResult Last { get; private set; } = null!;

    public void Dispose() => http.Dispose();

    internal static int Rotations(LocalDecisionCase decision) => decision.Boolean ? 1 : decision.Options.Length;

    /// <summary>One request with the choice's criteria in cyclic rotation <paramref name="shift"/>.</summary>
    internal async Task<LocalDecisionOutcome> RunAsync(LocalDecisionCase decision, int shift, CancellationToken token)
    {
        results.Clear();
        recorder.Bodies.Clear();
        var request = new FlowCompletionRequest(decision.Id, decision.Task, JsonSerializer.SerializeToElement(decision.State),
            decision.Schema(shift), model, "laya", null);
        using var output = JsonDocument.Parse(await completion.CompleteAsync(request, token));

        // The strict client accepted the response; the result is what it validated.
        var result = Last = Assert.Single(results);
        Assert.Equal(nameof(SystemOneProbabilityPolicy.LayaFourDecimals), result.ProbabilityPolicy);
        Assert.True(result.InputTokens > 0, $"laya/{model}/{decision.Id}: usage.input_tokens was {result.InputTokens}, expected > 0.");
        Assert.True(result.OutputTokens >= 0, $"laya/{model}/{decision.Id}: usage.output_tokens was {result.OutputTokens}.");

        // Laya answers model "laya-rl-agent" for every checkpoint and silently auto-routes names it does not know,
        // so the routing block is the only proof the requested checkpoint answered.
        using (var body = JsonDocument.Parse(Assert.Single(recorder.Bodies)))
        {
            var routing = body.RootElement.GetProperty("routing");
            Assert.True(routing.GetProperty("model").GetString() == model,
                $"laya/{model}/{decision.Id}: request was routed to another checkpoint: {routing.GetRawText()}");
        }

        var answer = result.Answers.GetProperty(decision.Field);
        var value = output.RootElement.GetProperty(decision.Field);
        if (decision.Boolean)
        {
            Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"laya/{model}/{decision.Id}: boolean field answered {value.GetRawText()}.");
            var noul = answer.GetProperty("noul").GetDouble();
            Assert.True(double.IsFinite(noul) && noul is >= 0 and <= 1, $"laya/{model}/{decision.Id}: noul {noul} outside [0,1].");
            var choice = value.GetBoolean() ? "true" : "false";
            Assert.Equal(noul >= BooleanThreshold ? "true" : "false", choice);
            return new(shift, choice, new Dictionary<string, double> { [LocalDecisionCase.Yes] = noul, [LocalDecisionCase.No] = 1 - noul });
        }

        var selected = value.GetString()!;
        Assert.Contains(selected, decision.Options);
        Assert.Equal(answer.GetProperty("choice").GetString(), selected);
        var reported = answer.GetProperty("probabilities");
        Assert.Equal(decision.Options.Order(StringComparer.Ordinal), reported.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var probabilities = decision.Options.ToDictionary(o => o, o => reported.GetProperty(o).GetDouble(), StringComparer.Ordinal);
        Assert.All(probabilities, p => Assert.True(double.IsFinite(p.Value) && p.Value is >= 0 and <= 1,
            $"laya/{model}/{decision.Id}: option '{p.Key}' has probability {p.Value}."));
        // Four-decimal rounding: the same tolerance the strict client applies.
        var sum = probabilities.Values.Sum();
        Assert.True(Math.Abs(sum - 1) <= probabilities.Count * 0.000051 + 0.000001, $"laya/{model}/{decision.Id}: probabilities sum to {sum:R}.");
        return new(shift, selected, probabilities);
    }

    private sealed class ResponseRecorder(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            var replay = new StringContent(body);
            replay.Headers.ContentType = response.Content.Headers.ContentType;
            response.Content = replay;
            return response;
        }
    }
}
