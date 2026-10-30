using System.Globalization;
using System.Text.Json;
using Cortex.Inference.SystemOne;
using Cortex.TestSupport.LocalModels;
using Xunit;
using static Cortex.Inference.Tests.LocalModels.LocalModelDemoCases;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Real end-to-end Laya runs: the pinned <c>laya[serve]</c> and CUDA torch in the cached venv, <c>laya.serve</c> on a
/// free loopback port with the checkpoint under test preloaded on the GPU at the pinned weight revision, and the demo
/// cases through <see cref="ProviderFlowCompletion"/> → <see cref="SystemOneFlowCompletion"/> with the strict
/// <see cref="SystemOneClient"/>. Missing GPU, network or server is a failure, never a skip. Semantic correctness is
/// only logged; the spec does not require it for Laya.
/// </summary>
[Trait("Category", "LocalModels")]
[Collection(LocalModelServers.Collection)]
public sealed class LayaLocalModelTests
{
    // First run downloads the checkpoint into the HF cache before the server binds its port.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Log(string line) => TestContext.Current.TestOutputHelper!.WriteLine(line);

    /// <summary>
    /// Every demo case in every cyclic order of its <c>criteria</c> (the schema's enum order; a boolean has one order).
    /// Structure is asserted; semantic correctness and the probability of the expected answer per order are only logged.
    /// </summary>
    [Theory]
    [InlineData("english")]
    [InlineData("multilingual")]
    [InlineData("typed-decisions")]
    public async Task Demo_cases_run_end_to_end_through_the_laya_provider_in_every_criteria_order(string model)
    {
        var manifest = LocalModelManifest.Load();
        await using var server = await StartServerAsync(manifest, model);
        await RequireCudaCheckpointAsync(server, model, manifest.Laya.ModelRevision);
        using var runner = new LayaCaseRunner(server.BaseUri, model);

        var wrong = new List<string>();
        foreach (var demo in Cases)
        {
            var expected = new List<string>();
            for (var shift = 0; shift < LayaCaseRunner.Rotations(demo); shift++)
            {
                var outcome = await runner.RunAsync(demo, shift, Token);
                var result = runner.Last;
                var order = demo.Boolean ? "noul" : string.Join(" / ", demo.Rotated(shift));
                expected.Add(outcome.FormatExpected(demo, "F4"));
                Log($"LAYA {model} {demo.Id} shift={shift} [{order}]: choice={outcome.Choice} expected={demo.Expected} {outcome.Describe()} " +
                    $"confidence={result.Answers.GetProperty(demo.Field).GetProperty("confidence").GetDouble().ToString("F4", CultureInfo.InvariantCulture)} " +
                    $"tokens={result.InputTokens}/{result.OutputTokens} latency={result.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}ms");
                if (!outcome.IsCorrect(demo)) wrong.Add($"{demo.Id} shift {shift} [{order}]: chose '{outcome.Choice}', expected '{demo.Expected}'");
            }
            Log($"LAYA {model} {demo.Id}: P(expected) per order = {string.Join(" / ", expected)}");
        }

        Log(wrong.Count == 0 ? $"LAYA {model}: all demo cases semantically correct in every order."
            : $"LAYA {model}: semantically wrong (logged only): {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// <c>laya.serve</c> reads its bind address from <c>LAYA_HOST</c>/<c>LAYA_PORT</c>, so it binds loopback only. Only the
    /// checkpoint under test is preloaded; <c>LAYA_REVISION</c> pins every checkpoint load (all three live in the
    /// <c>convaiinnovations/laya</c> bundle repo) to the manifest's commit. The server only binds after the preload,
    /// so <c>/health</c> answering 200 means the checkpoint is resident.
    /// </summary>
    internal static async Task<LocalServerProcess> StartServerAsync(LocalModelManifest manifest, string model)
    {
        var python = await new LocalModelCache().EnsureLayaAsync(manifest, Token);
        var port = LocalServerProcess.FreeLoopbackPort().ToString(CultureInfo.InvariantCulture);
        return await LocalServerProcess.StartAsync(python, ["-m", "laya.serve"],
            new Dictionary<string, string>
            {
                ["LAYA_HOST"] = "127.0.0.1",
                ["LAYA_PORT"] = port,
                ["LAYA_DEVICE"] = "cuda",
                ["LAYA_PRELOAD"] = "1",
                ["LAYA_MODELS"] = model,
                ["LAYA_REVISION"] = manifest.Laya.ModelRevision,
                ["LAYA_API_KEY"] = "",
                ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1",
            },
            new Uri($"http://127.0.0.1:{port}/health"), StartupTimeout, Token);
    }

    /// <summary>A checkpoint that fell back to the CPU or loaded another revision is a broken environment, not a pass.</summary>
    internal static async Task RequireCudaCheckpointAsync(LocalServerProcess server, string model, string revision)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var text = await http.GetStringAsync(new Uri(server.BaseUri, "/health"), Token);
        using var health = JsonDocument.Parse(text);
        var root = health.RootElement;
        Assert.True(root.TryGetProperty("checkpoint_devices", out var devices) && devices.TryGetProperty(model, out var device) &&
            device.GetString() == "cuda", $"Laya checkpoint '{model}' is not on CUDA; Laya local-model tests need a GPU. /health said: {text}");
        Assert.True(root.TryGetProperty("revisions", out var revisions) && revisions.TryGetProperty(model, out var loaded) &&
            loaded.GetString() == revision, $"Laya checkpoint '{model}' is not at the pinned revision {revision}. /health said: {text}");
    }
}
