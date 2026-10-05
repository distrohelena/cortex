using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableIdentityTests
{
    internal static SessionKey Session => new("runtime", "character", "channel", "account", "conversation");
    internal static ArtifactScope Scope => new("installation", Session, "execution", "revision");

    [Fact]
    public void IdsIncludeEverySessionDimension()
    {
        var original = DurableIdentity.SessionWorkflowId("installation", Session);
        SessionKey[] different = [Session with { RuntimeId = "other" }, Session with { CharacterId = "other" },
            Session with { ChannelId = "other" }, Session with { AccountId = "other" }, Session with { NativeConversationId = "other" }];
        Assert.All(different, key => Assert.NotEqual(original, DurableIdentity.SessionWorkflowId("installation", key)));
        Assert.NotEqual(original, DurableIdentity.SessionWorkflowId("other", Session));
        Assert.Equal(original, DurableIdentity.SessionWorkflowId("installation", new("runtime", "character", "channel", "account", "conversation")));
        // Length-prefix/canonical framing must prevent delimiter collisions.
        Assert.NotEqual(DurableIdentity.SessionWorkflowId("installation", Session with { RuntimeId = "a:b", CharacterId = "c" }),
            DurableIdentity.SessionWorkflowId("installation", Session with { RuntimeId = "a", CharacterId = "b:c" }));
    }

    [Fact]
    public void OrdinalAndRevisionChangeOperationId()
    {
        var key = new StepKey(Scope, "prompt", 0);
        Assert.NotEqual(DurableIdentity.OperationId(key), DurableIdentity.OperationId(key with { Ordinal = 1 }));
        Assert.NotEqual(DurableIdentity.OperationId(key), DurableIdentity.OperationId(key with { Scope = Scope with { ProjectRevision = "other" } }));
        Assert.NotEqual(DurableIdentity.OperationId(key), DurableIdentity.FeedbackRequestId(key));
        Assert.NotEqual(DurableIdentity.BatchId("installation", Session, "revision", 0), DurableIdentity.BatchId("installation", Session, "revision", 1));
        Assert.NotEqual(DurableIdentity.ExecutionId("installation", Session, "batch", "normal"), DurableIdentity.ExecutionId("installation", Session, "batch", "preview"));
    }

    [Fact]
    public void InvalidIdentitiesAndPaginationAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DurableIdentity.SessionWorkflowId("", Session));
        Assert.Throws<ArgumentException>(() => DurableIdentity.SessionWorkflowId("installation", Session with { AccountId = "a\nb" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => DurableIdentity.OperationId(new(Scope, "prompt", -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(Limit: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(Limit: 1001).Validate());
    }
}
