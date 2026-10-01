using Filomena.Core.Identity;
using Cortex.Core.Memory;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class MemoryPolicyTests
{
    private const string Drift = "whatsapp-group-5511981042361-1589823314-g-us";
    private const string Plex = "whatsapp-group-120363318973251043-g-us";
    private const string Actor = "whatsapp:5511944558218@s.whatsapp.net";
    private static readonly MemoryPerson Helena = new(Guid.Parse("b4964ff3-b8e0-5a95-8e82-517ab0dc88ee"), Actor, "Helena");
    private static MemoryQuery Query(int items = 8, int chars = 1800) => new(Drift,
        FilomenaIdentity.AgentUuid(Drift), MemoryMode.Hybrid, MemoryScope.Person,
        [Helena], "Persona", Enum.GetValues<MemoryCategory>(), items, chars);
    private static MemoryItem Item(string id, string text, MemoryCategory category) =>
        new(id, Drift, text, category, [Helena], "exact");

    [Fact]
    public void BoundaryPriorityPrecedesDeduplicationAndBudget()
    {
        var fact = Item("f", "fato", MemoryCategory.Fact);
        var boundary = Item("b", "limite", MemoryCategory.Boundary);
        var items = MemoryResultPolicy.Apply(Query(1), [fact, fact, boundary], DateTimeOffset.UtcNow);
        Assert.Equal("b", Assert.Single(items).Id);
        Assert.Empty(MemoryResultPolicy.Apply(Query(chars: 3), [fact, boundary], DateTimeOffset.UtcNow));
        Assert.Equal(2, MemoryResultPolicy.Apply(Query(), [fact, fact, boundary], DateTimeOffset.UtcNow).Count);
    }

    [Fact]
    public void InactiveAndFutureFactsDoNotReturn()
    {
        var now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
        var item = Item("f", "fato", MemoryCategory.Fact);
        Assert.Empty(MemoryResultPolicy.Apply(Query(),
            [item with { InvalidAt = now }, item with { Id = "e", ExpiredAt = now },
                item with { Id = "future", ValidAt = now.AddDays(1) }], now));
    }

    [Fact]
    public void CrossConversationResultFailsClosedInsteadOfLeaking()
    {
        Assert.Throws<InvalidDataException>(() => MemoryResultPolicy.Apply(Query(),
            [Item("x", "PLEX73", MemoryCategory.Fact) with { ConversationNamespace = Plex }], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CanonicalIdentityDoesNotUseDisplayNameAndRejectsForgedUuid()
    {
        var same = MemoryIdentity.Person(Drift, Actor, "Gabs");
        Assert.Equal(Helena.ParticipantUuid, same.ParticipantUuid);
        Assert.NotEqual(same.ParticipantUuid, MemoryIdentity.Person(Plex, Actor, "Helena").ParticipantUuid);
        Assert.Throws<InvalidDataException>(() => MemoryResultPolicy.ValidateQuery(
            Query() with { Persons = [Helena with { ParticipantUuid = Guid.NewGuid() }] }));
        Assert.Throws<InvalidDataException>(() => MemoryIdentity.Person(Drift, "Helena", "Helena"));
    }

    [Fact]
    public void PersonQueryDoesNotAdoptAnotherCanonicalSubject()
    {
        var other = MemoryIdentity.Person(Drift, "whatsapp:5511999999999@s.whatsapp.net", "Helena");
        Assert.Empty(MemoryResultPolicy.Apply(Query(),
            [Item("other", "fato", MemoryCategory.Fact) with { Subjects = [other] }], DateTimeOffset.UtcNow));
    }
}
