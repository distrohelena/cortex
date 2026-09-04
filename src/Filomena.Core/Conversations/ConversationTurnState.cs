using System.Threading.Channels;

namespace Filomena.Core.Conversations;

internal sealed class ConversationTurnState(ConversationInfo conversation)
{
    private readonly object startGate = new();
    private Task? processorTask;

    public ConversationInfo Conversation { get; } = conversation;

    public Channel<InboundConversationEvent> Inbox { get; } = Channel.CreateUnbounded<InboundConversationEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public List<ObservedInbound> History { get; } = [];

    public DateTimeOffset? LastFilomenaTurnAt { get; set; }

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
