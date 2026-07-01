using Core.Features.Chat.ViewModels.Polls;
using Shared.Contracts.Poll;
using Shared.Contracts.User;

namespace Core.Features.Chat.ViewModels.Polls;

public sealed partial class PollResultOptionViewModel : ObservableObject
{
    public string OptionText { get; }
    public int VotesCount { get; }
    public double VotesPercentage { get; }
    public double VotesFraction { get; }
    public bool IsAnonymous { get; }
    public List<int> VoterIds { get; }

    [ObservableProperty]
    public partial ObservableCollection<PollVoterViewModel> Voters { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoadingVoters { get; set; } = true;

    public PollResultOptionViewModel(PollOptionDto option, bool isAnonymous, int totalVotes)
    {
        OptionText = option.Text;
        VotesCount = option.VotesCount;
        IsAnonymous = isAnonymous;
        VotesFraction = totalVotes == 0 ? 0 : (double)VotesCount / totalVotes;
        VotesPercentage = System.Math.Round(VotesFraction * 100, 1);
        VoterIds = option.Votes.ConvertAll(v => v.UserId);
    }

    public void ApplyVoters(Dictionary<int, UserDto> resolved)
    {
        var voters = VoterIds.ConvertAll(id => resolved.TryGetValue(id, out var user) ? new PollVoterViewModel
        {
            UserId = id,
            DisplayName = user.DisplayName ?? user.Username ?? $"Пользователь {id}",
            Avatar = user.Avatar
        } : new PollVoterViewModel
        {
            UserId = id,
            DisplayName = $"Пользователь {id}",
            Avatar = null
        });

        Voters = new ObservableCollection<PollVoterViewModel>(voters);
        IsLoadingVoters = false;
    }
}