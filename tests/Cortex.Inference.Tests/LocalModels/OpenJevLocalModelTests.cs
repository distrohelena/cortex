using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Inference.OpenJev;
using Cortex.TestSupport.LocalModels;
using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Real end-to-end OpenJev runs: pinned llama.cpp and GGUFs from the local cache, a llama-server on a free loopback port
/// with every layer on the GPU, and the demo cases through <see cref="ProviderFlowCompletion"/>. Missing GPU, network or
/// server is a failure, never a skip.
/// </summary>
[Trait("Category", "LocalModels")]
public sealed class OpenJevLocalModelTests
{
    private const string AccountState = "A customer says a password reset succeeded, but every login attempt still returns ‘account locked’. Two unlock emails were requested and neither arrived.";
    private const string EmailState = "An email claims to be from the payroll team and says the recipient’s salary payment will be suspended today. It comes from payroll-review@outlook.com and links to a non-company sign-in page asking for a password and verification code.";
    private const string EmailQuestion = "How should this email be classified?";
    private static readonly string[] EmailOptions = ["Legitimate", "Spam", "Phishing"];
    private const double BooleanThreshold = 0.5;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private sealed record DemoCase(string Name, string State, string Question, string Field, string Schema, string[] Options, string Expected);

    private static readonly DemoCase[] Cases =
    [
        new("account", AccountState, "Which queue should handle this request?", "queue",
            """{"type":"object","properties":{"queue":{"type":"string","enum":["Account access support","Billing support","Close as resolved"]}},"required":["queue"],"additionalProperties":false}""",
            ["Account access support", "Billing support", "Close as resolved"], "Account access support"),
        new("email", EmailState, EmailQuestion, "classification",
            """{"type":"object","properties":{"classification":{"type":"string","enum":["Legitimate","Spam","Phishing"]}},"required":["classification"],"additionalProperties":false}""",
            EmailOptions, "Phishing"),
        new("boolean", EmailState, "Answer the field about this email.", "asks_for_credentials",
            """{"type":"object","properties":{"asks_for_credentials":{"type":"boolean","description":"The message asks the recipient to enter a password or verification code."}},"required":["asks_for_credentials"],"additionalProperties":false}""",
            ["true", "false"], "true"),
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Log(string line) => TestContext.Current.TestOutputHelper!.WriteLine(line);

    [Theory]
    [InlineData("qwen3-0.6b", false)]
    [InlineData("minicpm5-2b", true)]
    [InlineData("qwen3.5-4b", true)]
    public async Task Demo_cases_run_end_to_end_through_the_openjev_provider(string modelKey, bool requireCorrect)
    {
        await using var server = await StartServerAsync(modelKey);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var decisions = new List<(string Field, OpenJevDecision Decision)>();
        var completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openjev"] = new OpenJevFlowCompletion(new OpenJevClient(http, server.BaseUri), BooleanThreshold,
                observeDecision: (_, field, decision) => decisions.Add((field, decision))),
        });

        var wrong = new List<string>();
        foreach (var demo in Cases)
        {
            decisions.Clear();
            var request = new FlowCompletionRequest(demo.Name, demo.Question, JsonSerializer.SerializeToElement(demo.State),
                JsonDocument.Parse(demo.Schema).RootElement.Clone(), modelKey, "openjev", null);
            using var output = JsonDocument.Parse(await completion.CompleteAsync(request, Token));

            var (field, decision) = Assert.Single(decisions);
            Assert.Equal(demo.Field, field);
            Assert.Equal(modelKey, decision.Model);
            Assert.Equal(demo.Options, decision.Options.Select(o => o.Text).ToArray());
            Assert.All(decision.Options, o => Assert.True(double.IsFinite(o.Probability) && double.IsFinite(o.Logprob),
                $"{modelKey}/{demo.Name}: option '{o.Text}' has a non-finite probability {o.Probability} or logprob {o.Logprob}."));
            var sum = decision.Options.Sum(o => o.Probability);
            Assert.True(Math.Abs(sum - 1) <= 1e-6, $"{modelKey}/{demo.Name}: probabilities sum to {sum:R}, not 1 ± 1e-6.");

            var value = output.RootElement.GetProperty(demo.Field);
            string choice;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                choice = value.GetBoolean() ? "true" : "false";
                Assert.Equal(decision.Options[0].Probability >= BooleanThreshold ? "true" : "false", choice);
            }
            else
            {
                choice = value.GetString()!;
                var argmax = decision.Options.Aggregate((best, next) => next.Probability > best.Probability ? next : best);
                Assert.Equal(argmax.Text, choice);
            }

            Log($"OPENJEV {modelKey} {demo.Name}: choice={choice} expected={demo.Expected} " +
                string.Join(" ", decision.Options.Select(o => $"[{o.Label}] {o.Text}={o.Probability.ToString("F6", CultureInfo.InvariantCulture)}")) +
                $" tokens={decision.InputTokens}/{decision.OutputTokens} latency={decision.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}ms");
            if (choice != demo.Expected) wrong.Add($"{demo.Name}: chose '{choice}', expected '{demo.Expected}'");
        }

        if (requireCorrect)
            Assert.True(wrong.Count == 0, $"{modelKey} answered demo cases wrongly: {string.Join("; ", wrong)}.");
    }

    [Fact]
    public async Task Label_bias_does_not_change_label_distribution()
    {
        const string modelKey = "minicpm5-2b";
        await using var server = await StartServerAsync(modelKey);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var client = new OpenJevClient(http, server.BaseUri);

        var biased = await client.DecideAsync(modelKey, EmailState, EmailQuestion, EmailOptions, null, Token, applyLabelBias: true);
        OpenJevDecision unbiased;
        try
        {
            unbiased = await client.DecideAsync(modelKey, EmailState, EmailQuestion, EmailOptions, null, Token, applyLabelBias: false);
        }
        catch (InvalidDataException error) when (error.Message.Contains("logprob entries", StringComparison.Ordinal))
        {
            Assert.Fail($"Without logit_bias a label was missing from the top 20 logprobs, so the distributions cannot be compared: {error.Message}");
            return;
        }

        for (var i = 0; i < EmailOptions.Length; i++)
            Log(string.Create(CultureInfo.InvariantCulture, $"OPENJEV-BIAS {modelKey} [{biased.Options[i].Label}] {EmailOptions[i]}: " +
                $"biased p={biased.Options[i].Probability:R} logprob={biased.Options[i].Logprob:R}; " +
                $"unbiased p={unbiased.Options[i].Probability:R} logprob={unbiased.Options[i].Logprob:R}; " +
                $"|Δp|={Math.Abs(biased.Options[i].Probability - unbiased.Options[i].Probability):R}"));
        for (var i = 0; i < EmailOptions.Length; i++)
            Assert.True(Math.Abs(biased.Options[i].Probability - unbiased.Options[i].Probability) <= 1e-4,
                $"logit_bias changed P({EmailOptions[i]}) from {unbiased.Options[i].Probability:R} to {biased.Options[i].Probability:R} (tolerance 1e-4).");
    }

    private static async Task<LocalServerProcess> StartServerAsync(string modelKey)
    {
        var manifest = LocalModelManifest.Load();
        var cache = new LocalModelCache();
        var llamaServer = await cache.EnsureLlamaServerAsync(manifest, Token);
        await RequireCudaDeviceAsync(llamaServer);
        var gguf = await cache.EnsureGgufAsync(manifest, modelKey, Token);
        var port = LocalServerProcess.FreeLoopbackPort();
        return await LocalServerProcess.StartAsync(llamaServer,
            ["-m", gguf, "--alias", modelKey, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "-ngl", "999", "-c", "2048"],
            new Dictionary<string, string>(), new Uri($"http://127.0.0.1:{port}/health"), StartupTimeout, Token);
    }

    /// <summary>Without a CUDA device <c>-ngl 999</c> silently runs on the CPU; that is a broken environment, not a pass.</summary>
    private static async Task RequireCudaDeviceAsync(string llamaServer)
    {
        var start = new ProcessStartInfo(llamaServer, "--list-devices")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {llamaServer} --list-devices.");
        var stdout = process.StandardOutput.ReadToEndAsync(Token);
        var stderr = process.StandardError.ReadToEndAsync(Token);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bound.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(bound.Token); }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("llama-server --list-devices did not finish in 60 s.");
        }
        var devices = await stdout + await stderr;
        Assert.True(devices.Contains("CUDA0", StringComparison.Ordinal),
            $"No CUDA GPU visible to llama-server; OpenJev local-model tests need one. llama-server --list-devices said:{Environment.NewLine}{devices}");
    }
}
