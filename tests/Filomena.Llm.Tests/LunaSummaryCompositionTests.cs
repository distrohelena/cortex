using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Shadow;
using Filomena.Llm.OpenAi;
using Xunit;

namespace Filomena.Llm.Tests;

// Characterization of existing composition: only the external HTTP boundary is fake.
public sealed class LunaSummaryCompositionTests
{
    private static readonly SystemOneParticipationConfig Config = new("systemone-participation:v1", "jev-1.13.0",
        ["dm-situation", "group-situation"], ["dm-presence", "group-presence"], []);
    private static JsonNode Candidate()
    {
        var project = SystemOneSummaryCompositionTests.Source();
        foreach (var state in project["project"]!["states"]!.AsArray())
        {
            var node = state!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "group-situation")!;
            node["provider"] = "openai";
            node["model"] = "gpt-6-luna";
            node["reasoningEffort"] = "medium";
            node["maxOutputTokens"] = 4096;
        }
        return project;
    }
    private static PinnedShadowProject Pin(JsonNode project)
    {
        var bytes = Encoding.UTF8.GetBytes(project.ToJsonString());
        return PinnedShadowProject.FromBytes(bytes, "summary-composition-test", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static FlowCompletionRequest Request(string id = "group-situation") => new(id, "Preserve the actual speaker and recipient.",
        JsonSerializer.SerializeToElement(new { mensagens = "Helena: nada só vim enxer mesmo" }),
        JsonDocument.Parse("""{"type":"object","additionalProperties":false,"properties":{"envolvimento":{"type":"string","enum":["direto","nenhum"]},"resumo":{"type":"string"}},"required":["envolvimento","resumo"]}""").RootElement.Clone(),
        "gpt-6-luna", "openai", null, "medium", 4096);
    private static IFlowCompletion Compose(HttpClient http, JsonNode project, IFlowCompletion? local = null) =>
        SystemOneParticipationHost.Create(Config, Pin(project), new ProviderFlowCompletion(new Dictionary<string, IFlowCompletion>
        {
            ["openai"] = new OpenAiFlowCompletion(http, () => "test-credential"),
            ["lmstudio"] = local ?? new RejectCompletion()
        }), http, _ => "test-credential");

    [Fact]
    public async Task Jev_fields_merge_with_luna_summary_without_changing_prompt_or_inputs()
    {
        using var handler = new Services();
        using var http = new HttpClient(handler);
        var request = Request();
        using var result = JsonDocument.Parse(await Compose(http, Candidate()).CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal("direto", result.RootElement.GetProperty("envolvimento").GetString());
        Assert.Equal("Helena diz que só veio encher.", result.RootElement.GetProperty("resumo").GetString());
        var typed = Assert.Single(handler.Typed);
        Assert.Equal("jev-1.13.0", typed.GetProperty("model").GetString());
        Assert.False(typed.GetProperty("questions").TryGetProperty("resumo", out _));
        var summary = Assert.Single(handler.Summaries);
        Assert.Equal("gpt-6-luna", summary.GetProperty("model").GetString());
        Assert.False(summary.GetProperty("store").GetBoolean());
        Assert.Equal("medium", summary.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(4096, summary.GetProperty("max_output_tokens").GetInt32());
        Assert.StartsWith(request.System, summary.GetProperty("instructions").GetString());
        Assert.Equal(request.Inputs.GetRawText(), summary.GetProperty("input")[0].GetProperty("content").GetString());
        Assert.Equal("resumo", Assert.Single(summary.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").EnumerateObject()).Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"resumo\":null}")]
    [InlineData("{\"resumo\":\" \"}")]
    [InlineData("{\"resumo\":\"ok\",\"envolvimento\":\"nenhum\"}")]
    [InlineData("{\"resumo\":\"ok\",\"resumo\":\"other\"}")]
    public async Task Invalid_luna_summary_never_releases_partial_typed_result(string summary)
    {
        using var handler = new Services(summary);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Compose(http, Candidate()).CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Single(handler.Typed);
        Assert.Single(handler.Summaries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_outage_propagates_without_local_fallback(bool typedFailure)
    {
        using var handler = new Services(failTyped: typedFailure, failSummary: !typedFailure);
        using var http = new HttpClient(handler);
        var local = new SystemOneOperationalCompositionTests.Completion("{\"resumo\":\"fallback must not execute\"}");
        await Assert.ThrowsAnyAsync<Exception>(() => Compose(http, Candidate(), local).CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Single(handler.Typed);
        Assert.Equal(typedFailure ? 0 : 1, handler.Summaries.Count);
        Assert.Empty(local.Requests);
    }

    [Fact]
    public async Task Unchanged_local_node_does_not_use_luna()
    {
        using var handler = new Services();
        using var http = new HttpClient(handler);
        var local = new SystemOneOperationalCompositionTests.Completion("unchanged");
        var request = Request("another-node") with { Provider = "lmstudio", Model = "original-model" };
        Assert.Equal("unchanged", await Compose(http, Candidate(), local).CompleteAsync(request, TestContext.Current.CancellationToken));
        Assert.Same(request, Assert.Single(local.Requests));
        Assert.Empty(handler.Typed);
        Assert.Empty(handler.Summaries);
    }

    [Theory]
    [InlineData("participation-inactive-v1", "direto")]
    [InlineData("participation-attentive-v1", "direto")]
    [InlineData("participation-engaged-v1", "direto")]
    [InlineData("participation-engaged-v1", "indireto")]
    public async Task Real_runtime_keeps_direct_bypass_and_non_direct_presence(string state, string involvement)
    {
        var candidate = Candidate();
        using var handler = new Services(involvement: involvement);
        using var http = new HttpClient(handler);
        var run = await ParticipationExperiments.ExecuteAsync(new(1, "summary composition", state,
            [new("Helena", "Filomena, vim só te perturbar")]), false, Compose(http, candidate), null, "none",
            TestContext.Current.CancellationToken, SessionEntryKind.Group,
            DirectParticipationRuleTests.ParticipationProject(state, sourceProject: candidate));
        Assert.True(run.Report.Success, run.Report.Error);
        Assert.Equal(involvement == "direto", DirectParticipationRuleTests.Direct(run));
        Assert.Equal(involvement == "direto" ? 1 : 2, handler.Typed.Count);
        Assert.Single(handler.Summaries);
        Assert.Single(run.Report.Trace, t => t.EventName == "execution-started");
        Assert.DoesNotContain(run.Report.Trace, t => t.EventName == "node-outbound");
    }

    private sealed class RejectCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) => throw new InvalidOperationException("Unexpected fallback or generation");
    }
    private sealed class Services(string summary = "{\"resumo\":\"Helena diz que só veio encher.\"}",
        bool failTyped = false, bool failSummary = false, string involvement = "direto")
        : DelegatingHandler(new SystemOneParticipationTests.Decisions(involvement, "calar", "jev-1.13.0"))
    {
        public List<JsonElement> Typed { get; } = [];
        public List<JsonElement> Summaries { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri!.Host == "api.typesafe.ai")
            {
                Typed.Add(body.RootElement.Clone());
                return failTyped ? new(HttpStatusCode.ServiceUnavailable) : await base.SendAsync(request, token);
            }
            if (request.RequestUri.Host != "api.openai.com" || request.RequestUri.AbsolutePath != "/v1/responses") throw new InvalidOperationException("Unexpected endpoint");
            Summaries.Add(body.RootElement.Clone());
            return failSummary ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] {
                    new { type = "message", content = new[] { new { type = "output_text", text = summary } } }
                } }))
            };
        }
    }
}
