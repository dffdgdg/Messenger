namespace Core.ViewModels.Chat.Polls;

public sealed class PollVoterViewModel
{
    public int UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Avatar { get; init; }
}