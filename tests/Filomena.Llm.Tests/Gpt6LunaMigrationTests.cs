using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Cortex.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class Gpt6LunaMigrationTests
{
    [Fact]
    public void Operational_project_compiles_with_only_Luna_model_replacements()
    {
        var root = ParticipationGateV1Tests.Root;
        var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(root, DmContinuityLiveTests.Artifact)))!;
        var file = Path.Combine(root, "docs/manual-live-project-v3-gpt6-luna.json");
        Assert.True(File.Exists(file), "GPT-6 Luna operational artifact missing");
        var bytes = File.ReadAllBytes(file);
        var updated = JsonNode.Parse(bytes)!;
        var count = 0;
        void RestoreModels(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["model"]?.ToString() == "gpt-6-luna")
                {
                    Assert.Equal("openai", obj["provider"]?.ToString());
                    obj["model"] = "gpt-5.6-luna";
                    count++;
                }
                foreach (var entry in obj) RestoreModels(entry.Value);
            }
            else if (node is JsonArray array)
                foreach (var item in array) RestoreModels(item);
        }
        RestoreModels(updated);
        Assert.Equal(24, count);
        Assert.True(JsonNode.DeepEquals(baseline, updated), "Prompts, reasoning, local models and graph must stay unchanged");
        _ = PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-sent", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
