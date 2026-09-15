using System.Text.Json.Nodes;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextFidelityProjectTests
{
    [Fact]
    public async Task Experimental_revision_changes_only_generation_and_regeneration_prompts()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var token = TestContext.Current.CancellationToken;
        var original = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs/manual-shadow-project-v1.json"), token))!;
        var clause = (await File.ReadAllTextAsync(Path.Combine(root, "docs/context-fidelity-clause-v1.txt"), token)).TrimEnd();
        var project = original.DeepClone();
        var changed = 0;
        foreach (var state in project["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "dm-generation" or "dm-regeneration" or "group-generation" or "group-regeneration")
                {
                    Assert.Equal("gpt-5.6-luna", node["model"]!.GetValue<string>());
                    Assert.Equal("medium", node["reasoningEffort"]!.GetValue<string>());
                    node["prompt"] = node["prompt"]!.GetValue<string>() + "\n\n" + clause;
                    changed++;
                }
        Assert.Equal(12, changed);
        var restored = project.DeepClone();
        foreach (var state in restored["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "dm-generation" or "dm-regeneration" or "group-generation" or "group-regeneration")
                    node["prompt"] = node["prompt"]!.GetValue<string>()[..^(clause.Length + 2)];
        Assert.True(JsonNode.DeepEquals(original, restored));
        var bytes = System.Text.Encoding.UTF8.GetBytes(project.ToJsonString());
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        _ = PinnedShadowProject.FromBytes(bytes, "manual-shadow-context-fidelity-v2", 2, sha);
        if (Environment.GetEnvironmentVariable("FILOMENA_CONTEXT_PROJECT_EXPORT") is { Length: > 0 } destination)
            await File.WriteAllBytesAsync(destination, bytes, token);
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs/manual-shadow-project-v2.json"), token));
        Assert.True(JsonNode.DeepEquals(project, persisted));
        TestContext.Current.TestOutputHelper!.WriteLine("PROJECT_SHA256=" + sha);
    }
}
