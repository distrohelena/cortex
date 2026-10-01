using System.Text.Json;
using Filomena.App;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Memory;
using Cortex.Core.Shadow;
using Cortex.Memory;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeSpeakerContextTests
{
    private const string Ns = "whatsapp-controlled-test";
    private const string Author = "whatsapp:111@s.whatsapp.net";
    private const string Other = "whatsapp:222@s.whatsapp.net";
    private const string Self = "agent:filomena";

    [Theory]
    [InlineData(false, "none", false, "")]
    [InlineData(true, "none", false, "")]
    [InlineData(true, "other", false, "")]
    [InlineData(true, "same", false, "")]
    [InlineData(true, "self", false, "")]
    [InlineData(true, "unresolved", false, "")]
    [InlineData(true, "other", true, "")]
    [InlineData(false, "self", true, "")]
    [InlineData(true, "none", true, "Nome no WhatsApp")]
    [InlineData(false, "none", true, "Nome no WhatsApp")]
    [InlineData(true, "none", false, "Nome no WhatsApp")]
    public async Task Enrichment_preserves_distinct_current_and_quoted_speakers(
        bool group, string quoteKind, bool missingNames, string nativeName)
    {
        var kind = group ? SessionEntryKind.Group : SessionEntryKind.Dm;
        var conversation = group ? "123@g.us" : "111@s.whatsapp.net";
        var session = new SessionKey("r", "filomena", "whatsapp", "default", conversation);
        var binding = new ShadowSessionBinding(session, Ns, [Self], [Author, Other], kind);
        object? quotedSender = quoteKind switch
        {
            "other" => new { nome = "", stableId = Other },
            "same" => new { nome = "", stableId = Author },
            "self" => new { nome = "Filomena", stableId = Self },
            _ => null
        };
        object? quote = quoteKind == "none" ? null : new
        {
            id = "older-message", texto = "Texto citado, não a mensagem mais recente.",
            remetente = quotedSender, origem = "citacao_do_remetente"
        };
        var input = new CanonicalShadowInbound("epoch", 22, session, kind, Ns, Author,
            new("current-message", "whatsapp", "default", conversation, DateTimeOffset.UtcNow,
                SessionMessageDirection.Inbound, kind, JsonSerializer.SerializeToElement(new
                {
                    texto = "Ela disse isso?", remetente = new { nome = nativeName, stableId = Author },
                    canal = new { nome = group ? "Grupo" : "DM", nativeId = conversation }, reply = quote
                })));
        var client = new Participants(missingNames);
        var output = await NativeInboundContext.EnrichAsync(input, binding, new(client), TestContext.Current.CancellationToken);
        var message = output.Message.Message;
        Assert.Equal(missingNames ? nativeName : "Helena", message.GetProperty("remetente").GetProperty("nome").GetString());
        Assert.Equal(Author, message.GetProperty("remetente").GetProperty("stableId").GetString());
        Assert.Equal(input.Sequence, output.Sequence);
        Assert.Equal(input.Message.ProviderMessageId, output.Message.ProviderMessageId);
        Assert.Equal(Author, output.ActorId);
        Assert.False(output.FromMe);
        Assert.Equal("Ela disse isso?", message.GetProperty("texto").GetString());
        var resultQuote = message.GetProperty("reply");
        if (quoteKind == "none") Assert.Equal(JsonValueKind.Null, resultQuote.ValueKind);
        else
        {
            Assert.Equal("older-message", resultQuote.GetProperty("id").GetString());
            Assert.Equal("Texto citado, não a mensagem mais recente.", resultQuote.GetProperty("texto").GetString());
            Assert.Equal("citacao_do_remetente", resultQuote.GetProperty("origem").GetString());
            var sender = resultQuote.GetProperty("remetente");
            if (quoteKind == "unresolved") Assert.Equal(JsonValueKind.Null, sender.ValueKind);
            else
            {
                Assert.Equal(quoteKind == "self" ? Self : quoteKind == "same" ? Author : Other,
                    sender.GetProperty("stableId").GetString());
                Assert.Equal(quoteKind == "self" ? "Filomena" : missingNames ? "" : quoteKind == "same" ? "Helena" : "Celso",
                    sender.GetProperty("nome").GetString());
            }
        }
        // Self is not a Person lookup; unresolved attribution must not guess an entity.
        Assert.Equal(quoteKind == "other" ? 2 : 1, client.Requests);
    }

    private sealed class Participants(bool missingNames) : IGraphitiReadClient
    {
        public int Requests { get; private set; }
        public Task<JsonElement> GetEntityNeighborhoodAsync(string ns, Guid id, int limit, CancellationToken token)
        {
            Assert.Equal(Ns, ns);
            var author = MemoryIdentity.Person(Ns, Author, null);
            var other = MemoryIdentity.Person(Ns, Other, null);
            var person = id == author.ParticipantUuid ? author : id == other.ParticipantUuid ? other
                : throw new InvalidOperationException("Unexpected participant lookup");
            Requests++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                facts = Array.Empty<object>(), entity = new
                {
                    uuid = person.ParticipantUuid, group_id = Ns, stable_id = person.ActorStableId,
                    labels = new[] { "Person" },
                    display_name_current = missingNames ? null : id == author.ParticipantUuid ? "Helena" : "Celso"
                }
            }));
        }
        public Task<JsonElement> SearchFactsAsync(string ns, string query, int limit, CancellationToken token)
            => throw new InvalidOperationException("No recall expected for identity enrichment");
    }
}
