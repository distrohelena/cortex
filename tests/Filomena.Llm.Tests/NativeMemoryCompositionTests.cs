using System.Text.Json;
using Filomena.App;
using Filomena.Core.Identity;
using Filomena.Core.Memory;
using Filomena.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeMemoryCompositionTests
{
    public static bool LocalFixtureRunning => Uri.TryCreate(Environment.GetEnvironmentVariable("NATIVE_MEMORY_TEST_ENDPOINT"),
        UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme == "http";

    // The companion Vitest test owns a loopback real plugin route/recorder and fake backend.
    [Fact(SkipUnless = nameof(LocalFixtureRunning), Skip = "Requires the companion local Vitest fixture server")]
    public async Task Real_plugin_round_trip_preserves_provenance_and_recall()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NATIVE_MEMORY_TEST_ENDPOINT")!);
        var directory = Directory.CreateTempSubdirectory("native-memory-roundtrip-");
        try
        {
            var b = NativeMemoryIngestTests.Binding();
            var config = NativeMemoryIngestTests.Config(b, directory.FullName) with { Endpoint = endpoint.AbsoluteUri };
            using var outbox = new NativeMemoryOutbox(config, b);
            outbox.StageBatch([NativeMemoryIngestTests.Input(b)]);
            var request = Assert.Single(outbox.Pending).Request;
            Assert.Equal("d7ac99a7-d322-5bb7-8dfd-b5636e160d0c", request.EpisodeUuid);
            Assert.Equal("249f0f7ce430abb0e8d2fc1053218b65fb89ebb89de4c42d47651848ba7a400f", request.RequestRef);
            using var writer = new NativeMemoryIngestClient(endpoint, "synthetic-gateway", "synthetic-host-capability-32-bytes-minimum");
            await outbox.ReconcileAsync(writer, TestContext.Current.CancellationToken);
            Assert.Equal("queued", Assert.Single(outbox.Pending).Receipt!.State);
            await outbox.ReconcileAsync(writer, TestContext.Current.CancellationToken);
            Assert.Empty(outbox.Pending);
            outbox.StageBatch([NativeMemoryIngestTests.Input(b)]); // Doesn't create another backend submission.
            Assert.Empty(outbox.Pending);

            using var http = new HttpClient();
            var reader = new GraphitiMemoryReader(new LocalFactsClient(http, new Uri(endpoint, "/test-facts")));
            var person = MemoryIdentity.Person(b.ConversationNamespace, "whatsapp:15550000001@s.whatsapp.net", null);
            var query = new MemoryQuery(b.ConversationNamespace, FilomenaIdentity.AgentUuid(b.ConversationNamespace),
                MemoryMode.Exact, MemoryScope.Person, [person], null, [MemoryCategory.Fact], 10, 2000);
            var fact = Assert.Single(await reader.QueryAsync(query, TestContext.Current.CancellationToken));
            Assert.Equal("Gosto de café.", fact.Content);
            Assert.Contains(request.EpisodeUuid, fact.EvidenceEpisodeIds!);
            Assert.Equal(person.ParticipantUuid, Assert.Single(fact.Subjects).ParticipantUuid);
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.QueryAsync(query with { ConversationNamespace = "foreign" }, TestContext.Current.CancellationToken));
        }
        finally { directory.Delete(true); }
    }

    private sealed class LocalFactsClient(HttpClient http, Uri endpoint) : IGraphitiReadClient
    {
        public async Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid person, int limit, CancellationToken token)
        {
            using var response = await http.GetAsync(endpoint, token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return document.RootElement.Clone();
        }
        public Task<JsonElement> SearchFactsAsync(string ns, string text, int limit, CancellationToken token) =>
            GetEntityNeighborhoodAsync(ns, Guid.Empty, limit, token);
    }
}
