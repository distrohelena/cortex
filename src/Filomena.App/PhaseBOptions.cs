using Cortex.Core.Conversations;

namespace Filomena.App;

public sealed class PhaseBOptions
{
    public const string SectionName = "PhaseB";

    public ConversationTurnOptions Turns { get; set; } = new();
}
