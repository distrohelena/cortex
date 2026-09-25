using System.Text.Json;
using Filomena.Core.Outbound;
using Filomena.OpenClaw.Mcp;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class OpenClawBoundMcpTransportTests
{
    private static readonly BoundSendRequest Request = new(
        "0123456789abcdef0123456789abcdef", "opaque-route", "whatsapp", "test-account",
        "5550001@lid", "approved candidate", 4000);

    [Theory]
    [InlineData("original-message-7")]
    [InlineData("wrong-target")]
    [InlineData(null)]
    public async Task GroupReplyRequiresVersionedTargetAndMatchingReceipt(string? receiptTarget)
    {
        const string group = "120300000001@g.us";
        await using var client = new Client { ReceiptRecipient = group,
            ReceiptContract = "whatsapp-bound-group-reply:v1", ChangedField = "replyToMessageId", ChangedValue = receiptTarget };
        var request = Request with { Recipient = group };
        // Set via JSON so RED exercises behavior before the optional property exists.
        var fields = JsonSerializer.SerializeToNode(request)!.AsObject();
        fields["ReplyToMessageId"] = "original-message-7";
        request = fields.Deserialize<BoundSendRequest>()!;
        var transport = new OpenClawBoundMcpTransport(client, true);
        if (receiptTarget == "original-message-7")
            await transport.SendBoundTextAsync(request, TestContext.Current.CancellationToken);
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => transport.SendBoundTextAsync(request, TestContext.Current.CancellationToken));
        var call = Assert.Single(client.Calls);
        var expected = JsonSerializer.SerializeToElement(call.Arguments).GetProperty("expected");
        Assert.Equal("whatsapp-bound-group-reply:v1", expected.GetProperty("contractVersion").GetString());
        Assert.Equal("original-message-7", expected.GetProperty("replyToMessageId").GetString());
    }

    [Fact]
    public async Task ExplicitGroupHostUsesSeparateContractAndExactReceipt()
    {
        const string group = "551100000001-1589823314@g.us";
        await using var client = new Client { AdvertiseBound = true, RouteRecipient = group,
            ReceiptRecipient = group, ReceiptContract = "whatsapp-bound-group-text:v1" };
        var transport = new OpenClawBoundMcpTransport(client, allowExactGroupDelivery: true);
        Assert.Equal(group, (await transport.DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken)).Recipient);
        var receipt = await transport.SendBoundTextAsync(Request with { Recipient = group }, TestContext.Current.CancellationToken);
        Assert.Equal(group, receipt.Recipient);
        var send = Assert.Single(client.Calls, c => c.Name == "messages_send_bound");
        Assert.Equal("whatsapp-bound-group-text:v1", JsonSerializer.SerializeToElement(send.Arguments)
            .GetProperty("expected").GetProperty("contractVersion").GetString());
    }

    [Fact]
    public async Task PinsAttemptAndDestinationAndRequiresObservedReceipt()
    {
        await using var client = new Client();
        var receipt = await new OpenClawBoundMcpTransport(client).SendBoundTextAsync(
            Request, TestContext.Current.CancellationToken);
        Assert.Equal("provider-id", receipt.ProviderMessageId);
        Assert.Equal("test-account", receipt.AccountId);
        Assert.Equal("5550001@lid", receipt.Recipient);
        var call = Assert.Single(client.Calls);
        Assert.Equal("messages_send_bound", call.Name);
        var args = JsonSerializer.SerializeToElement(call.Arguments);
        Assert.Equal("opaque-route", args.GetProperty("session_key").GetString());
        Assert.Equal("approved candidate", args.GetProperty("text").GetString());
        var expected = args.GetProperty("expected");
        Assert.Equal("whatsapp-bound-text:v1", expected.GetProperty("contractVersion").GetString());
        Assert.Equal(Request.AttemptRef, expected.GetProperty("attemptRef").GetString());
        Assert.Equal("test-account", expected.GetProperty("accountId").GetString());
        Assert.Equal("5550001@lid", expected.GetProperty("recipient").GetString());
        Assert.Equal(4000, expected.GetProperty("maxSinglePartCharacters").GetInt32());
    }

    [Fact]
    public async Task ManagedMemberDmUsesStrictVersionAndCarriesExactLeaseBinding()
    {
        const string canonicalPn = "15550001@s.whatsapp.net";
        var binding = new MemberDmBoundTextBinding(canonicalPn, "dm-scope", "dm-route", "dispatch-rev-7", "lease-generation-3");
        await using var client = new Client { ReceiptRecipient = canonicalPn, ReceiptContract = "whatsapp-bound-member-dm-text:v1",
            MemberDmReceipt = binding };
        var request = Request with { Recipient = canonicalPn, MemberDm = binding };

        await new OpenClawBoundMcpTransport(client).SendBoundTextAsync(request, TestContext.Current.CancellationToken);

        var args = JsonSerializer.SerializeToElement(Assert.Single(client.Calls).Arguments);
        var expected = args.GetProperty("expected");
        Assert.Equal("whatsapp-bound-member-dm-text:v1", expected.GetProperty("contractVersion").GetString());
        Assert.Equal(canonicalPn, expected.GetProperty("nativeConversationId").GetString());
        Assert.Equal("dm-scope", expected.GetProperty("scopeRef").GetString());
        Assert.Equal("dm-route", expected.GetProperty("routeBindingRef").GetString());
        Assert.Equal("dispatch-rev-7", expected.GetProperty("effectiveConfigRevision").GetString());
        Assert.Equal("lease-generation-3", expected.GetProperty("membershipLeaseRef").GetString());
    }

    [Fact]
    public async Task ManagedMemberDmDoesNotTreatFrozenReplyPortAsBoundQuoteAuthority()
    {
        const string canonicalPn = "15550001@s.whatsapp.net";
        var request = Request with
        {
            Recipient = canonicalPn,
            MemberDm = new(canonicalPn, "dm-scope", "dm-route", "dispatch-rev-7", "lease-generation-3"),
            ReplyToMessageId = "observed-message-9",
        };
        await using var client = new Client();

        await Assert.ThrowsAsync<ArgumentException>(() => new OpenClawBoundMcpTransport(client)
            .SendBoundTextAsync(request, TestContext.Current.CancellationToken));

        Assert.Empty(client.Calls);
    }

    [Theory]
    [InlineData("missing-lease")]
    [InlineData("lid-alias-recipient")]
    public async Task InvalidManagedMemberDmNeverFallsBackToLegacyContract(string variant)
    {
        const string canonicalPn = "15550001@s.whatsapp.net";
        var binding = new MemberDmBoundTextBinding(canonicalPn, "dm-scope", "dm-route", "dispatch-rev-7",
            variant == "missing-lease" ? "" : "lease-generation-3");
        var request = Request with
        {
            Recipient = variant == "lid-alias-recipient" ? "991234@lid" : canonicalPn,
            MemberDm = binding,
        };
        await using var client = new Client();

        await Assert.ThrowsAsync<ArgumentException>(() => new OpenClawBoundMcpTransport(client)
            .SendBoundTextAsync(request, TestContext.Current.CancellationToken));

        Assert.Empty(client.Calls);
    }

    [Theory]
    [InlineData("contractVersion", "whatsapp-bound-text:v1")]
    [InlineData("recipient", "120399999999@g.us")]
    [InlineData("accountId", "wrong-account")]
    public async Task GroupReceiptMismatchNeverFallsBackOrRetries(string field, string value)
    {
        const string group = "120300000001@g.us";
        await using var client = new Client { ReceiptRecipient = group,
            ReceiptContract = "whatsapp-bound-group-text:v1", ChangedField = field, ChangedValue = value };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenClawBoundMcpTransport(client, true)
            .SendBoundTextAsync(Request with { Recipient = group }, TestContext.Current.CancellationToken));
        Assert.Equal("messages_send_bound", Assert.Single(client.Calls).Name);
    }

    [Theory]
    [InlineData("contractVersion", "unknown")]
    [InlineData("attemptRef", "another-attempt")]
    [InlineData("channel", "telegram")]
    [InlineData("accountId", "other-account")]
    [InlineData("recipient", "99999@lid")]
    [InlineData("messageId", "")]
    [InlineData("accountId", null)]
    public async Task MissingOrMismatchedReceiptNeverSucceedsOrFallsBack(string field, string? value)
    {
        await using var client = new Client { ChangedField = field, ChangedValue = value };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenClawBoundMcpTransport(client)
            .SendBoundTextAsync(Request, TestContext.Current.CancellationToken));
        Assert.Equal("messages_send_bound", Assert.Single(client.Calls).Name);
    }

    [Fact]
    public async Task TransportFailureIsNeverRetried()
    {
        await using var client = new Client { Fail = true };
        await Assert.ThrowsAsync<IOException>(() => new OpenClawBoundMcpTransport(client)
            .SendBoundTextAsync(Request, TestContext.Current.CancellationToken));
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task UnboundSendIsForbidden()
    {
        await using var client = new Client();
        await Assert.ThrowsAsync<NotSupportedException>(() => new OpenClawBoundMcpTransport(client)
            .SendTextAsync("opaque-route", "hello", TestContext.Current.CancellationToken));
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task MissingNativeCapabilityPreventsRouteApproval()
    {
        await using var client = new Client();
        await Assert.ThrowsAsync<NotSupportedException>(() => new OpenClawBoundMcpTransport(client)
            .DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken));
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task AdvertisedBoundCapabilityStillRequiresExactConversationMetadata()
    {
        await using var client = new Client { AdvertiseBound = true };
        var route = await new OpenClawBoundMcpTransport(client).DescribeRouteAsync(
            "opaque-route", TestContext.Current.CancellationToken);
        Assert.Equal(new TransportRouteDescription("whatsapp", "test-account", "5550001@lid"), route);
        Assert.Equal("conversation_get", Assert.Single(client.Calls).Name);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("attempt")]
    [InlineData("limit")]
    [InlineData("oversize")]
    [InlineData("channel")]
    public async Task InvalidRequestNeverReachesMcp(string variant)
    {
        await using var client = new Client();
        var request = variant switch
        {
            "group" => Request with { Recipient = "5550001@g.us" },
            "attempt" => Request with { AttemptRef = "new-attempt" },
            "limit" => Request with { MaxSinglePartCharacters = 4001 },
            "oversize" => Request with { MaxSinglePartCharacters = 3 },
            _ => Request with { Channel = "telegram" },
        };
        await Assert.ThrowsAsync<ArgumentException>(() => new OpenClawBoundMcpTransport(client)
            .SendBoundTextAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task BoundDescriptionConvertsOnlyExactE164ToNativePn()
    {
        await using var client = new Client { AdvertiseBound = true, RouteRecipient = "+15550001" };
        var route = await new OpenClawBoundMcpTransport(client).DescribeRouteAsync(
            "opaque-route", TestContext.Current.CancellationToken);
        Assert.Equal(new TransportRouteDescription("whatsapp", "test-account", "15550001@s.whatsapp.net"), route);
        Assert.Equal("conversation_get", Assert.Single(client.Calls).Name);
    }

    [Theory]
    [InlineData("15550001")]
    [InlineData("+015550001")]
    [InlineData("+15550001\n")]
    [InlineData(" +15550001")]
    [InlineData("15550001@g.us")]
    public async Task BoundDescriptionRejectsAmbiguousOrGroupDestination(string recipient)
    {
        await using var client = new Client { AdvertiseBound = true, RouteRecipient = recipient };
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenClawBoundMcpTransport(client)
            .DescribeRouteAsync("opaque-route", TestContext.Current.CancellationToken));
        Assert.Equal("conversation_get", Assert.Single(client.Calls).Name);
    }

    private sealed class Client : IMcpToolClient
    {
        public List<McpToolCall> Calls { get; } = [];
        public string? ChangedField { get; init; }
        public string? ChangedValue { get; init; }
        public bool Fail { get; init; }
        public bool AdvertiseBound { get; init; }
        public string RouteRecipient { get; init; } = "5550001@lid";
        public string ReceiptRecipient { get; init; } = "5550001@lid";
        public string ReceiptContract { get; init; } = "whatsapp-bound-text:v1";
        public MemberDmBoundTextBinding? MemberDmReceipt { get; init; }
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>(AdvertiseBound
                ? [new("messages_send_bound", null, JsonSerializer.SerializeToElement(new { }))] : []);
        public Task<JsonElement> CallToolAsync(string name, IReadOnlyDictionary<string, object?> args,
            CancellationToken cancellationToken)
        {
            Calls.Add(new(name, args));
            if (Fail) throw new IOException("connection lost after submission");
            if (name == "conversation_get") return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                conversation = new { channel = "whatsapp", accountId = "test-account", to = RouteRecipient },
            }));
            var result = new Dictionary<string, object?>
            {
                ["contractVersion"] = ReceiptContract, ["attemptRef"] = Request.AttemptRef,
                ["channel"] = "whatsapp", ["accountId"] = "test-account",
                ["recipient"] = ReceiptRecipient, ["messageId"] = "provider-id",
            };
            if (MemberDmReceipt is { } memberDm)
            {
                result["nativeConversationId"] = memberDm.NativeConversationId;
                result["scopeRef"] = memberDm.ScopeRef;
                result["membershipLeaseRef"] = memberDm.MembershipLeaseRef;
            }
            if (ChangedField is not null) result[ChangedField] = ChangedValue;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { result }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
