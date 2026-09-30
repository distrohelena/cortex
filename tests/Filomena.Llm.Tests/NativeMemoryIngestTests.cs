using System.Net;
using System.Text.Json;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class NativeMemoryIngestTests
{
    internal static ShadowSessionBinding Binding()
    {
        var s = new SessionKey("test", "filomena", "whatsapp", "test-account", "12345@g.us");
        return new(s, FilomenaIdentity.WhatsAppConversationNamespace(s.NativeConversationId),
            ["whatsapp:15550000002@s.whatsapp.net"], ["whatsapp:15550000001@s.whatsapp.net"], SessionEntryKind.Group);
    }
    internal static CanonicalShadowInbound Input(ShadowSessionBinding b, string text = "Gosto de café.") => new("test-epoch", 11, b.Session, b.Kind,
        b.ConversationNamespace, "whatsapp:15550000001@s.whatsapp.net",
        new("synthetic-1", b.Session.ChannelId, b.Session.AccountId, b.Session.NativeConversationId,
            DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), SessionMessageDirection.Inbound, b.Kind,
            JsonSerializer.SerializeToElement(new { texto = text, remetente = new { nome = "", stableId = "whatsapp:15550000001@s.whatsapp.net" } })));
    internal static NativeMemoryIngestConfig Config(ShadowSessionBinding b, string directory) => new(
        "http://127.0.0.1:18789/plugins/memory-graphiti/native-ingest/v1", "TEST_GATEWAY", "TEST_MEMORY", "test-binding",
        b.Session, b.ConversationNamespace, "test-epoch", 10, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), directory);

    [Fact]
    public async Task Slow_memory_backend_does_not_block_staging_and_shutdown_cancels_it()
    {
        var dir = Directory.CreateTempSubdirectory("memory-host-test-");
        try
        {
            var b = Binding(); var config = Config(b, dir.FullName);
            var handler = new SlowHandler(); using var http = new HttpClient(handler);
            var client = new NativeMemoryIngestClient(new Uri(config.Endpoint), "gateway", "host", http);
            await using (var host = new NativeMemoryHost(config, b, true, client))
            {
                host.StageBatch([Input(b)]);
                host.ScheduleReconciliation();
                await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
                host.StageBatch([Input(b) with { Sequence = 12, Message = Input(b).Message with { ProviderMessageId = "second" } }]);
                Assert.Equal(2, host.PendingCount);
                host.ScheduleReconciliation();
                Assert.Equal(1, handler.Calls);
            }
            Assert.True(handler.Cancelled);
            using var reopened = new NativeMemoryOutbox(config, b);
            Assert.Equal(2, reopened.Pending.Count);
        }
        finally { dir.Delete(true); }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls; public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }

    [Fact]
    public void Writer_authority_is_unavailable_to_shadow_and_wrong_session()
    {
        var b = Binding(); var config = Config(b, Path.GetTempPath());
        Assert.Throws<InvalidDataException>(() => NativeMemoryHost.ValidateAuthority(config, b, operationalLive: false));
        Assert.Throws<InvalidDataException>(() => NativeMemoryHost.ValidateAuthority(config with { Session = b.Session with { AccountId = "foreign" } }, b, true));
        NativeMemoryHost.ValidateAuthority(config, b, true);
    }

    [Fact]
    public void Quoted_message_remains_provenance_not_current_speaker_text()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            var input = Input(b) with
            {
                Message = Input(b).Message with
                {
                    Message = JsonSerializer.SerializeToElement(new
                    { texto = "Discordo.", reply = new { id = "quoted-provider-id", texto = "Outra pessoa disse isso." } })
                }
            };
            box.StageBatch([input]);
            var message = Assert.Single(box.Pending).Request.Message;
            Assert.Equal("Discordo.", message.GetProperty("content").GetString());
            Assert.Equal("quoted-provider-id", message.GetProperty("replyTo").GetProperty("providerMessageId").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Stage_survives_restart_and_same_id_different_content_is_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); var config = Config(b, dir.FullName);
            using (var box = new NativeMemoryOutbox(config, b))
            {
                Assert.Single(box.StageBatch([Input(b)]));
                Assert.Single(box.StageBatch([Input(b)]));
                Assert.Single(box.Pending);
                Assert.Throws<InvalidDataException>(() => box.StageBatch([Input(b, "conflicting content")]));
            }
            using var reopened = new NativeMemoryOutbox(config, b);
            Assert.Equal("Gosto de café.", Assert.Single(reopened.Pending).Request.Message.GetProperty("content").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Staging_preserves_display_name_without_changing_canonical_identity()
    {
        var dir = Directory.CreateTempSubdirectory("memory-name-test-");
        try
        {
            var b = Binding();
            using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            var input = Input(b);
            input = input with { Message = input.Message with { Message = JsonSerializer.SerializeToElement(new
            {
                texto = "Gosto de café.", remetente = new { nome = "Nome do WhatsApp", stableId = input.ActorId }
            }) } };
            box.StageBatch([input]);
            box.StageBatch([input]);
            var sender = Assert.Single(box.Pending).Request.Message.GetProperty("sender");
            Assert.Equal("Nome do WhatsApp", sender.GetProperty("displayName").GetString());
            Assert.Equal(input.ActorId, sender.GetProperty("stableId").GetString());
            Assert.Equal("trusted_native_observation", sender.GetProperty("identitySource").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Self_snapshot_suppressed_and_pre_boundary_events_are_not_ingested()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            Assert.Empty(box.StageBatch([Input(b) with { FromMe = true }, Input(b) with { Sequence = 10 },
                Input(b) with { SkipNonText = true }, Input(b) with { SuppressionReason = "membership_revoked" }]));
            Assert.Empty(box.Pending);
            Assert.Throws<InvalidDataException>(() => box.StageBatch([Input(b) with { StreamEpoch = "other-epoch" }]));
            Assert.Throws<InvalidDataException>(() => box.StageBatch([Input(b) with { Session = b.Session with { AccountId = "foreign" } }]));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Pending_is_independent_of_conversation_and_queued_is_not_persisted()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            box.StageBatch([Input(b)]); // No generation or participation needed, including silent outcomes.
            using var http = new HttpClient(new ReceiptHandler());
            using var client = new NativeMemoryIngestClient(new Uri(Config(b, dir.FullName).Endpoint), "gateway", "host", http);
            await box.ReconcileAsync(client, TestContext.Current.CancellationToken);
            Assert.Equal("queued", Assert.Single(box.Pending).Receipt!.State);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Older_pending_work_does_not_starve_new_messages()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            box.StageBatch(Enumerable.Range(0, 33).Select(i => Input(b) with
            {
                Sequence = 11 + i,
                Message = Input(b).Message with { ProviderMessageId = "message-" + i }
            }).ToArray());
            var handler = new ReceiptHandler(true);
            using var http = new HttpClient(handler);
            using var client = new NativeMemoryIngestClient(new Uri(Config(b, dir.FullName).Endpoint), "gateway", "host", http);
            await box.ReconcileAsync(client, TestContext.Current.CancellationToken);
            await box.ReconcileAsync(client, TestContext.Current.CancellationToken);
            Assert.All(box.Pending, item => Assert.Equal("memory_backend_unavailable", item.Diagnostic));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Backend_outage_preserves_pending_instead_of_reporting_success()
    {
        var dir = Directory.CreateTempSubdirectory("memory-outbox-test-");
        try
        {
            var b = Binding(); using var box = new NativeMemoryOutbox(Config(b, dir.FullName), b);
            box.StageBatch([Input(b)]);
            using var http = new HttpClient(new ReceiptHandler(true));
            using var client = new NativeMemoryIngestClient(new Uri(Config(b, dir.FullName).Endpoint), "gateway", "host", http);
            await box.ReconcileAsync(client, TestContext.Current.CancellationToken);
            Assert.Equal("memory_backend_unavailable", Assert.Single(box.Pending).Diagnostic);
        }
        finally { dir.Delete(true); }
    }

    private sealed class ReceiptHandler(bool outage = false) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (outage) throw new HttpRequestException("synthetic outage");
            Assert.Equal("host", Assert.Single(request.Headers.GetValues("x-native-memory-capability")));
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var input = await JsonSerializer.DeserializeAsync<NativeMemoryRequest>(await request.Content!.ReadAsStreamAsync(token), NativeMemoryIngestClient.Json, token);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new NativeMemoryReceipt("native-memory-ingest:v1",
                input!.RequestRef, input.EpisodeUuid, "queued", "reconcile"), NativeMemoryIngestClient.Json))
            };
        }
    }
}
