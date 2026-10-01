using System.Text.Json;
using Cortex.Core.Memory;
using Cortex.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class GraphitiParticipantTests
{
    [Fact]
    public async Task Native_context_uses_registered_name_and_preserves_quote_cursor_and_message_identity()
    {
        const string actor = "whatsapp:123456789@s.whatsapp.net", ns = "whatsapp-test-group";
        var session = new Cortex.Core.Flows.Sessions.SessionKey("r", "filomena", "whatsapp", "default", "123@g.us");
        var binding = new Cortex.Core.Shadow.ShadowSessionBinding(session, ns, ["agent:filomena"], [actor],
            Cortex.Core.Flows.Sessions.SessionEntryKind.Group);
        var person = MemoryIdentity.Person(ns, actor, null);
        var client = new Client(JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>(), entity = new {
            uuid = person.ParticipantUuid, group_id = ns, stable_id = actor, labels = new[] { "Person" }, display_name_current = "Helena Assis" } }));
        var input = new Cortex.Core.Shadow.CanonicalShadowInbound("epoch", 22, session, binding.Kind, ns, actor,
            new("incoming-id", "whatsapp", "default", "123@g.us", DateTimeOffset.UtcNow,
                Cortex.Core.Flows.Sessions.SessionMessageDirection.Inbound, binding.Kind,
                JsonSerializer.SerializeToElement(new { texto = "responde isso", remetente = new { nome = "", stableId = actor },
                    canal = new { nome = "Grupo", nativeId = "123@g.us" },
                    reply = new { id = "quoted-id", texto = "John Mayer?", origem = "citacao_do_remetente" } })));
        var actual = await Filomena.App.NativeInboundContext.EnrichAsync(input, binding, new(client), TestContext.Current.CancellationToken);
        Assert.Equal(input.Sequence, actual.Sequence);
        Assert.Equal(input.Message.ProviderMessageId, actual.Message.ProviderMessageId);
        Assert.Equal("Helena Assis", actual.Message.Message.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.Equal("John Mayer?", actual.Message.Message.GetProperty("reply").GetProperty("texto").GetString());
        await Assert.ThrowsAsync<InvalidDataException>(() => Filomena.App.NativeInboundContext.EnrichAsync(
            input with { ConversationNamespace = "foreign" }, binding, new(client), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("foreign_namespace", false)]
    [InlineData("wrong_uuid", false)]
    [InlineData("wrong_actor", false)]
    [InlineData("not_person", false)]
    [InlineData("outage", false)]
    public async Task Registered_name_requires_exact_canonical_identity(string mutation, bool valid)
    {
        var person = MemoryIdentity.Person("whatsapp-test-group", "whatsapp:123456789@s.whatsapp.net", null);
        var response = mutation == "outage" ? JsonSerializer.SerializeToElement(new { error = "offline" })
            : JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>(), entity = new {
                uuid = mutation == "wrong_uuid" ? Guid.NewGuid() : person.ParticipantUuid,
                group_id = mutation == "foreign_namespace" ? "other" : "whatsapp-test-group",
                stable_id = mutation == "wrong_actor" ? "whatsapp:987654321@s.whatsapp.net" : person.ActorStableId,
                labels = new[] { mutation == "not_person" ? "Entity" : "Person" },
                name = "technical-name-never-used", display_name_current = "Helena Assis"
            } });
        var reader = new GraphitiParticipantReader(new Client(response));
        if (valid) Assert.Equal("Helena Assis", (await reader.ResolveAsync("whatsapp-test-group", person.ActorStableId,
            TestContext.Current.CancellationToken)).DisplayName);
        else await Assert.ThrowsAsync<InvalidDataException>(() => reader.ResolveAsync("whatsapp-test-group", person.ActorStableId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_registered_name_never_uses_technical_name()
    {
        var person = MemoryIdentity.Person("whatsapp-test-group", "whatsapp:123456789@s.whatsapp.net", null);
        var response = JsonSerializer.SerializeToElement(new { facts = Array.Empty<object>(), entity = new {
            uuid = person.ParticipantUuid, group_id = "whatsapp-test-group", stable_id = person.ActorStableId,
            labels = new[] { "Person" }, name = person.ActorStableId
        } });
        Assert.Null((await new GraphitiParticipantReader(new Client(response)).ResolveAsync("whatsapp-test-group", person.ActorStableId,
            TestContext.Current.CancellationToken)).DisplayName);
    }

    private sealed class Client(JsonElement response) : IGraphitiReadClient
    {
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid id, int limit, CancellationToken token) => Task.FromResult(response);
        public Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token) => throw new InvalidOperationException();
    }
}
