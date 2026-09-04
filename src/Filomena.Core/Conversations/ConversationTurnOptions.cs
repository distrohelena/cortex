namespace Filomena.Core.Conversations;

public sealed record ConversationTurnOptions
{
    public string[] AllowlistedDirectMessageNativeIds { get; init; } = [];

    public string[] FilomenaAliases { get; init; } = ["@Filomena", "Filomena"];

    public TimeSpan AllowlistedDirectMessageQuietWindow { get; init; } = TimeSpan.FromSeconds(1.8);

    public TimeSpan AllowlistedDirectMessageMaxBurst { get; init; } = TimeSpan.FromSeconds(6);

    public TimeSpan DirectAddressGroupQuietWindow { get; init; } = TimeSpan.FromSeconds(1.5);

    public TimeSpan DirectAddressGroupMaxBurst { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan GroupQuietWindow { get; init; } = TimeSpan.FromSeconds(3.5);

    public TimeSpan GroupMaxBurst { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WaitReevaluationDelay { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxWaitReevaluations { get; init; } = 1;

    public int RecentMessageLimit { get; init; } = 16;

    public int RecentCharacterLimit { get; init; } = 3000;

    public TimeSpan RecentWindow { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        ValidatePositive(AllowlistedDirectMessageQuietWindow, nameof(AllowlistedDirectMessageQuietWindow));
        ValidatePositive(AllowlistedDirectMessageMaxBurst, nameof(AllowlistedDirectMessageMaxBurst));
        ValidatePositive(DirectAddressGroupQuietWindow, nameof(DirectAddressGroupQuietWindow));
        ValidatePositive(DirectAddressGroupMaxBurst, nameof(DirectAddressGroupMaxBurst));
        ValidatePositive(GroupQuietWindow, nameof(GroupQuietWindow));
        ValidatePositive(GroupMaxBurst, nameof(GroupMaxBurst));
        ValidatePositive(WaitReevaluationDelay, nameof(WaitReevaluationDelay));
        ValidatePositive(RecentWindow, nameof(RecentWindow));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWaitReevaluations, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(RecentMessageLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(RecentCharacterLimit, 1);
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, "Duration must be positive.");
        }
    }
}

public interface IConversationClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class TimeProviderConversationClock(TimeProvider timeProvider) : IConversationClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
