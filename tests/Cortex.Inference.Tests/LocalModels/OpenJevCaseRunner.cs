using System.Globalization;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenJev;
using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>What one model answered for one case in one option order (or averaged over all orders).</summary>
/// <param name="Shift">The cyclic rotation asked; <c>null</c> for the rotation-averaged answer.</param>
/// <param name="Choice">The answer the flow output carried: an option, or <c>"true"</c>/<c>"false"</c>.</param>
/// <param name="Probabilities">Probability per original option text (<c>Yes</c>/<c>No</c> for a boolean).</param>
internal sealed record LocalDecisionOutcome(int? Shift, string Choice, IReadOnlyDictionary<string, double> Probabilities)
{
    internal bool IsCorrect(LocalDecisionCase decision) => Choice == decision.Expected;

    internal double ExpectedProbability(LocalDecisionCase decision) => Probabilities[decision.ExpectedOption];

    internal string Describe() => string.Join(" ", Probabilities.Select(p => $"{p.Key}={p.Value.ToString("F4", CultureInfo.InvariantCulture)}"));
}

/// <summary>Records the wall time of every inference call (chat completion or System 1 request) that passes through it.</summary>
internal sealed class InferenceCallTimer(HttpMessageHandler inner, Func<HttpRequestMessage, bool> isInference) : DelegatingHandler(inner)
{
    internal List<TimeSpan> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        if (isInference(request)) lock (Calls) Calls.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started));
        return response;
    }
}

/// <summary>
/// Runs <see cref="LocalDecisionCase"/>s against a local <c>llama-server</c> through <see cref="ProviderFlowCompletion"/> →
/// <see cref="OpenJevFlowCompletion"/>, checking every decision's structure (model, options as sent, finite probabilities
/// summing to 1, choice = argmax or threshold) before returning it.
/// </summary>
/// <remarks>
/// A choice is rotated through the schema: the enum lists the options in the rotated order, so the flow asks them that
/// way. A boolean cannot be rotated through the schema, because the flow always asks <c>A. Yes / B. No</c>. Its
/// <c>Yes/No</c> order goes through the flow; its <c>No/Yes</c> order goes to <c>OpenJevClient.DecideAsync</c>
/// directly, with the question built exactly as the flow builds it (<see cref="LocalDecisionCase.OpenJevQuestion"/>).
/// That is the same call the flow's <c>AverageRotations</c> mode makes for its second order.
/// </remarks>
internal sealed class OpenJevCaseRunner : IDisposable
{
    internal const double BooleanThreshold = 0.5;
    private readonly string model;
    private readonly HttpClient http;
    private readonly OpenJevClient client;

    internal OpenJevCaseRunner(Uri baseUri, string model)
    {
        this.model = model;
        Timer = new InferenceCallTimer(new HttpClientHandler(), request => request.RequestUri!.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal));
        http = new HttpClient(Timer) { Timeout = TimeSpan.FromMinutes(2) };
        client = new OpenJevClient(http, baseUri);
    }

    internal InferenceCallTimer Timer { get; }

    public void Dispose() => http.Dispose();

    /// <summary>One call in cyclic rotation <paramref name="shift"/> of the options, <see cref="OpenJevOptionOrder.Fixed"/>.</summary>
    internal async Task<LocalDecisionOutcome> FixedAsync(LocalDecisionCase decision, int shift, CancellationToken token)
    {
        var sent = decision.Rotated(shift);
        if (decision.Boolean && shift != 0)
        {
            var direct = await client.DecideAsync(model, decision.State, decision.OpenJevQuestion, sent, null, token);
            var probabilities = Validate(decision, direct, sent, finiteLogprobs: true);
            return new(shift, probabilities[LocalDecisionCase.Yes] >= BooleanThreshold ? "true" : "false", probabilities);
        }
        return await ThroughFlowAsync(decision, shift, sent, OpenJevOptionOrder.Fixed, token);
    }

    /// <summary>The flow's <see cref="OpenJevOptionOrder.AverageRotations"/> answer: one call per rotation, probabilities averaged.</summary>
    internal Task<LocalDecisionOutcome> AveragedAsync(LocalDecisionCase decision, CancellationToken token) =>
        ThroughFlowAsync(decision, null, decision.Rotated(0), OpenJevOptionOrder.AverageRotations, token);

    private async Task<LocalDecisionOutcome> ThroughFlowAsync(LocalDecisionCase decision, int? shift, string[] sent,
        OpenJevOptionOrder order, CancellationToken token)
    {
        var decisions = new List<(string Field, OpenJevDecision Decision)>();
        var completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openjev"] = new OpenJevFlowCompletion(client, BooleanThreshold,
                observeDecision: (_, field, observed) => decisions.Add((field, observed)), optionOrder: order),
        });
        var request = new FlowCompletionRequest(decision.Id, decision.Task, JsonSerializer.SerializeToElement(decision.State),
            decision.Schema(shift ?? 0), model, "openjev", null);
        using var output = JsonDocument.Parse(await completion.CompleteAsync(request, token));

        var (field, observedDecision) = Assert.Single(decisions);
        Assert.Equal(decision.Field, field);
        // A rotation-averaged decision is synthesized: ln(mean) is -Infinity when every order gave an option 0.
        var probabilities = Validate(decision, observedDecision, sent, finiteLogprobs: order == OpenJevOptionOrder.Fixed);
        var value = output.RootElement.GetProperty(decision.Field);
        string choice;
        if (decision.Boolean)
        {
            Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{model}/{decision.Id}: boolean field answered {value.GetRawText()}.");
            choice = value.GetBoolean() ? "true" : "false";
            Assert.Equal(probabilities[LocalDecisionCase.Yes] >= BooleanThreshold ? "true" : "false", choice);
        }
        else
        {
            choice = value.GetString()!;
            var argmax = observedDecision.Options.Aggregate((best, next) => next.Probability > best.Probability ? next : best);
            Assert.Equal(argmax.Text, choice);
        }
        return new(shift, choice, probabilities);
    }

    private Dictionary<string, double> Validate(LocalDecisionCase decision, OpenJevDecision observed, string[] sent, bool finiteLogprobs)
    {
        Assert.Equal(model, observed.Model);
        Assert.Equal(sent, observed.Options.Select(o => o.Text).ToArray());
        Assert.All(observed.Options, o => Assert.True(double.IsFinite(o.Probability) && o.Probability is >= 0 and <= 1 &&
            (!finiteLogprobs || double.IsFinite(o.Logprob)),
            $"{model}/{decision.Id}: option '{o.Text}' has probability {o.Probability} and logprob {o.Logprob}."));
        var sum = observed.Options.Sum(o => o.Probability);
        Assert.True(Math.Abs(sum - 1) <= 1e-6, $"{model}/{decision.Id}: probabilities sum to {sum:R}, not 1 ± 1e-6.");
        // Keyed by option text in the case's own order, whatever order was asked.
        var byText = observed.Options.ToDictionary(o => o.Text, o => o.Probability, StringComparer.Ordinal);
        return decision.Rotated(0).ToDictionary(o => o, o => byText[o], StringComparer.Ordinal);
    }
}
