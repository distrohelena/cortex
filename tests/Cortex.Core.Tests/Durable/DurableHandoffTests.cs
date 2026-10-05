using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Filomena.Core.Identity;
using Xunit;

namespace Cortex.Core.Tests.Durable;

public sealed class DurableHandoffTests
{
    [Fact]
    public async Task AsyncFacadeAdmitsOnceAndRepeatedDrainDoesNotRepeatInboxCompletion()
    {
        using var directory = new ArtifactDirectory(); Directory.CreateDirectory(directory.Path);
        var session = new SessionKey("runtime", "character", "whatsapp", "account", "5511000000000@s.whatsapp.net");
        var actor = "whatsapp:5511000000000@s.whatsapp.net";
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId), ["whatsapp:5511999999999@s.whatsapp.net"], [actor]);
        var project = new PinnedShadowProject(DurableDriverTests.Project());
        var inboxPath = Path.Combine(directory.Path, "inbox.jsonl");
        var model = new IncrementalKernelTests.Model();
        using var host = new ShadowOutboundHost(Path.Combine(directory.Path, "outbound.jsonl"), project.Project.Document, new([], [], new Dictionary<string, string>(), [], null), [session], TransportSafetyCapabilities.Unproven);
        host.Rearm(OutboundMode.Shadow);
        string batchId; string executionId;
        using (var inbox = new ShadowInboundJournal(inboxPath, binding, project.Project.ProjectRevision, "epoch", 0))
        {
            await using var backend = new LocalSessionExecutionBackend(new SessionFlowRuntime(TimeProvider.System, model));
            await using var run = await DurableObservedRun.CreateAsync(backend, new NoReviews(), project, binding, inbox, host, OutboundMode.Shadow, TestContext.Current.CancellationToken);
            var message = new SessionMessageEnvelope("provider", session.ChannelId, session.AccountId, session.NativeConversationId, DateTimeOffset.UtcNow,
                SessionMessageDirection.Inbound, SessionEntryKind.Dm, JsonSerializer.SerializeToElement(new { texto = "fixture", remetente = new { nome = "actor" }, canal = new { nome = "DM", nativeId = session.NativeConversationId }, reply = (object?)null }));
            var input = new CanonicalShadowInbound("epoch", 1, session, SessionEntryKind.Dm, binding.ConversationNamespace, actor, message);
            Assert.Equal("admitted", (await run.AcceptAsync(input, TestContext.Current.CancellationToken)).Status);
            await run.DrainAsync(TestContext.Current.CancellationToken);
            await run.DrainAsync(TestContext.Current.CancellationToken);
            var outcome = Assert.Single((await run.ReadOutcomesAsync(new(), TestContext.Current.CancellationToken)).Items); batchId = outcome.BatchId; executionId = outcome.ExecutionId;
            Assert.Equal("duplicate", (await run.AcceptAsync(input, TestContext.Current.CancellationToken)).Status);
            Assert.Equal(2, model.Requests.Count);
        }
        using var reopened = new ShadowInboundJournal(inboxPath, binding, project.Project.ProjectRevision, "epoch", 0);
        reopened.CompletedOnce("provider", batchId, executionId, true);
        Assert.Throws<InvalidOperationException>(() => reopened.CompletedOnce("provider", "foreign-batch", executionId, true));
    }
    private sealed class NoReviews : ISessionReviewBackend
    {
        public Task<SessionParticipationReviewResult> ReviewAsync(SessionKey session, string originalMessageId, ParticipationReviewDefinition definition, CancellationToken token) => throw new NotSupportedException();
        public Task<SessionResponsePreviewResult> PreviewAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token) => throw new NotSupportedException();
        public Task<SessionReviewedResponseResult> ContinueAsync(SessionKey session, ArtifactRef reviewRef, CancellationToken token) => throw new NotSupportedException();
    }
}
