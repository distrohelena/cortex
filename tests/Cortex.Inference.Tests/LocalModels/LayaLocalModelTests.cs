using System.Globalization;
using System.Text.Json;
using Cortex.Core.Flows;
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
public sealed class LayaLocalModelTests
{
    private const double BooleanThreshold = 0.5;
    // First run downloads the checkpoint into the HF cache before the server binds its port.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Log(string line) => TestContext.Current.TestOutputHelper!.WriteLine(line);

    [Theory]
    [InlineData("english")]
    [InlineData("multilingual")]
    [InlineData("typed-decisions")]
    public async Task Demo_cases_run_end_to_end_through_the_laya_provider(string model)
    {
        var manifest = LocalModelManifest.Load();
        await using var server = await StartServerAsync(manifest, model);
        await RequireCudaCheckpointAsync(server, model, manifest.Laya.ModelRevision);

        var recorder = new ResponseRecorder(new HttpClientHandler());
        using var http = new HttpClient(recorder) { Timeout = TimeSpan.FromMinutes(2) };
        var results = new List<SystemOneResult>();
        var completion = new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["laya"] = new SystemOneFlowCompletion(
                new SystemOneClient(http, new Uri(server.BaseUri, "/v1/systemone"), () => null, requireCredential: false,
                    SystemOneProbabilityPolicy.LayaFourDecimals),
                "laya", BooleanThreshold, observe: (_, result) => results.Add(result)),
        });

        var wrong = new List<string>();
        foreach (var demo in Cases)
        {
            results.Clear();
            recorder.Bodies.Clear();
            var request = new FlowCompletionRequest(demo.Name, demo.Question, JsonSerializer.SerializeToElement(demo.State),
                JsonDocument.Parse(demo.Schema).RootElement.Clone(), model, "laya", null);
            using var output = JsonDocument.Parse(await completion.CompleteAsync(request, Token));

            // The strict client accepted the response; the result is what it validated.
            var result = Assert.Single(results);
            Assert.Equal(nameof(SystemOneProbabilityPolicy.LayaFourDecimals), result.ProbabilityPolicy);
            Assert.True(result.InputTokens > 0, $"laya/{model}/{demo.Name}: usage.input_tokens was {result.InputTokens}, expected > 0.");
            Assert.True(result.OutputTokens >= 0, $"laya/{model}/{demo.Name}: usage.output_tokens was {result.OutputTokens}.");

            // Laya answers model "laya-rl-agent" for every checkpoint and silently auto-routes names it does not know,
            // so the routing block is the only proof the requested checkpoint answered.
            using (var body = JsonDocument.Parse(Assert.Single(recorder.Bodies)))
            {
                var routing = body.RootElement.GetProperty("routing");
                Assert.True(routing.GetProperty("model").GetString() == model,
                    $"laya/{model}/{demo.Name}: request was routed to another checkpoint: {routing.GetRawText()}");
            }

            var answer = result.Answers.GetProperty(demo.Field);
            var value = output.RootElement.GetProperty(demo.Field);
            string choice, detail;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                var noul = answer.GetProperty("noul").GetDouble();
                choice = value.GetBoolean() ? "true" : "false";
                Assert.Equal(noul >= BooleanThreshold ? "true" : "false", choice);
                detail = string.Create(CultureInfo.InvariantCulture, $"noul={noul:F4}");
            }
            else
            {
                choice = value.GetString()!;
                Assert.Contains(choice, demo.Options);
                Assert.Equal(answer.GetProperty("choice").GetString(), choice);
                var probabilities = answer.GetProperty("probabilities");
                Assert.Equal(demo.Options.Order(StringComparer.Ordinal), probabilities.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
                detail = string.Join(" ", demo.Options.Select(o =>
                    $"{o}={probabilities.GetProperty(o).GetDouble().ToString("F4", CultureInfo.InvariantCulture)}"));
            }

            Log($"LAYA {model} {demo.Name}: choice={choice} expected={demo.Expected} {detail} " +
                $"confidence={answer.GetProperty("confidence").GetDouble().ToString("F4", CultureInfo.InvariantCulture)} " +
                $"tokens={result.InputTokens}/{result.OutputTokens} latency={result.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}ms");
            if (choice != demo.Expected) wrong.Add($"{demo.Name}: chose '{choice}', expected '{demo.Expected}'");
        }

        Log(wrong.Count == 0 ? $"LAYA {model}: all demo cases semantically correct."
            : $"LAYA {model}: semantically wrong (logged only): {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// <c>laya.serve</c> reads its bind address from <c>LAYA_HOST</c>/<c>LAYA_PORT</c>, so it binds loopback only. Only the
    /// checkpoint under test is preloaded; <c>LAYA_REVISION</c> pins every checkpoint load (all three live in the
    /// <c>convaiinnovations/laya</c> bundle repo) to the manifest's commit. The server only binds after the preload,
    /// so <c>/health</c> answering 200 means the checkpoint is resident.
    /// </summary>
    private static async Task<LocalServerProcess> StartServerAsync(LocalModelManifest manifest, string model)
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
    private static async Task RequireCudaCheckpointAsync(LocalServerProcess server, string model, string revision)
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
