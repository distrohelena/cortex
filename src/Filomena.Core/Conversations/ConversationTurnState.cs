using System.Threading.Channels;

namespace Filomena.Core.Conversations;

internal sealed class ConversationTurnState(ConversationInfo conversation)
{
    private readonly object startGate = new();
    private Task? processorTask;

    public ConversationInfo Conversation { get; set; } = conversation;

    public Channel<QueuedInbound> Inbox { get; } = Channel.CreateUnbounded<QueuedInbound>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public List<ObservedInbound> History { get; } = [];
    public List<SentConversationTurn> OutboundHistory { get; } = [];

    public DateTimeOffset? LastFilomenaTurnAt { get; set; }
    public DateTimeOffset? LastInboundObservedAt { get; set; }

    public Task Start(Func<Task> processor)
    {
        lock (startGate)
        {
            return processorTask ??= Task.Run(processor);
        }
    }
}

internal sealed record ObservedInbound(
    InboundConversationEvent Message,
    DateTimeOffset ObservedAt);

internal sealed record QueuedInbound(InboundConversationEvent Message, DateTimeOffset ReceivedAt);
