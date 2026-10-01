using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Cortex.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class PortuguesePromptArtifactTests
{
    [Fact]
    public void Portuguese_variant_compiles_without_changing_models_contracts_or_routing()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "docs/manual-group-live-project-v3-pt-br-v1.json");
        Assert.True(File.Exists(path), "Portuguese instruction variant is missing");
        var bytes = File.ReadAllBytes(path);
        _ = PinnedShadowProject.FromBytes(bytes, "manual-shadow-direct-rule-v3", 3,
            Convert.ToHexString(SHA256.HashData(bytes)));

        var candidate = JsonNode.Parse(bytes)!;
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName,
            "docs/manual-group-live-project-v3-natural-v1.json")))!;
        var states = candidate["project"]!["states"]!.AsArray();
        var originalStates = baseline["project"]!["states"]!.AsArray();
        Assert.Equal(originalStates.Count, states.Count);
        var changed = 0;
        for (var s = 0; s < states.Count; s++)
        {
            var nodes = states[s]!["graph"]!["nodes"]!.AsArray();
            var originalNodes = originalStates[s]!["graph"]!["nodes"]!.AsArray();
            Assert.Equal(originalNodes.Count, nodes.Count);
            for (var n = 0; n < nodes.Count; n++)
            {
                if (originalNodes[n]!["prompt"] is not { } originalPrompt) continue;
                var prompt = nodes[n]!["prompt"]!.GetValue<string>();
                if (prompt != originalPrompt.GetValue<string>()) changed++;
                // Only prose may differ: restoring it must recover the entire original artifact.
                nodes[n]!["prompt"] = originalPrompt.DeepClone();
            }
        }
        Assert.True(changed > 0, "Variant must not silently reuse the untranslated artifact");
        Assert.True(JsonNode.DeepEquals(baseline, candidate),
            "Language cleanup changed non-prompt configuration, contracts, models or routing");
    }
}
