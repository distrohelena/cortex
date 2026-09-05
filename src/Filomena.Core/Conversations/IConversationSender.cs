namespace Filomena.Core.Conversations;

public sealed record SendReceipt(string ProviderMessageId);

public interface IConversationSender
{
    Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken);
}
