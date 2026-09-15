using System.Text.Json;
using Filomena.App;
using Filomena.Core.Memory;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Filomena.Llm.Tests;

// Explicit opt-in diagnostic: two read operations, no flow/model/writer. Raw evidence stays in ignored state/.
public sealed class GraphitiContractDiagnosticTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("FILOMENA_GRAPHITI_CONTRACT_DIAGNOSTIC") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Explicit read-only contract diagnostic only.")]
    public async Task CaptureExactThenSemanticContract()
    {
        var root = Environment.GetEnvironmentVariable("FILOMENA_DIAGNOSTIC_ROOT")!;
        var config = JsonSerializer.Deserialize<ManualShadowConfig>(await File.ReadAllTextAsync(Path.Combine(root, "config/manual-shadow.local.json"), TestContext.Current.CancellationToken), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var person = MemoryIdentity.Person(config.ConversationNamespace, config.PeerActorId, null);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(config.GraphitiEndpoint), Name = "Graphiti contract read-only diagnostic",
            TransportMode = HttpTransportMode.StreamableHttp, ConnectionTimeout = TimeSpan.FromSeconds(20)
        }), cancellationToken: TestContext.Current.CancellationToken);
        var destination = Path.Combine(root, "state/manual-shadow", "contract-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await using var evidence = new StreamWriter(destination);
        foreach (var tool in new[] { "get_entity_neighborhood", "search_memory_facts" })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            Dictionary<string, object?> arguments = tool == "get_entity_neighborhood"
                ? new() { ["group_id"] = config.ConversationNamespace, ["entity_uuid"] = person.ParticipantUuid.ToString(), ["limit"] = 50, ["active_only"] = true }
                : new() { ["group_ids"] = new[] { config.ConversationNamespace }, ["query"] = "limites pedidos de interação", ["max_facts"] = 50, ["active_only"] = true };
            var response = await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
            await evidence.WriteLineAsync(JsonSerializer.Serialize(new { tool, response })); await evidence.FlushAsync(TestContext.Current.CancellationToken);
            var value = response.StructuredContent ?? JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text).RootElement;
            if (value.TryGetProperty("result", out var wrapped)) value = wrapped;
            TestContext.Current.TestOutputHelper!.WriteLine($"{tool}: isError={response.IsError}; fields={string.Join(',', value.EnumerateObject().Select(p => p.Name))}; evidence={destination}");
            if (value.TryGetProperty("facts", out var facts))
                TestContext.Current.TestOutputHelper.WriteLine($"items={facts.GetArrayLength()}; namespaceMatched={facts.EnumerateArray().All(f => f.TryGetProperty("group_id", out var group) && group.GetString() == config.ConversationNamespace)}; firstFields={string.Join(',', facts.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.Object ? facts[0].EnumerateObject().Select(p => p.Name) : [])}");
        }
    }
}
