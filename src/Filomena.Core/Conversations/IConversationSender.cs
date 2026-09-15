namespace Filomena.Core.Conversations;

// Observed transport evidence, not a send authorization or a proof of delivery/read.
// Missing metadata stays missing; never reconstruct it from a session label.
public sealed record SendReceipt(string ProviderMessageId)
{
    public string? Channel { get; init; }
    public string? AccountId { get; init; }
    public string? Recipient { get; init; }
    public string? GatewayRunId { get; init; }
}

public interface IConversationSender
{
    Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken);
}
