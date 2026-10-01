using Filomena.Core.Identity;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class IdentityCompatibilityTests
{
    private const string DriftJid = "5511981042361-1589823314@g.us";
    private const string DriftNamespace = "whatsapp-group-5511981042361-1589823314-g-us";
    private const string PlexNamespace = "whatsapp-group-120363318973251043-g-us";
    private const string HelenaDmNamespace = "whatsapp-dm-5511944558218-s-whatsapp-net";
    private const string HelenaStableId = "whatsapp:5511944558218@s.whatsapp.net";

    [Fact]
    public void WhatsAppNamespacesRemainByteCompatible()
    {
        Assert.Equal(DriftNamespace, FilomenaIdentity.WhatsAppConversationNamespace(DriftJid));
        Assert.Equal(
            "whatsapp-dm-5511999999999-s-whatsapp-net",
            FilomenaIdentity.WhatsAppConversationNamespace("5511999999999@s.whatsapp.net"));
    }

    [Fact]
    public void CanonicalFixtureUuidsRemainByteCompatible()
    {
        const string fixtureActor = "whatsapp:5511999999999@s.whatsapp.net";

        Assert.Equal(
            Guid.Parse("a20b8f6b-d0c7-5f01-a766-3773f806ae6b"),
            FilomenaIdentity.ParticipantUuid(DriftNamespace, fixtureActor));
        Assert.Equal(
            Guid.Parse("7da4d33e-c475-5446-bd7a-0c0f0bd25178"),
            FilomenaIdentity.AgentUuid(DriftNamespace));
        Assert.Equal(
            Guid.Parse("827e98fb-1456-524d-ab68-19a8b61a8049"),
            FilomenaIdentity.InboundEpisodeUuid("whatsapp", DriftJid, "3EB0ABC"));
        Assert.Equal(
            Guid.Parse("8770f1d3-045e-520a-b43e-23f9792edf61"),
            FilomenaIdentity.OutboundEpisodeUuid("whatsapp", DriftJid, "outbound-provider-1"));
    }

    [Theory]
    [InlineData(DriftNamespace, "b4964ff3-b8e0-5a95-8e82-517ab0dc88ee")]
    [InlineData(PlexNamespace, "6824b555-ae10-53ca-958f-99f4cc832d23")]
    [InlineData(HelenaDmNamespace, "87943b77-4bb0-555c-84a7-7443b84a674e")]
    public void HelenaParticipantRemainsConversationScoped(string conversationNamespace, string expected)
    {
        Assert.Equal(Guid.Parse(expected), FilomenaIdentity.ParticipantUuid(conversationNamespace, HelenaStableId));
    }

    [Fact]
    public void AccountNameDoesNotParticipateInPersistentIdentity()
    {
        var oldAccount = FilomenaIdentity.WhatsAppConversationNamespace(DriftJid, "default");
        var newAccount = FilomenaIdentity.WhatsAppConversationNamespace(DriftJid, "filomena-business");

        Assert.Equal(oldAccount, newAccount);
        Assert.Equal(FilomenaIdentity.AgentUuid(oldAccount), FilomenaIdentity.AgentUuid(newAccount));
    }
}
