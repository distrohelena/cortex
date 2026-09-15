using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class PlaygroundProjectTests
{
    private const string Project = """
    {"format":"filomena-local-project","project":{"version":1,"stateScope":"session","initialStateId":"a","states":[{"id":"a","name":"Inicial","graph":{"version":2,"nodes":[{"id":"in","kind":"dm","x":0,"y":0},{"id":"p","kind":"prompt","prompt":"Árbitro","responseFields":[{"name":"motivo","description":"Até 10 palavras","type":"text"}],"x":100,"y":0}],"edges":[],"controlEdges":[{"source":"in","sourcePort":"then","target":"p","targetPort":"exec"}]}}]}}
    """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, false, true, true)]
    public async Task SavesEntireDraftAcrossServerRestartAndRejectsUnsafeRequests(bool boundaryDraft, bool legacy, bool enforcement = false, bool v2 = false)
    {
        var directory = Directory.CreateTempSubdirectory("filomena-project-");
        try
        {
            using var http = new HttpClient();
            var design = JsonNode.Parse(Project)!;
            if (enforcement)
            {
                var node = design["project"]!["states"]![0]!["graph"]!["nodes"]![1]!;
                node["executionProfile"] = "boundary_enforcement_v1";
                node["semanticContractVersion"] = v2 ? "no_teasing_about_topic:semantic-v2" : "boundary-semantic-features:v1";
                node["boundaryEvaluatorVersion"] = v2 ? "no_teasing_about_topic:evaluator-v2" : "no_teasing_about_topic:evaluator-v1";
                node["boundaryTemplateVersion"] = "no_teasing_about_topic:v1";
                // Deliberately incomplete executable contract; saving a draft remains allowed.
            }
            if (boundaryDraft)
            {
                var graph = design["project"]!["states"]![0]!["graph"]!;
                graph["version"] = 3;
                var node = graph["nodes"]![1]!;
                node["kind"] = "interpretBoundaries";
                if (legacy)
                {
                    node["provider"] = "openai";
                    node["model"] = "unselected-interpreter";
                    node["maxOutputTokens"] = 1024;
                    node["reasoningEffort"] = "medium";
                }
                node["settings"] = JsonNode.Parse(legacy
                    ? """{"taxonomyVersion":"v1","interpreterContractVersion":"v1","templatesVersion":"v1"}"""
                    : """{"taxonomyVersion":"v1","grammarVersion":"v1","templatesVersion":"v1"}""");
            }
            using var document = JsonDocument.Parse(design.ToJsonString());
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                if (v2)
                {
                    var detector = await browser.GetFromJsonAsync<JsonElement>("/api/boundary-enforcement?semanticVersion=no_teasing_about_topic:semantic-v2&evaluatorVersion=no_teasing_about_topic:evaluator-v2", TestContext.Current.CancellationToken);
                    Assert.Equal(9, detector.GetProperty("modelOutputs").GetProperty("properties").EnumerateObject().Count());
                    Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/boundary-enforcement?semanticVersion=latest&evaluatorVersion=latest", TestContext.Current.CancellationToken)).StatusCode);
                }
                Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/flow/project", TestContext.Current.CancellationToken)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/flow/project", document.RootElement, TestContext.Current.CancellationToken)).StatusCode);
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                (await browser.PostAsJsonAsync("/api/flow/project", document.RootElement, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/flow/project", new { message = "private chat" }, TestContext.Current.CancellationToken)).StatusCode);
            }
            await using (var app = LocalPromptPlayground.Build(new(), http, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", TestContext.Current.CancellationToken);
                if (legacy)
                {
                    var node = design["project"]!["states"]![0]!["graph"]!["nodes"]![1]!;
                    foreach (var field in new[] { "provider", "model", "reasoningEffort", "maxOutputTokens" }) node.AsObject().Remove(field);
                    node["settings"] = JsonNode.Parse("""{"taxonomyVersion":"v1","grammarVersion":"v1","templatesVersion":"v1"}""");
                }
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(design), loaded));
                if (boundaryDraft)
                {
                    var stored = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "project.json"), TestContext.Current.CancellationToken);
                    Assert.DoesNotContain("unselected-interpreter", stored);
                    Assert.Contains("grammarVersion", stored);
                }
            }
            Assert.Single(Directory.GetFiles(directory.FullName));
        }
        finally { directory.Delete(true); }
    }
}
