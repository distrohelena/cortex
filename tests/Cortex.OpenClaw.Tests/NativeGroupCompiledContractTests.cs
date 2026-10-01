using System.Net;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Shadow;
using Filomena.Core.Identity;
using Cortex.OpenClaw.Observation;
using Xunit;

namespace Cortex.OpenClaw.Tests;

public sealed class NativeGroupCompiledContractTests
{
    [Fact]
    public async Task Native_generated_v2_preserves_participants_self_cursor_and_disarmed_replay()
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-group-v2.json"),
            TestContext.Current.CancellationToken));
        var root = fixture.RootElement;
        Assert.Equal("compiled-native-reader-http/synthetic-local-only", root.GetProperty("provenance").GetString());
        var b = root.GetProperty("binding");
        string Text(string key) => b.GetProperty(key).GetString()!;
        var self = "whatsapp:" + Text("self");
        var alice = "whatsapp:" + Text("alice");
        var bob = "whatsapp:" + Text("bob");
        var session = new SessionKey("contract", "filomena", Text("channel"), Text("account"), Text("group"));
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(Text("group")),
            [self], [alice, bob], SessionEntryKind.Group);
        var participants = new Dictionary<string, string>
        {
            [Text("alice")] = alice, [Text("aliceLid")] = alice, [Text("bob")] = bob
        };
        var pollName = "poll";
        var statusName = "status";
        var requests = new List<string>();
        using var client = new HttpClient(new FixtureHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var path = request.RequestUri!.AbsolutePath;
            Assert.StartsWith("/inbound-observation/v2/", path);
            requests.Add(path);
            return root.GetProperty(path.EndsWith("/status") ? statusName : path.EndsWith("/open") ? "open" : pollName);
        }));
        NativeObservationReader Reader() => new(client, new("http://127.0.0.1:12345"), "synthetic", binding,
            Text("sessionRef"), self, alice, groupParticipants: participants);
        var reader = Reader();
        var ct = TestContext.Current.CancellationToken;
        var approval = await reader.PreflightAsync(ct);
        var opened = await reader.OpenAsync(approval, ct);
        Assert.Empty(opened.Records);
        Assert.Equal(0, opened.Cursor.Sequence);
        var batch = await reader.PollAsync(approval, opened.Cursor, ct);
        Assert.Equal(4, batch.Cursor.Sequence);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, batch.Records.Select(r => r.Sequence));
        Assert.Equal(Enumerable.Range(1, 4).Select(i => $"group-native-{i}"), batch.Records.Select(r => r.NativeMessageId));
        var inbounds = batch.Records.Select(reader.ToInbound).ToArray();
        Assert.Equal(new[] { alice, alice, bob, self }, inbounds.Select(i => i.ActorId));
        Assert.Equal(new[] { false, false, false, true }, inbounds.Select(i => i.FromMe));
        Assert.All(inbounds, i => { Assert.Equal(session, i.Session); Assert.Equal(SessionEntryKind.Group, i.Kind); });
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), batch.Records[0].OccurredAt);
        Assert.Equal("off", root.GetProperty("disarmed").GetProperty("captureState").GetString());
        Assert.Equal("healthy", root.GetProperty("disarmed").GetProperty("health").GetString());
        pollName = "replayAfterDisarm";
        statusName = "disarmed";
        var restarted = Reader();
        var replay = await restarted.PollAsync(await restarted.PreflightAsync(ct), opened.Cursor, ct);
        Assert.True(replay.EpochClosed);
        Assert.Equal(batch.Cursor, replay.Cursor);
        Assert.Equal(batch.Records.Select(r => (r.NativeMessageId, r.Sequence, r.ActorRef)),
            replay.Records.Select(r => (r.NativeMessageId, r.Sequence, r.ActorRef)));
        Assert.Equal(2, requests.Count(p => p.EndsWith("/poll")));
    }

    [Theory]
    [InlineData("version", "version_mismatch")]
    [InlineData("session", "integrity_violation")]
    [InlineData("actor", "integrity_violation")]
    [InlineData("epoch", "integrity_violation")]
    [InlineData("sequence", "gap_detected")]
    [InlineData("revision", "integrity_violation")]
    public async Task Native_shape_rejects_tampered_group_records(string mutation, string expectedCode)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-group-v2.json"),
            TestContext.Current.CancellationToken))!;
        var b = root["binding"]!;
        string Text(string key) => b[key]!.GetValue<string>();
        var session = new SessionKey("contract", "filomena", Text("channel"), Text("account"), Text("group"));
        var self = "whatsapp:" + Text("self");
        var alice = "whatsapp:" + Text("alice");
        var bob = "whatsapp:" + Text("bob");
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(Text("group")),
            [self], [alice, bob], SessionEntryKind.Group);
        var poll = root["poll"]!;
        switch (mutation)
        {
            case "version": poll["version"] = "inbound-observation:v99"; break;
            case "session": poll["events"]![0]!["sessionRef"] = "foreign"; break;
            case "actor": poll["events"]![0]!["actorRef"] = "unknown-actor"; break;
            case "epoch": poll["nextCursor"]!["epoch"] = "other-epoch"; break;
            case "sequence": poll["events"]![0]!["sequence"] = 2; break;
            case "revision": poll["effectiveConfigRevision"] = "stale"; break;
        }
        using var client = new HttpClient(new FixtureHandler(request => JsonSerializer.SerializeToElement(
            root[request.RequestUri!.AbsolutePath.EndsWith("/status") ? "status" : "poll"])));
        var reader = new NativeObservationReader(client, new("http://127.0.0.1:12345"), "synthetic", binding,
            Text("sessionRef"), self, alice, groupParticipants: new Dictionary<string, string>
            { [Text("alice")] = alice, [Text("aliceLid")] = alice, [Text("bob")] = bob });
        var ct = TestContext.Current.CancellationToken;
        var approval = await reader.PreflightAsync(ct);
        var cursor = root["open"]!["cursor"]!.Deserialize<NativeObservationCursor>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expectedCode, (await Assert.ThrowsAsync<NativeObservationException>(() =>
            reader.PollAsync(approval, cursor, ct))).Code);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, JsonElement> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request).GetRawText(), System.Text.Encoding.UTF8, "application/json") });
    }
}
