using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cortex.TestSupport.LocalModels;
using Xunit;

namespace Cortex.Inference.Tests.LocalModels;

/// <summary>
/// Measures every pinned local decision model on <c>tests/fixtures/local-decision-cases.json</c> and overwrites
/// <c>artifacts/local-models/benchmark.md</c>. OpenJev models answer every case in every cyclic option order
/// (<c>Fixed</c>) and once more rotation-averaged; Laya checkpoints answer every case in every <c>criteria</c> order.
/// One server at a time, started exactly as the <c>LocalModels</c> tests start it. This is a measurement: it asserts
/// only structure (finite probabilities summing to 1, a valid choice), never accuracy.
/// </summary>
[Trait("Category", "LocalModelsBenchmark")]
[Collection(LocalModelServers.Collection)]
public sealed class LocalDecisionBenchmarkTests
{
    private static readonly string[] OpenJevModels = ["qwen3-0.6b", "minicpm5-2b", "qwen3.5-4b"];
    private static readonly string[] LayaModels = ["english", "multilingual", "typed-decisions"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Log(string line) => TestContext.Current.TestOutputHelper!.WriteLine(line);

    private enum Mode { FixedRotations, AveragedRotations, LayaCriteriaRotations }

    /// <summary>
    /// One model in one mode: per case, either every rotation's outcome or the single averaged outcome. <see cref="Calls"/>
    /// holds every inference HTTP call; <see cref="Decisions"/> the summed call time of each answer (n calls for an averaged one).
    /// </summary>
    private sealed record Run(string Provider, string Model, Mode Mode, Dictionary<LocalDecisionCase, List<LocalDecisionOutcome>> Outcomes,
        List<TimeSpan> Calls, List<TimeSpan> Decisions);

    /// <summary>Runs one decision and records the summed time of the inference calls it made.</summary>
    private static async Task<LocalDecisionOutcome> Timed(Run run, InferenceCallTimer timer, Func<Task<LocalDecisionOutcome>> decide)
    {
        var before = timer.Calls.Count;
        var outcome = await decide();
        run.Decisions.Add(timer.Calls.Skip(before).Aggregate(TimeSpan.Zero, (sum, call) => sum + call));
        return outcome;
    }

    [Fact]
    public async Task Benchmark_local_decision_models()
    {
        var manifest = LocalModelManifest.Load();
        var cases = LocalDecisionCase.LoadCorpus();
        Assert.True(cases.Count >= 24, $"The corpus has {cases.Count} cases; the benchmark needs at least 24.");
        Assert.Equal(cases.Count(c => c.Language == "en"), cases.Count(c => c.Language == "pt"));
        Assert.Contains(cases, c => c.Boolean);
        Assert.Contains(cases, c => !c.Boolean);

        var runs = new List<Run>();
        var started = Stopwatch.GetTimestamp();
        foreach (var model in OpenJevModels) runs.AddRange(await OpenJevAsync(model, cases));
        foreach (var model in LayaModels) runs.Add(await LayaAsync(manifest, model, cases));

        var report = Report(manifest, cases, runs, Stopwatch.GetElapsedTime(started));
        var path = Path.Combine(LocalModelManifest.RepositoryRoot(), "artifacts", "local-models", "benchmark.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, new UTF8Encoding(false), Token);
        Log($"Wrote {path}");
        Log(report);
    }

    private static async Task<Run[]> OpenJevAsync(string model, IReadOnlyList<LocalDecisionCase> cases)
    {
        await using var server = await OpenJevLocalModelTests.StartServerAsync(model);
        using var runner = new OpenJevCaseRunner(server.BaseUri, model);

        var fixedRun = new Run("openjev", model, Mode.FixedRotations, [], [], []);
        foreach (var decision in cases)
        {
            var outcomes = fixedRun.Outcomes[decision] = [];
            for (var shift = 0; shift < decision.Rotations; shift++)
                outcomes.Add(await Timed(fixedRun, runner.Timer, () => Recorded(model, decision, shift, () => runner.FixedAsync(decision, shift, Token))));
        }
        fixedRun.Calls.AddRange(runner.Timer.Calls);
        Assert.Equal(cases.Sum(c => c.Rotations), fixedRun.Calls.Count);

        runner.Timer.Calls.Clear();
        var averagedRun = new Run("openjev", model, Mode.AveragedRotations, [], [], []);
        foreach (var decision in cases)
            averagedRun.Outcomes[decision] = [await Timed(averagedRun, runner.Timer, () => Recorded(model, decision, null, () => runner.AveragedAsync(decision, Token)))];
        averagedRun.Calls.AddRange(runner.Timer.Calls);
        // A rejected rotation ends its averaged question early, so only an error-free run makes every call.
        if (averagedRun.Outcomes.Values.All(o => o[0].Error is null)) Assert.Equal(cases.Sum(c => c.Rotations), averagedRun.Calls.Count);

        Log($"OPENJEV {model}: fixed {Summary(fixedRun)}; averaged {Summary(averagedRun)}");
        return [fixedRun, averagedRun];
    }

    /// <summary>
    /// OpenJev rejects a response whose top 20 logprobs miss a label (it never renormalizes). That is a property of the
    /// model on that prompt, so the benchmark records it as an error answer (counted wrong) instead of aborting the run.
    /// Every other failure, including a duplicate label entry or a structural assertion, still fails the benchmark.
    /// </summary>
    private static async Task<LocalDecisionOutcome> Recorded(string model, LocalDecisionCase decision, int? shift, Func<Task<LocalDecisionOutcome>> run)
    {
        try { return await run(); }
        catch (InvalidDataException error) when (error.Message.Contains("has 0 logprob entries", StringComparison.Ordinal))
        {
            Log($"OPENJEV {model} {decision.Id} {(shift is { } s ? $"shift {s}" : "averaged")}: rejected: {error.Message}");
            return LocalDecisionOutcome.Failed(shift, error);
        }
    }

    private static async Task<Run> LayaAsync(LocalModelManifest manifest, string model, IReadOnlyList<LocalDecisionCase> cases)
    {
        await using var server = await LayaLocalModelTests.StartServerAsync(manifest, model);
        await LayaLocalModelTests.RequireCudaCheckpointAsync(server, model, manifest.Laya.ModelRevision);
        using var runner = new LayaCaseRunner(server.BaseUri, model);

        var run = new Run("laya", model, Mode.LayaCriteriaRotations, [], [], []);
        foreach (var decision in cases)
        {
            var outcomes = run.Outcomes[decision] = [];
            for (var shift = 0; shift < LayaCaseRunner.Rotations(decision); shift++)
                outcomes.Add(await Timed(run, runner.Timer, () => runner.RunAsync(decision, shift, Token)));
        }
        run.Calls.AddRange(runner.Timer.Calls);
        Assert.Equal(cases.Sum(LayaCaseRunner.Rotations), run.Calls.Count);
        Log($"LAYA {model}: {Summary(run)}");
        return run;
    }

    // Metrics. "Mean per rotation": per case, the fraction of its orders answered correctly, averaged over cases.
    // "Worst case": the fraction of cases answered correctly in every order. "Averaged": the averaged answer is correct;
    // for OpenJev that is the AverageRotations mode itself, for Laya the per-option mean of the orders it already answered.

    private static double MeanPerRotation(Run run, Func<LocalDecisionCase, bool> filter) =>
        run.Outcomes.Where(p => filter(p.Key)).Average(p => p.Value.Count(o => o.IsCorrect(p.Key)) / (double)p.Value.Count);

    private static double WorstCase(Run run, Func<LocalDecisionCase, bool> filter) =>
        run.Outcomes.Where(p => filter(p.Key)).Average(p => p.Value.All(o => o.IsCorrect(p.Key)) ? 1.0 : 0.0);

    private static double Averaged(Run run, Func<LocalDecisionCase, bool> filter) =>
        run.Outcomes.Where(p => filter(p.Key)).Average(p => AveragedChoice(p.Key, p.Value) == p.Key.Expected ? 1.0 : 0.0);

    // Judgement only: rejected orders (no answer) are left out. A case with no answered order is left out entirely.
    private static (int Correct, int Cases) JudgedWorstCase(Run run) =>
        Tally(run, outcomes => outcomes.All(o => o.Item2.IsCorrect(o.Item1)));

    private static (int Correct, int Cases) JudgedAveraged(Run run) =>
        Tally(run, outcomes => AveragedChoice(outcomes[0].Item1, outcomes.Select(o => o.Item2).ToList()) == outcomes[0].Item1.Expected);

    private static (int Correct, int Cases) Tally(Run run, Func<List<(LocalDecisionCase, LocalDecisionOutcome)>, bool> correct)
    {
        var answered = run.Outcomes.Select(p => p.Value.Where(o => o.Error is null).Select(o => (p.Key, o)).ToList()).Where(l => l.Count > 0).ToList();
        return (answered.Count(correct), answered.Count);
    }

    private static int Rejected(Run run) => run.Outcomes.Values.Sum(o => o.Count(x => x.Error is not null));

    private static string Fraction((int Correct, int Cases) tally) =>
        $"{Percent(tally.Correct / (double)tally.Cases)} ({tally.Correct}/{tally.Cases})";

    /// <summary>The single averaged answer, or (for per-order outcomes) the argmax of the per-option mean.</summary>
    private static string AveragedChoice(LocalDecisionCase decision, List<LocalDecisionOutcome> outcomes)
    {
        if (outcomes.Count == 1) return outcomes[0].Error is null ? outcomes[0].Choice : "error";
        if (outcomes.Any(o => o.Error is not null)) return "error";
        var means = outcomes[0].Probabilities.Keys.ToDictionary(k => k, k => outcomes.Average(o => o.Probabilities[k]));
        if (decision.Boolean) return means[LocalDecisionCase.Yes] >= OpenJevCaseRunner.BooleanThreshold ? "true" : "false";
        // First maximum in the case's own option order, as the flow picks it.
        return means.Aggregate((best, next) => next.Value > best.Value ? next : best).Key;
    }

    private static string Summary(Run run) => run.Mode == Mode.AveragedRotations
        ? $"accuracy {Percent(Averaged(run, _ => true))}, judged {Fraction(JudgedAveraged(run))}, {Rejected(run)} rejected, {run.Calls.Count} calls"
        : $"mean per rotation {Percent(MeanPerRotation(run, _ => true))}, worst case {Percent(WorstCase(run, _ => true))}, " +
          $"judged worst case {Fraction(JudgedWorstCase(run))}, {Rejected(run)} rejected, {run.Calls.Count} calls";

    private static string Percent(double value) => (value * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

    private static string Milliseconds(List<TimeSpan> calls)
    {
        var sorted = calls.Select(c => c.TotalMilliseconds).Order().ToArray();
        var median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return median.ToString("F0", CultureInfo.InvariantCulture) + " ms";
    }

    private static string ModeName(Mode mode) => mode switch
    {
        Mode.FixedRotations => "fixed, every rotation",
        Mode.AveragedRotations => "rotation-averaged",
        _ => "every criteria rotation",
    };

    private static string Report(LocalModelManifest manifest, IReadOnlyList<LocalDecisionCase> cases, List<Run> runs, TimeSpan duration)
    {
        Func<LocalDecisionCase, bool> all = _ => true;
        var groups = new (string Name, Func<LocalDecisionCase, bool> Filter)[]
        {
            ("en", c => c.Language == "en"), ("pt", c => c.Language == "pt"), ("choice", c => !c.Boolean), ("boolean", c => c.Boolean),
        };
        string Count(Func<LocalDecisionCase, bool> filter) => cases.Count(filter).ToString(CultureInfo.InvariantCulture);
        string Cell(Run run, Func<LocalDecisionCase, bool> filter) => run.Mode == Mode.AveragedRotations
            ? Percent(Averaged(run, filter))
            : $"{Percent(MeanPerRotation(run, filter))} / {Percent(WorstCase(run, filter))}";

        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');
        Line("# Local decision model benchmark");
        Line();
        Line("Generated by `LocalDecisionBenchmarkTests.Benchmark_local_decision_models` " +
             "(`dotnet test tests/Cortex.Inference.Tests --filter Category=LocalModelsBenchmark`). Overwritten on every run.");
        Line();
        Line($"- Date: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");
        Line($"- GPU: {Gpu()}");
        Line($"- Corpus: `tests/fixtures/local-decision-cases.json`, {cases.Count} cases ({Count(c => c.Language == "en")} en, " +
             $"{Count(c => c.Language == "pt")} pt; {Count(c => !c.Boolean)} choice with 2 to 5 options, {Count(c => c.Boolean)} boolean)");
        Line($"- Run time: {duration.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture)} min, one server at a time");
        Line();
        Line("## Pinned versions");
        Line();
        Line($"- llama.cpp `{manifest.LlamaCpp.Tag}` ({string.Join(", ", manifest.LlamaCpp.Assets.Select(a => $"`{a.FileName}`"))})");
        foreach (var model in OpenJevModels)
        {
            var pin = manifest.Model(model);
            Line($"- `{model}`: `{pin.Repo}@{pin.Revision}`, `{pin.File}`");
        }
        Line($"- Laya: `{manifest.Laya.Package}`, weights `convaiinnovations/laya@{manifest.Laya.ModelRevision}`, `{manifest.Laya.Torch}`");
        Line();
        Line("## Summary");
        Line();
        Line("- **Mean per rotation**: per case, the fraction of its option orders answered correctly, averaged over the cases.");
        Line("- **Worst case**: the fraction of cases answered correctly in *every* option order.");
        Line("- **Averaged**: OpenJev `rotation-averaged` rows are `OpenJevOptionOrder.AverageRotations` itself (one call per order, " +
             "probabilities averaged; a rotation the provider rejects rejects the whole averaged answer). For Laya it is computed afterwards from the orders already asked (per-option mean, argmax); " +
             "Laya has no averaging mode.");
        Line("- An OpenJev boolean has two orders (`Yes/No`, `No/Yes`); a Laya boolean is one `noul` question with a single order, " +
             "so a Laya worst case over all cases is flattered by its booleans. **Worst case (choice)** compares order stability on the " +
             $"{Count(c => !c.Boolean)} choice cases only, where every provider is asked every order.");
        Line("- **Rejected**: answers the provider refused (OpenJev: a label missing from the top 20 logprobs). Rotation rows count " +
             "rejected orders; rotation-averaged rows count rejected questions (one rejected order rejects the whole averaged answer). " +
             "Rejections count as wrong in every accuracy column, as they fail in production.");
        Line("- **Judged worst case / judged averaged**: the same metrics with rejected orders left out, i.e. only the orders the model " +
             "actually answered (a case with no answered order is left out). This separates the model's judgement from rejections.");
        Line();
        Line("| Model | Mode | Mean per rotation | Worst case | Worst case (choice) | Averaged | Rejected | Judged worst case | Judged averaged |");
        Line("|---|---|---|---|---|---|---|---|---|");
        Func<LocalDecisionCase, bool> choiceOnly = c => !c.Boolean;
        foreach (var run in runs)
        {
            var rotations = run.Mode != Mode.AveragedRotations;
            var averaged = run.Mode != Mode.FixedRotations;
            Line($"| {run.Provider} `{run.Model}` | {ModeName(run.Mode)} | {(rotations ? Percent(MeanPerRotation(run, all)) : "—")} | " +
                 $"{(rotations ? Percent(WorstCase(run, all)) : "—")} | {(rotations ? Percent(WorstCase(run, choiceOnly)) : "—")} | " +
                 $"{(averaged ? Percent(Averaged(run, all)) : "—")} | {Rejected(run)} | " +
                 $"{(rotations ? Fraction(JudgedWorstCase(run)) : "—")} | {(averaged ? Fraction(JudgedAveraged(run)) : "—")} |");
        }
        Line();
        Line("Cost. **Per HTTP call**: median wall time of one inference call (a chat completion or a `/v1/systemone` request; " +
             "label tokenization is not counted). **Per decision**: median summed call time of one answer, so a rotation-averaged " +
             "answer costs n calls. Calls is the number of inference calls.");
        Line();
        Line("| Model | Mode | Median per HTTP call | Median per decision | Calls |");
        Line("|---|---|---|---|---|");
        foreach (var run in runs)
            Line($"| {run.Provider} `{run.Model}` | {ModeName(run.Mode)} | {Milliseconds(run.Calls)} | {Milliseconds(run.Decisions)} | {run.Calls.Count} |");
        Line();
        Line("By language and type. Rotation rows show *mean per rotation / worst case*; rotation-averaged rows show the averaged accuracy.");
        Line();
        Line($"| Model | Mode | {string.Join(" | ", groups.Select(g => $"{g.Name} ({Count(g.Filter)})"))} |");
        Line($"|---|---|{string.Concat(groups.Select(_ => "---|"))}");
        foreach (var run in runs)
            Line($"| {run.Provider} `{run.Model}` | {ModeName(run.Mode)} | {string.Join(" | ", groups.Select(g => Cell(run, g.Filter)))} |");
        Line();
        Line("## Wrong answers");
        Line();
        Line("Per model and mode, every case with at least one wrong answer. P(expected) is the probability the model gave the " +
             "expected answer in each order, in rotation order (shift 0 is the corpus order).");
        foreach (var run in runs)
        {
            Line();
            Line($"### {run.Provider} `{run.Model}`, {ModeName(run.Mode)}");
            Line();
            var wrong = run.Outcomes.Where(p => p.Value.Any(o => !o.IsCorrect(p.Key))).ToArray();
            if (wrong.Length == 0) Line("None.");
            foreach (var (decision, outcomes) in wrong)
            {
                var kind = decision.Boolean ? "boolean" : $"choice/{decision.Options.Length}";
                var probabilities = string.Join(" / ", outcomes.Select(o => o.FormatExpected(decision, "F3")));
                var answers = string.Join(", ", outcomes.Where(o => !o.IsCorrect(decision)).Select(o =>
                    (o.Shift is { } shift ? $"shift {shift}: " : "") + (o.Error is null ? $"`{o.Choice}`" : $"rejected ({o.Error})")).Distinct());
                Line(run.Mode == Mode.AveragedRotations
                    ? $"- `{decision.Id}` ({decision.Language}, {kind}): expected `{decision.Expected}`, answered {answers}; P(expected) = {probabilities}"
                    : $"- `{decision.Id}` ({decision.Language}, {kind}): expected `{decision.Expected}`, wrong in " +
                      $"{outcomes.Count(o => !o.IsCorrect(decision))}/{outcomes.Count} orders ({answers}); P(expected) = {probabilities}");
            }
        }
        return text.ToString();
    }

    private static string Gpu()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=name,driver_version --format=csv,noheader")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return output.Length == 0 ? "unknown" : output.Replace("\r", "").Replace("\n", "; ") + " (name, driver)";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "unknown (nvidia-smi unavailable)";
        }
    }
}
