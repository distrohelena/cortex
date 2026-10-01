using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Cortex.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GraphitiCanaryTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_TEST_GRAPHITI") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set FILOMENA_TEST_GRAPHITI=1 for the existing read-only canary.")]
    public async Task ExistingCanariesStayInTheirConversation()
    {
        var endpoint = Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_MCP_ENDPOINT") ?? "http://127.0.0.1:8000/mcp";
        await using var client = new GraphitiReadClient(new Uri(endpoint), TimeSpan.FromSeconds(30));
        var reader = new GraphitiMemoryReader(client);
        var shape = await client.SearchFactsAsync("whatsapp-group-5511981042361-1589823314-g-us", "DRIFT42", 1, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine("MCP shape: " + string.Join(",", shape.EnumerateObject().Select(p => p.Name + ":" + p.Value.ValueKind)));
        if (shape.TryGetProperty("result", out var nested) && nested.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            TestContext.Current.TestOutputHelper!.WriteLine("MCP result shape: " + string.Join(",", nested.EnumerateObject().Select(p => p.Name + ":" + p.Value.ValueKind)));
            if (nested.TryGetProperty("error", out var error))
                TestContext.Current.TestOutputHelper!.WriteLine("Safe backend error markers: " + string.Join(',', new[] { "connection", "embedding", "model", "timeout", "429", "quota", "refused", "not initialized", "not found", "unavailable", "dimension" }.Where(marker => (error.GetString() ?? "").Contains(marker, StringComparison.OrdinalIgnoreCase))));
        }
        var contexts = new[]
        {
            ("whatsapp-group-5511981042361-1589823314-g-us", "DRIFT42"),
            ("whatsapp-group-120363318973251043-g-us", "PLEX73"),
            ("whatsapp-dm-5511944558218-s-whatsapp-net", "PRIVATE91")
        };
        foreach (var (ns, expected) in contexts)
        {
            var query = new MemoryQuery(ns, FilomenaIdentity.AgentUuid(ns), MemoryMode.Semantic, MemoryScope.Conversation,
                [], "DRIFT42 PLEX73 PRIVATE91 código de teste", Enum.GetValues<MemoryCategory>(), 50, 100000);
            var results = await reader.QueryAsync(query, TestContext.Current.CancellationToken);
            Assert.Contains(results, m => m.Content.Contains(expected, StringComparison.Ordinal));
            Assert.All(results, m => Assert.Equal(ns, m.ConversationNamespace));
            foreach (var (_, other) in contexts.Where(c => c.Item2 != expected))
                Assert.DoesNotContain(results, m => m.Content.Contains(other, StringComparison.Ordinal));
            TestContext.Current.TestOutputHelper!.WriteLine($"{ns}: found {expected}; excluded other conversation markers; {results.Count} active facts.");
        }
    }
}
