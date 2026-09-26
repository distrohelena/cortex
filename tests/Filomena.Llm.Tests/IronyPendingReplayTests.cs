using System.Text.Json;
using Xunit;

namespace Filomena.Llm.Tests;

// Synthetic queue-entry experiment, not a live scheduler or participation policy.
public sealed class IronyPendingReplayTests
{
    [Fact]
    public void Saved_private_results_replay_without_new_model_calls()
    {
        var path = Environment.GetEnvironmentVariable("FILOMENA_IRONY_QUEUE_REPLAY_FILE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Explicit read-only replay of private saved analyses.");
        Assert.True(Path.IsPathFullyQualified(path!));
        var rows = File.ReadLines(path!).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();
        Assert.Equal(8, rows.Length);
        var cases = rows.GroupBy(row => row.GetProperty("id").GetString()).ToArray();
        Assert.Equal(4, cases.Length);
        var changed = 0;
        foreach (var pair in cases)
        {
            var first = Assert.Single(pair, row => !row.GetProperty("revisit").GetBoolean());
            var later = Assert.Single(pair, row => row.GetProperty("revisit").GetBoolean());
            Assert.Equal(first.GetProperty("corpusHash").GetString(), later.GetProperty("corpusHash").GetString());
            Assert.Equal(first.GetProperty("promptHash").GetString(), later.GetProperty("promptHash").GetString());
            var pending = new PendingIronyReplay(first.GetProperty("input"), first.GetProperty("output").GetString()!);
            var initialReading = pending.CurrentReading;
            var context = later.GetProperty("input");
            pending.Advance(context, context.GetProperty("after").GetArrayLength());
            var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
            Assert.Equal("closed_obsolete", pending.Complete(job, later.GetProperty("output").GetString()!));
            Assert.Null(pending.Take());
            if (initialReading != pending.CurrentReading) changed++;
            TestContext.Current.TestOutputHelper!.WriteLine($"{pair.Key}: {initialReading} -> {pending.CurrentReading}; {pending.State}");
        }
        // Pinned previous observations, not semantic ground truth or new inference.
        Assert.Equal(2, changed);
    }

    internal static JsonElement Input(int after = 0, string group = "group-a", string target = "m0") =>
        IronyReplayExperiment.Input(JsonSerializer.SerializeToElement(new
        {
            id = "synthetic-pending",
            groupRef = group,
            gaps = new[] { "synthetic_controlled_fixture" },
            before = Array.Empty<object>(),
            target = new
            {
                id = target,
                speaker = "person-a",
                text = "Agora com 1000.",
                quote = new { id = "quote-original", text = "Aqui estão 100 casas de pi." }
            },
            after = Enumerable.Range(1, after).Select(i => new
            {
                id = $"m{i}",
                speaker = "person-b",
                text = i == 1 ? "Era brincadeira." : "Outro assunto."
            }).ToArray()
        }), true);

    internal static string Reading(string reading = "ambigua", string confidence = "low",
        string opportunity = "uncertain", string evidence = "m0") => JsonSerializer.Serialize(new
        {
            reading,
            confidence,
            alternative = (string?)null,
            evidenceIds = new[] { evidence },
            explanation = "Controlled fixture, not model inference.",
            responseOpportunity = opportunity
        });

    [Fact]
    public void Uncertain_item_waits_for_new_context_and_does_not_duplicate_inflight_work()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        Assert.Null(pending.Take());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Null(pending.Take());
        Assert.Equal("m0", job.Input.GetProperty("target").GetProperty("id").GetString());
        Assert.Equal("person-a", job.Input.GetProperty("target").GetProperty("speaker").GetString());
        Assert.Equal("quote-original", job.Input.GetProperty("target").GetProperty("quote").GetProperty("id").GetString());
        Assert.Equal("synthetic_controlled_fixture", job.Input.GetProperty("gaps")[0].GetString());
    }

    [Fact]
    public void Better_interpretation_only_requests_separate_participation_review()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Equal("needs_participation_review", pending.Complete(job, Reading("provocacao", "high", "still_relevant", "m1")));
        Assert.Equal("provocacao", pending.CurrentReading);
        Assert.Null(pending.Take());
    }

    [Fact]
    public void Improved_understanding_does_not_reopen_an_obsolete_opportunity()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Equal("closed_obsolete", pending.Complete(job, Reading("provocacao", "high", "obsolete", "m1")));
        Assert.Equal("provocacao", pending.CurrentReading);
        pending.Advance(Input(2), 2);
        Assert.Null(pending.Take());
    }

    [Theory]
    [InlineData(true, 2, "closed_topic")]
    [InlineData(false, 150, "closed_distance")]
    public void Migration_or_large_backlog_closes_even_an_inflight_reassessment(bool migrated, int distance, string reason)
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        // 150 is an explicit experiment horizon, NOT a proposed product constant.
        pending.Advance(Input(distance), distance, migrated);
        Assert.Equal(reason, pending.Complete(job, Reading("provocacao", "high", "still_relevant", "m1")));
        Assert.Null(pending.Take());
        Assert.Equal("ambigua", pending.CurrentReading);
    }

    [Fact]
    public void Duplicate_context_does_not_schedule_again_and_budget_is_bounded()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        for (var i = 1; i <= 2; i++)
        {
            pending.Advance(Input(i), i);
            var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
            pending.Complete(job, Reading());
            pending.Advance(Input(i), i);
            Assert.Null(pending.Take());
        }
        pending.Advance(Input(3), 3);
        Assert.Null(pending.Take());
        Assert.Equal("closed_budget", pending.State);
    }

    [Fact]
    public void Newer_context_makes_old_result_stale_without_replacing_the_reading()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        pending.Advance(Input(2), 2);
        Assert.Equal("stale_context", pending.Complete(job, Reading("provocacao", "high", "still_relevant", "m1")));
        Assert.Equal("ambigua", pending.CurrentReading);
        Assert.NotNull(pending.Take());
    }

    [Fact]
    public void Completed_job_cannot_be_applied_twice_or_to_a_new_run()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        pending.Complete(job, Reading());
        Assert.Throws<InvalidOperationException>(() => pending.Complete(job, Reading()));
        var restarted = new PendingIronyReplay(Input(), Reading());
        restarted.Advance(Input(1), 1);
        Assert.NotNull(restarted.Take());
        Assert.Throws<InvalidOperationException>(() => restarted.Complete(job, Reading()));
    }

    [Theory]
    [InlineData("other-group", "m0")]
    [InlineData("group-a", "other-target")]
    public void Context_from_another_conversation_or_target_is_rejected(string group, string target)
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        Assert.Throws<InvalidDataException>(() => pending.Advance(Input(1, group, target), 1));
        Assert.Null(pending.Take());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    public void Invalid_result_is_failure_not_literal_or_permission_and_does_not_busy_retry(string output)
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Equal("analysis_failed", pending.Complete(job, output));
        Assert.Equal("ambigua", pending.CurrentReading);
        Assert.Null(pending.Take());
    }

    [Fact]
    public void Fabricated_evidence_cannot_change_the_reading()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Equal("analysis_failed", pending.Complete(job, Reading("ironia", "high", "still_relevant", "future")));
        Assert.Equal("ambigua", pending.CurrentReading);
    }

    [Fact]
    public void Already_handled_original_cannot_be_reopened_by_later_understanding()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        pending.MarkHandled();
        Assert.Equal("closed_handled", pending.Complete(job, Reading("ironia", "high", "still_relevant")));
        pending.Advance(Input(2), 2);
        Assert.Null(pending.Take());
    }

    [Fact]
    public void Clear_initial_reading_is_not_queued_just_because_more_messages_arrive()
    {
        var pending = new PendingIronyReplay(Input(), Reading("literal", "high", "still_relevant"));
        pending.Advance(Input(1), 1);
        Assert.Null(pending.Take());
    }

    [Fact]
    public void Horizon_closes_before_scheduling_at_150_not_at_149()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(149), 149);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Equal(149, job.Input.GetProperty("after").GetArrayLength());
        pending.Complete(job, Reading());
        pending.Advance(Input(150), 150);
        Assert.Null(pending.Take());
        Assert.Equal("closed_distance", pending.State);
    }

    [Fact]
    public void Changed_content_under_same_message_id_is_not_new_context()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(1), 1);
        using var changed = JsonDocument.Parse(Input(1).GetRawText().Replace("Era brincadeira.", "Não era brincadeira."));
        Assert.Throws<InvalidDataException>(() => pending.Advance(changed.RootElement, 1));
    }

    [Fact]
    public void Regressive_distance_is_rejected_without_changing_current_job()
    {
        var pending = new PendingIronyReplay(Input(), Reading());
        pending.Advance(Input(2), 2);
        var job = Assert.IsType<PendingIronyReplay.Job>(pending.Take());
        Assert.Throws<InvalidDataException>(() => pending.Advance(Input(1), 1));
        Assert.Equal("needs_participation_review", pending.Complete(job, Reading("provocacao", "high", "still_relevant")));
    }
}

// Throwaway offline state-machine probe. No callbacks, model client, sender, store or timer.
internal sealed class PendingIronyReplay
{
    internal sealed record Job(JsonElement Input, int Version);
    private JsonElement _input;
    private Job? _inflight;
    private int _version;
    private int _takenVersion;
    private int _attempts;
    private int _distance;
    private readonly int _horizon;
    private readonly int _budget;

    internal PendingIronyReplay(JsonElement input, string initial, int horizon = 150, int budget = 2)
    {
        if (horizon < 1 || budget < 1) throw new ArgumentOutOfRangeException(nameof(horizon));
        IronyReplayExperiment.Validate(initial, input);
        _input = input.Clone();
        _horizon = horizon;
        _budget = budget;
        using var parsed = JsonDocument.Parse(initial);
        CurrentReading = parsed.RootElement.GetProperty("reading").GetString()!;
        State = parsed.RootElement.GetProperty("responseOpportunity").GetString() == "obsolete"
            ? "closed_obsolete" : IsUncertain(parsed.RootElement) ? "pending" : "closed_clear";
    }

    internal string State { get; private set; }
    internal string CurrentReading { get; private set; }

    internal void Advance(JsonElement input, int distance, bool topicClosed = false)
    {
        // Validate the existing analyzer input contract without invoking a model.
        IronyReplayExperiment.Validate("""
            {"reading":"ambigua","alternative":null,"confidence":"low","evidenceIds":[],
             "explanation":"Input validation only.","responseOpportunity":"uncertain"}
            """, input);
        if (input.GetProperty("groupRef").GetString() != _input.GetProperty("groupRef").GetString()
            || input.GetProperty("caseId").GetString() != _input.GetProperty("caseId").GetString()
            || input.GetProperty("target").GetRawText() != _input.GetProperty("target").GetRawText()
            || distance < _distance || distance < input.GetProperty("after").GetArrayLength())
            throw new InvalidDataException("Replay scope/order mismatch.");
        var previous = _input.GetProperty("before").EnumerateArray()
            .Concat(_input.GetProperty("after").EnumerateArray())
            .ToDictionary(m => m.GetProperty("id").GetString()!, m => m.GetRawText());
        foreach (var message in input.GetProperty("before").EnumerateArray()
                     .Concat(input.GetProperty("after").EnumerateArray()))
        {
            if (previous.TryGetValue(message.GetProperty("id").GetString()!, out var old)
                && old != message.GetRawText())
                throw new InvalidDataException("Changed message requires an explicit edit event, outside this spike.");
        }
        if (State != "pending") return;
        if (topicClosed) State = "closed_topic";
        else if (distance >= _horizon) State = "closed_distance";
        if (input.GetRawText() != _input.GetRawText() || distance != _distance)
        {
            _input = input.Clone();
            _distance = distance;
            _version++;
        }
    }

    internal Job? Take()
    {
        if (State != "pending" || _inflight is not null || _version == _takenVersion) return null;
        if (_attempts >= _budget) { State = "closed_budget"; return null; }
        _attempts++;
        _takenVersion = _version;
        return _inflight = new Job(_input.Clone(), _version);
    }

    internal string Complete(Job job, string output)
    {
        // Reference identity binds completion to this run's exact reservation.
        if (!ReferenceEquals(job, _inflight)) throw new InvalidOperationException("Unknown or consumed replay job.");
        _inflight = null;
        if (State != "pending") return State;
        if (job.Version != _version) return "stale_context";
        try { IronyReplayExperiment.Validate(output, job.Input); }
        catch (InvalidDataException) { return "analysis_failed"; }
        using var parsed = JsonDocument.Parse(output);
        var result = parsed.RootElement;
        CurrentReading = result.GetProperty("reading").GetString()!;
        if (result.GetProperty("responseOpportunity").GetString() == "obsolete")
            return State = "closed_obsolete";
        if (IsUncertain(result)) return "pending";
        State = "closed_interpreted";
        return result.GetProperty("responseOpportunity").GetString() == "still_relevant"
            ? "needs_participation_review" : "interpreted_opportunity_uncertain";
    }

    internal void MarkHandled() => State = "closed_handled";

    private static bool IsUncertain(JsonElement result) =>
        result.GetProperty("reading").GetString() == "ambigua"
        || result.GetProperty("confidence").GetString() != "high";
}
