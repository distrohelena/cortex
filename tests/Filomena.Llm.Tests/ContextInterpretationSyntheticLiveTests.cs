using System.Diagnostics;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Filomena.Llm.Context;
using Cortex.Inference.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextInterpretationSyntheticLiveTests
{
    [Fact]
    public async Task Synthetic_context_pairs_use_real_analyzer_without_private_history_or_transport()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_IRONY_SYNTHETIC_LIVE") != "1", "Explicit six-call synthetic model experiment.");
        var outputDirectory = Environment.GetEnvironmentVariable("FILOMENA_IRONY_SYNTHETIC_OUTPUT")
            ?? throw new InvalidOperationException("Output directory required.");
        Assert.True(Path.IsPathFullyQualified(outputDirectory) && Directory.Exists(outputDirectory));
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key));
        using var usage = new Usage();
        using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
        var analyzer = new ContextInterpretationAnalyzer(new OpenAiFlowCompletion(http, () => key), ContextInterpretationAnalyzerTests.Settings);
        await using var fixture = await SessionParticipationReviewTests.Harness.Start();
        var snapshot = fixture.Runtime.ReadSession(fixture.Session)!;
        var seed = fixture.Runtime.ReadHistory(fixture.Session);
        var path = Path.Combine(outputDirectory, $"synthetic-context-{Guid.NewGuid():N}.jsonl");
        await using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var cases = new[]
        {
            (Id: "praise-clarified", Original: "Nossa, que maravilha.", Later: "Foi ironia: o aplicativo apagou o rascunho. Já recuperei, podemos mudar de assunto."),
            (Id: "literal-clarified", Original: "Nossa, que maravilha.", Later: "Estou falando sério, o aplicativo recuperou o rascunho inteiro. Fiquei feliz."),
            (Id: "playful-request", Original: "Agora organiza meus próximos mil anos também.", Later: "Estou brincando com esse planejamento exagerado, não quero outra lista. Vamos falar do filme.")
        };
        foreach (var item in cases)
        {
            SessionHistoryEntry[] history =
            [
                seed[0] with { Message = Message(item.Original) },
                seed[1] with { Message = Message(item.Later) }
            ];
            var context = new ParticipationReassessmentContext(snapshot, history[0], history, false);
            foreach (var revisit in new[] { false, true })
            {
                usage.Value = null;
                var start = Stopwatch.GetTimestamp();
                var result = revisit
                    ? await analyzer.ReassessAsync(context, TestContext.Current.CancellationToken)
                    : await analyzer.AnalyzeInitialAsync(context, TestContext.Current.CancellationToken);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    item.Id,
                    revisit,
                    item.Original,
                    laterContext = revisit ? item.Later : null,
                    result,
                    milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    usage = usage.Value,
                    model = "gpt-6-luna",
                    effort = "medium",
                    transportCalls = 0,
                    graphitiCalls = 0,
                    memoryWrites = 0,
                    cost = (decimal?)null
                }));
                Assert.NotNull(result.Interpretation);
                Assert.All(result.EvidenceMessageIds, id => Assert.Contains(id, revisit ? new[] { "original", "clarification" } : ["original"]));
            }
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"Synthetic report: {path}");
    }

    private static JsonElement Message(string text) => JsonSerializer.SerializeToElement(new
    {
        texto = text,
        remetente = new { nome = "Pessoa fictícia", stableId = "synthetic:person" },
        reply = (object?)null
    });

    internal sealed class Usage() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        internal JsonElement? Value;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (document.RootElement.TryGetProperty("usage", out var value)) Value = value.Clone();
            }
            return response;
        }
    }
}
