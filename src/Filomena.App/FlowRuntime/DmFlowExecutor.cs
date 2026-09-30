using Filomena.Core.Conversations;
using Filomena.Core.Identity;
using Filomena.Llm.LmStudio;

namespace Filomena.App.FlowRuntime;

public sealed record DmFlowExecutionResult(string Text, string? OutboundMessageId);

public sealed class DmFlowExecutor
{
    private const int MaxTextCharacters = 16_000;
    private readonly IModelResponseChatClient model;
    private readonly IConversationSender sender;

    public DmFlowExecutor(IModelResponseChatClient model, IConversationSender sender)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public async Task<DmFlowExecutionResult> ExecuteAsync(
        CompiledDmFlow flow,
        InboundConversationEvent message,
        bool send,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(message);
        ValidateMessage(message);
        cancellationToken.ThrowIfCancellationRequested();

        var text = message.Content;
        foreach (var prompt in flow.Prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text = string.IsNullOrWhiteSpace(prompt.Model)
                ? await model.CompleteResponseAsync(prompt.Instruction, text, cancellationToken)
                : await model.CompleteResponseAsync(prompt.Instruction, text, prompt.Model, cancellationToken);
            text = text?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Length > MaxTextCharacters)
            {
                throw new InvalidDataException("The model returned empty or oversized text.");
            }
        }

        if (!send)
        {
            return new DmFlowExecutionResult(text, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var receipt = await sender.SendTextAsync(message.SessionKey, text, cancellationToken);
        if (receipt is null || string.IsNullOrWhiteSpace(receipt.ProviderMessageId))
        {
            throw new InvalidDataException("The sender returned an invalid receipt.");
        }

        return new DmFlowExecutionResult(text, receipt.ProviderMessageId);
    }

    private static void ValidateMessage(InboundConversationEvent message)
    {
        if (string.IsNullOrWhiteSpace(message.SessionKey) ||
            string.IsNullOrWhiteSpace(message.ProviderMessageId) ||
            message.Content is null || string.IsNullOrWhiteSpace(message.Content) ||
            message.Content.Length > MaxTextCharacters || message.Timestamp is null ||
            message.Conversation is not { } conversation || conversation.Id is null ||
            conversation.Kind != ConversationKind.DirectMessage ||
            !string.Equals(conversation.SessionKey, message.SessionKey, StringComparison.Ordinal) ||
            message.Sender is not { } sender)
        {
            throw new InvalidDataException("The inbound event is not a complete DM event.");
        }

        var conversationId = conversation.Id;
        if (!string.Equals(conversationId.Channel, "whatsapp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(conversationId.AccountId) ||
            string.IsNullOrWhiteSpace(conversationId.NativeId) ||
            string.IsNullOrWhiteSpace(conversationId.Namespace) ||
            !string.Equals(sender.Channel, conversationId.Channel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sender.AccountId, conversationId.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The inbound DM identity is incoherent.");
        }

        string conversationNativeId;
        string senderNativeId;
        try
        {
            conversationNativeId = FilomenaIdentity.NormalizeWhatsAppActorNativeId(conversationId.NativeId);
            senderNativeId = FilomenaIdentity.NormalizeWhatsAppActorNativeId(sender.NativeId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The inbound DM identity is not a valid WhatsApp identity.", exception);
        }

        if (!string.Equals(conversationId.Namespace,
                FilomenaIdentity.WhatsAppConversationNamespace(conversationNativeId), StringComparison.Ordinal) ||
            !IsDirectMessageNativeId(conversationNativeId) ||
            !IsDirectMessageNativeId(senderNativeId) ||
            !string.Equals(senderNativeId, conversationNativeId, StringComparison.Ordinal) ||
            !string.Equals(sender.StableId, FilomenaIdentity.WhatsAppActorStableId(senderNativeId), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The inbound sender is not the observed DM peer.");
        }
    }

    private static bool IsDirectMessageNativeId(string nativeId) =>
        nativeId.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) ||
        nativeId.EndsWith("@lid", StringComparison.Ordinal);
}
