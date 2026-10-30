using System.Diagnostics;
using System.Globalization;
using Cortex.Inference.OpenJev;
using Cortex.TestSupport.LocalModels;
using Xunit;
using static Cortex.Inference.Tests.LocalModels.LocalModelDemoCases;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Real end-to-end OpenJev runs: pinned llama.cpp and GGUFs from the local cache, a llama-server on a free loopback port
/// with every layer on the GPU, and the demo cases through <see cref="ProviderFlowCompletion"/>. Missing GPU, network or
/// server is a failure, never a skip.
/// </summary>
[Trait("Category", "LocalModels")]
[Collection(LocalModelServers.Collection)]
public sealed class OpenJevLocalModelTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Log(string line) => TestContext.Current.TestOutputHelper!.WriteLine(line);

    /// <summary>
    /// Every demo case in every cyclic order of its options, one <see cref="OpenJevOptionOrder.Fixed"/> call each (see
    /// <see cref="OpenJevCaseRunner"/> for how a boolean's No/Yes order is asked). A model that must be correct must be
    /// correct in every order: small models' label-position bias is exactly what a single fixed order hides.
    /// </summary>
    [Theory]
    [InlineData("qwen3-0.6b", false)]
    [InlineData("minicpm5-2b", true)]
    [InlineData("qwen3.5-4b", true)]
    public async Task Demo_cases_are_answered_in_every_fixed_option_order(string modelKey, bool requireCorrect)
    {
        await using var server = await StartServerAsync(modelKey);
        using var runner = new OpenJevCaseRunner(server.BaseUri, modelKey);

        var wrong = new List<string>();
        foreach (var demo in Cases)
        {
            var expected = new List<string>();
            for (var shift = 0; shift < demo.Rotations; shift++)
            {
                var outcome = await runner.FixedAsync(demo, shift, Token);
                var order = string.Join(" / ", demo.Rotated(shift));
                expected.Add(outcome.ExpectedProbability(demo).ToString("F4", CultureInfo.InvariantCulture));
                Log($"OPENJEV {modelKey} {demo.Id} fixed shift={shift} [{order}]: choice={outcome.Choice} expected={demo.Expected} " +
                    $"P(expected)={outcome.ExpectedProbability(demo).ToString("F6", CultureInfo.InvariantCulture)} {outcome.Describe()}");
                if (!outcome.IsCorrect(demo)) wrong.Add($"{demo.Id} shift {shift} [{order}]: chose '{outcome.Choice}', expected '{demo.Expected}'");
            }
            Log($"OPENJEV {modelKey} {demo.Id} fixed: P(expected) per rotation = {string.Join(" / ", expected)}");
        }

        if (requireCorrect)
            Assert.True(wrong.Count == 0, $"{modelKey} answered demo cases wrongly in some option order: {string.Join("; ", wrong)}.");
    }

    /// <summary>Every demo case with <see cref="OpenJevOptionOrder.AverageRotations"/>, through the flow.</summary>
    [Theory]
    [InlineData("qwen3-0.6b", false)]
    [InlineData("minicpm5-2b", true)]
    [InlineData("qwen3.5-4b", true)]
    public async Task Demo_cases_are_answered_with_rotation_averaging(string modelKey, bool requireCorrect)
    {
        await using var server = await StartServerAsync(modelKey);
        using var runner = new OpenJevCaseRunner(server.BaseUri, modelKey);

        var wrong = new List<string>();
        foreach (var demo in Cases)
        {
            var calls = runner.Timer.Calls.Count;
            var outcome = await runner.AveragedAsync(demo, Token);
            Assert.Equal(demo.Rotations, runner.Timer.Calls.Count - calls);
            Log($"OPENJEV {modelKey} {demo.Id} averaged: choice={outcome.Choice} expected={demo.Expected} " +
                $"P(expected)={outcome.ExpectedProbability(demo).ToString("F6", CultureInfo.InvariantCulture)} {outcome.Describe()}");
            if (!outcome.IsCorrect(demo)) wrong.Add($"{demo.Id}: chose '{outcome.Choice}', expected '{demo.Expected}'");
        }

        if (requireCorrect)
            Assert.True(wrong.Count == 0, $"{modelKey} answered demo cases wrongly with rotation averaging: {string.Join("; ", wrong)}.");
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

    internal static async Task<LocalServerProcess> StartServerAsync(string modelKey)
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
