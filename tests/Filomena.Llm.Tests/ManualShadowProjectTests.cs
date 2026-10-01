using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ManualShadowProjectTests
{
    [Fact]
    public void Approved_H_configuration_with_only_shadow_sinks_is_executable()
    {
        var original = MultiDetectorGateTests.Fixture();
        var project = original.DeepClone();
        foreach (var state in project["project"]!["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            foreach (var sink in new[] { "dm-transition-responder", "dm-accepted-1", "dm-accepted-2" })
            {
                var id = sink + "-shadow-send";
                graph["nodes"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id, kind = "send", x = 3300, y = 1700 }));
                graph["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { source = sink.EndsWith("-2", StringComparison.Ordinal) ? "dm-regeneration" : "dm-generation", sourcePort = "texto", target = id, targetPort = "message" }));
                foreach (var edge in graph["controlEdges"]!.AsArray().Where(e => e!["target"]!.GetValue<string>() == sink)) edge!["target"] = id;
                graph["controlEdges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { source = id, sourcePort = "shadowed", target = sink, targetPort = "exec" }));
            }
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(project.ToJsonString());
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var pin = PinnedShadowProject.FromBytes(bytes, "manual-shadow-grounding-B-v1", 1, sha);
        Assert.Equal("participation-inactive-v1", pin.Project.Document.GetProperty("initialStateId").GetString());
        // Inverse of the routing-only adaptation must recover the approved H project exactly.
        var restored = project.DeepClone();
        foreach (var state in restored["project"]!["states"]!.AsArray())
        {
            var graph = state!["graph"]!;
            foreach (var node in graph["nodes"]!.AsArray().Where(n => n!["kind"]!.GetValue<string>() == "send").ToArray()) graph["nodes"]!.AsArray().Remove(node);
            foreach (var edge in graph["edges"]!.AsArray().Where(e => e!["target"]!.GetValue<string>().EndsWith("-shadow-send", StringComparison.Ordinal)).ToArray()) graph["edges"]!.AsArray().Remove(edge);
            foreach (var edge in graph["controlEdges"]!.AsArray().ToArray())
                if (edge!["source"]!.GetValue<string>().EndsWith("-shadow-send", StringComparison.Ordinal)) graph["controlEdges"]!.AsArray().Remove(edge);
                else if (edge["target"]!.GetValue<string>().EndsWith("-shadow-send", StringComparison.Ordinal)) edge["target"] = edge["target"]!.GetValue<string>()[..^12];
        }
        Assert.True(JsonNode.DeepEquals(original, restored));
        // Explicit local artifact export only. No recall or completion, never live evidence.
        if (Environment.GetEnvironmentVariable("FILOMENA_MANUAL_PROJECT_EXPORT") is { Length: > 0 } destination)
        {
            File.WriteAllBytes(destination, bytes);
            TestContext.Current.TestOutputHelper!.WriteLine("PROJECT_SHA256=" + sha);
        }
    }
}
