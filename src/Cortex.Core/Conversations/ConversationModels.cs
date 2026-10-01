namespace Cortex.Core.Conversations;

public enum ConversationKind
{
    Unknown,
    DirectMessage,
    Group,
}

public sealed record ConversationId(
    string Channel,
    string AccountId,
    string NativeId,
    string Namespace,
    string? ThreadId = null);

public sealed record ConversationInfo(
    ConversationId Id,
    string SessionKey,
    ConversationKind Kind,
    string? DisplayName,
    string? Label,
    string? DerivedTitle,
    string? LastMessagePreview,
    DateTimeOffset? UpdatedAt);

public sealed record ActorRef(
    string Channel,
    string AccountId,
    string NativeId,
    string StableId,
    string? DisplayName = null,
    string? RawNativeId = null);

public sealed record ReplyRef(string ProviderMessageId);

public sealed record InboundConversationEvent(
    long Cursor,
    string SessionKey,
    ConversationInfo? Conversation,
    string ProviderMessageId,
    string Content,
    ActorRef? Sender,
    DateTimeOffset? Timestamp,
    ReplyRef? ReplyTo,
    int? MessageSequence,
    IReadOnlyDictionary<string, object?> StructuralMetadata);
