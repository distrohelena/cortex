using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ShadowProjectTests
{
    [Fact]
    public async Task SendDraftRoundtripsButCannotPersistHostAuthority()
    {
        var directory = Directory.CreateTempSubdirectory("shadow-project-");
        try
        {
            var document = JsonNode.Parse("""
                {"format":"filomena-local-project","project":{"version":1,"stateScope":"session","initialStateId":"a","states":[{"id":"a","name":"A","graph":{"version":3,"nodes":[{"id":"in","kind":"dm","x":0,"y":0},{"id":"send","kind":"send","x":100,"y":0}],"edges":[],"controlEdges":[]}}]}}
                """)!;
            using var unusedModelClient = new HttpClient();
            // The existing pinned multi-detector configuration must survive beside Enviar.
            document["project"]!["states"]![0]!["graph"]!["nodes"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"enforcement-draft","kind":"prompt","prompt":"Existing profile","x":200,"y":0,
                 "executionProfile":"boundary_enforcement_v1","boundaryTemplateVersion":"no_teasing_about_topic:v1",
                 "semanticContractVersion":"no_teasing_about_topic:semantic-v2","boundaryEvaluatorVersion":"no_teasing_about_topic:evaluator-v2",
                 "enforcementStrategy":"multiple_detectors_v1","complementaryContractVersion":"no_teasing_about_topic:implicit-depreciation-v1","aggregationPolicyVersion":"any_block_all_allow:v1"}
                """));
            await using (var app = LocalPromptPlayground.Build(new(), unusedModelClient, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
                (await browser.PostAsJsonAsync("/api/flow/project", document, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
                foreach (var field in new[] { "target", "outboundContext", "provenance", "recipient", "executionProfile" })
                {
                    var attack = document.DeepClone();
                    attack["project"]!["states"]![0]!["graph"]!["nodes"]![1]![field] = "fake";
                    Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/flow/project", attack, TestContext.Current.CancellationToken)).StatusCode);
                }
                var elevated = document.DeepClone();
                elevated["project"]!["outboundMode"] = "enabled";
                Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/flow/project", elevated, TestContext.Current.CancellationToken)).StatusCode);
            }
            await using (var app = LocalPromptPlayground.Build(new(), unusedModelClient, 0, projectDirectory: directory.FullName))
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(document), loaded));
            }
        }
        finally { directory.Delete(true); }
    }
}
