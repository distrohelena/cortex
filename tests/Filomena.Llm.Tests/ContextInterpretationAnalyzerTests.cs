using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Llm.Context;
using Xunit;
using static Filomena.Llm.Tests.SessionParticipationReviewTests;

namespace Filomena.Llm.Tests;

public sealed class ContextInterpretationAnalyzerTests
{
    internal static ContextInterpretationSettings Settings => new("gpt-6-luna", "openai", "medium", 2048, 8, 8, 2000);

    [Theory]
    [InlineData("provocacao", "high", "still_relevant", true, ReassessmentOpportunity.Relevant)]
    [InlineData("ambigua", "high", "still_relevant", false, ReassessmentOpportunity.Relevant)]
    [InlineData("sarcasmo", "medium", "still_relevant", false, ReassessmentOpportunity.Relevant)]
    [InlineData("ironia", "high", "obsolete", true, ReassessmentOpportunity.Obsolete)]
    public async Task Adapter_preserves_contract_and_maps_only_visible_opaque_evidence(
        string reading, string confidence, string opportunity, bool confident, ReassessmentOpportunity expected)
    {
        await using var h = await Harness.Start();
        var model = new Completion(reading, confidence, opportunity);
        var analyzer = new ContextInterpretationAnalyzer(model, Settings);
        var history = h.Runtime.ReadHistory(h.Session);
        var result = await analyzer.ReassessAsync(new(h.Runtime.ReadSession(h.Session)!, history[0], history, false), TestContext.Current.CancellationToken);
        Assert.Equal(confident, result.Confident);
        Assert.Equal(expected, result.Opportunity);
        Assert.Equal(reading, result.Interpretation!.Reading);
        Assert.Equal(confidence, result.Interpretation.Confidence);
        Assert.Equal(["clarification"], result.EvidenceMessageIds);
        var request = Assert.Single(model.Requests);
        Assert.Equal("gpt-6-luna", request.Model);
        Assert.Equal("medium", request.ReasoningEffort);
        Assert.Equal(IronyReplayExperiment.Prompt, request.System);
        Assert.Equal("reassessment", request.Inputs.GetProperty("phase").GetString());
        Assert.Equal("Agora com 1000.", request.Inputs.GetProperty("target").GetProperty("text").GetString());
        Assert.DoesNotContain(h.Session.AccountId, request.Inputs.GetRawText());
        Assert.DoesNotContain(h.Session.NativeConversationId, request.Inputs.GetRawText());
        Assert.DoesNotContain("fixture:helena", request.Inputs.GetRawText());
        Assert.DoesNotContain("quoted-pi", request.Inputs.GetRawText());
        Assert.Contains("Aqui est", request.Inputs.GetProperty("target").GetProperty("quote").GetRawText());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("outage")]
    [InlineData("quote-only")]
    public async Task Invalid_evidence_or_backend_failure_does_not_become_a_reading(string scenario)
    {
        await using var h = await Harness.Start();
        var model = new Completion("ironia", "high", "still_relevant") { Scenario = scenario };
        var analyzer = new ContextInterpretationAnalyzer(model, Settings);
        var history = h.Runtime.ReadHistory(h.Session);
        var error = await Record.ExceptionAsync(() => analyzer.ReassessAsync(new(h.Runtime.ReadSession(h.Session)!, history[0], history, false),
            TestContext.Current.CancellationToken));
        if (scenario == "outage") Assert.IsType<HttpRequestException>(error);
        else Assert.IsType<InvalidDataException>(error);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("already-answered")]
    [InlineData("outbound")]
    public async Task Wrong_scope_or_already_answered_target_rejects_before_completion(string scenario)
    {
        await using var h = await Harness.Start();
        var history = h.Runtime.ReadHistory(h.Session);
        var snapshot = h.Runtime.ReadSession(h.Session)!;
        var original = history[0];
        if (scenario == "account") snapshot = snapshot with { Session = snapshot.Session with { AccountId = "foreign" } };
        if (scenario == "outbound") original = original with { Direction = SessionMessageDirection.Outbound };
        var model = new Completion("ironia", "high", "still_relevant");
        var analyzer = new ContextInterpretationAnalyzer(model, Settings);
        await Assert.ThrowsAsync<InvalidDataException>(() => analyzer.ReassessAsync(new(snapshot, original, history, scenario == "already-answered"),
            TestContext.Current.CancellationToken));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Truncated_context_declares_gap_and_does_not_expand_evidence_scope()
    {
        await using var h = await Harness.Start();
        var history = h.Runtime.ReadHistory(h.Session);
        var model = new Completion("ironia", "high", "still_relevant");
        var analyzer = new ContextInterpretationAnalyzer(model, Settings with { MaxTextCharacters = 8, BeforeMessages = 0, AfterMessages = 1 });
        await analyzer.ReassessAsync(new(h.Runtime.ReadSession(h.Session)!, history[0], history, false), TestContext.Current.CancellationToken);
        var input = Assert.Single(model.Requests).Inputs;
        Assert.Equal("Agora co", input.GetProperty("target").GetProperty("text").GetString());
        Assert.Contains("text_truncated", input.GetProperty("gaps").EnumerateArray().Select(x => x.GetString()));
        Assert.Single(input.GetProperty("after").EnumerateArray());
    }

    [Fact]
    public async Task Initial_adapter_does_not_include_later_context_even_when_available_in_snapshot()
    {
        await using var h = await Harness.Start();
        var history = h.Runtime.ReadHistory(h.Session);
        var model = new Completion("ambigua", "low", "uncertain");
        var analyzer = new ContextInterpretationAnalyzer(model, Settings);
        var result = await analyzer.AnalyzeInitialAsync(new(h.Runtime.ReadSession(h.Session)!, history[0], history, false),
            TestContext.Current.CancellationToken);
        var input = Assert.Single(model.Requests).Inputs;
        Assert.Equal("initial", input.GetProperty("phase").GetString());
        Assert.Empty(input.GetProperty("after").EnumerateArray());
        Assert.DoesNotContain("Era brincadeira", input.GetRawText());
        Assert.False(result.Confident);
        Assert.Equal(["original"], result.EvidenceMessageIds);
    }

    internal sealed class Completion(string reading, string confidence, string opportunity) : IFlowCompletion
    {
        internal string? Scenario;
        internal List<FlowCompletionRequest> Requests { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Requests.Add(request);
            if (Scenario == "outage") throw new HttpRequestException("fixture outage");
            if (Scenario == "missing") return Task.FromResult("{}");
            var initial = request.Inputs.GetProperty("phase").GetString() == "initial";
            var uncertain = initial && request.Inputs.GetProperty("target").GetProperty("text").GetString()!.Contains("1000", StringComparison.Ordinal);
            var evidence = Scenario == "foreign" ? "unseen" : Scenario == "quote-only"
                ? request.Inputs.GetProperty("target").GetProperty("quote").GetProperty("id").GetString()
                : initial ? request.Inputs.GetProperty("target").GetProperty("id").GetString()
                : request.Inputs.GetProperty("after")[0].GetProperty("id").GetString();
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                reading = Scenario == "initial-policy" && initial ? uncertain ? "ambigua" : "literal" : reading,
                alternative = (string?)null,
                confidence = Scenario == "initial-policy" && initial ? uncertain ? "low" : "high" : confidence,
                evidenceIds = new[] { evidence },
                explanation = "O contexto posterior esclarece a brincadeira.",
                responseOpportunity = opportunity
            }));
        }
    }
}
