using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App;
using Cortex.Core.Flows;
using Filomena.Llm.SystemOne;
using Cortex.Inference.SystemOne;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeMentionParticipationLiveTests
{
    [Fact]
    public async Task Normalized_self_mention_is_direct_with_current_host_classifier()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("FILOMENA_MENTION_PROBE") != "1", "Explicit isolated Jev-only probe");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var file = Environment.GetEnvironmentVariable("FILOMENA_MENTION_HOST")!;
        var config = JsonSerializer.Deserialize<ManualShadowConfig>(File.ReadAllText(file), options)!;
        using var project = JsonDocument.Parse(File.ReadAllBytes(config.ProjectFile));
        var root = project.RootElement.GetProperty("project");
        var state = root.GetProperty("states").EnumerateArray().First();
        var graph = state.GetProperty("graph");
        var node = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "group-situation");
        var entry = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "group");
        var schema = JsonNode.Parse(TypedFlowRunner.ResponseSchema(TypedFlowCompiler.Compile(graph,
            entry.GetProperty("id").GetString()!).Nodes["group-situation"])!.Value.GetRawText())!;
        schema["properties"]!.AsObject().Remove("resumo");
        schema["required"] = new JsonArray(schema["properties"]!.AsObject().Select(p => JsonValue.Create(p.Key)).ToArray());
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var completion = new SystemOneFlowCompletion(new(http, new("https://api.typesafe.ai/v1/systemone"),
            () => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"), true, SystemOneProbabilityPolicy.Jev113TwoDecimals),
            "jev", 0.5, choiceQuestions: config.SystemOneParticipation!.ChoiceQuestions);
        var inputs = JsonSerializer.SerializeToElement(new {
            mensagens = new[] { new { texto = "@Filomena tá aí", remetente = new { nome = "Helena", @ref = "ref1" },
                canal = new { nome = "Grupo sintético", @ref = "ref2" }, reply = (object?)null } },
            historico = Array.Empty<object>()
        });
        using var output = JsonDocument.Parse(await completion.CompleteAsync(new("group-situation",
            root.GetProperty("personalityCore").GetString() + "\n" + state.GetProperty("participationGuidance").GetString()
                + "\n" + node.GetProperty("prompt").GetString(), inputs, JsonSerializer.SerializeToElement(schema),
            config.SystemOneParticipation.Model, "jev", null), TestContext.Current.CancellationToken));
        Assert.Equal("direto", output.RootElement.GetProperty("envolvimento").GetString());
    }
}
