using System.Text.Json;
using System.Text.RegularExpressions;
using Filomena.Core.Conversations;
using Filomena.Core.Outbound;

namespace Filomena.OpenClaw.Mcp;

/// <summary>Explicit native bound-send contract; never falls back to messages_send.</summary>
public sealed class OpenClawBoundMcpTransport(IMcpToolClient client, bool allowExactGroupDelivery = false) : IBoundOutboundTransport
{
    private const string Contract = "whatsapp-bound-text:v1";
    private const string GroupContract = "whatsapp-bound-group-text:v1";
    private const string GroupReplyContract = "whatsapp-bound-group-reply:v1";
    private bool IsApprovedGroup(string? recipient) => allowExactGroupDelivery
        && Regex.IsMatch(recipient ?? "", @"\A[0-9]+(-[0-9]+)?@g\.us\z");

    public Task<SendReceipt> SendTextAsync(string route, string text, CancellationToken cancellationToken) =>
        Task.FromException<SendReceipt>(new NotSupportedException("A bound send request is required."));

    public async Task<TransportRouteDescription> DescribeRouteAsync(string route, CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken);
        if (!tools.Any(tool => tool.Name == "messages_send_bound"))
            throw new NotSupportedException("Native bound-send capability is unavailable; legacy fallback is forbidden.");
        var description = await new OpenClawMcpTransport(client).DescribeRouteAsync(route, cancellationToken);
        var recipient = description.Recipient;
        // Persisted WhatsApp delivery routes use E.164; this is syntax normalization,
        // not PN/LID discovery or alias lookup. Keep the legacy adapter unchanged.
        if (description.Channel != "whatsapp" || recipient is null)
            throw new InvalidDataException("Bound WhatsApp route required.");
        if (Regex.IsMatch(recipient, @"\A\+[1-9][0-9]{1,14}\z"))
            recipient = recipient[1..] + "@s.whatsapp.net";
        if (!Regex.IsMatch(recipient, @"\A[0-9]+@(s\.whatsapp\.net|lid)\z") && !IsApprovedGroup(recipient))
            throw new InvalidDataException("Unproven native WhatsApp recipient.");
        return description with { Recipient = recipient };
    }

    public async Task<SendReceipt> SendBoundTextAsync(BoundSendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransportRoute);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        if (request.Channel != "whatsapp" ||
            !Regex.IsMatch(request.AttemptRef ?? "", "\\A[a-f0-9]{32}\\z") ||
            (!Regex.IsMatch(request.Recipient ?? "", "\\A[0-9]+@(s\\.whatsapp\\.net|lid)\\z") && !IsApprovedGroup(request.Recipient)) ||
            request.MaxSinglePartCharacters is < 1 or > 4000 ||
            request.Text.Length > request.MaxSinglePartCharacters)
            throw new ArgumentException("Invalid bound single-part request.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ReplyToMessageId is { } target && (!IsApprovedGroup(request.Recipient) ||
            target.Length > 256 || !Regex.IsMatch(target, @"\A\S+\z")))
            throw new ArgumentException("Invalid bound group reply target.", nameof(request));
        var contract = request.ReplyToMessageId is not null ? GroupReplyContract
            : IsApprovedGroup(request.Recipient) ? GroupContract : Contract;
        var expected = new Dictionary<string, object?>
        {
            ["contractVersion"] = contract, ["attemptRef"] = request.AttemptRef,
            ["accountId"] = request.AccountId, ["recipient"] = request.Recipient,
            ["maxSinglePartCharacters"] = request.MaxSinglePartCharacters,
        };
        if (request.ReplyToMessageId is not null) expected["replyToMessageId"] = request.ReplyToMessageId;
        var response = await client.CallToolAsync("messages_send_bound", new Dictionary<string, object?>
        {
            ["session_key"] = request.TransportRoute,
            ["text"] = request.Text,
            ["expected"] = expected,
        }, cancellationToken);
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            Read(result, "contractVersion") != contract || Read(result, "attemptRef") != request.AttemptRef ||
            Read(result, "channel") != request.Channel || Read(result, "accountId") != request.AccountId ||
            Read(result, "recipient") != request.Recipient || Read(result, "messageId") is not { } messageId ||
            Read(result, "replyToMessageId") != request.ReplyToMessageId)
            throw new InvalidDataException("Bound send outcome unverified. Do not retry automatically.");
        return new SendReceipt(messageId)
        {
            Channel = Read(result, "channel"), AccountId = Read(result, "accountId"),
            Recipient = Read(result, "recipient"), GatewayRunId = Read(result, "runId"),
            ReplyToMessageId = Read(result, "replyToMessageId"),
        };
    }

    private static string? Read(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(field.GetString()) ? field.GetString() : null;
}
