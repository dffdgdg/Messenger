using CommunityToolkit.Mvvm.Input;
using Shared.Contracts.Poll;

namespace Core.ViewModels.Chat;

public partial class PollOptionViewModel : ObservableObject
{
    private readonly PollViewModel _pollViewModel;
    private readonly PollOptionDto _option;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VotesFraction))]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VotesFraction))]
    public partial int VotesCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VotesFraction))]
    public partial double VotesPercentage { get; set; }

    [ObservableProperty]
    public partial bool CanVote { get; set; }

    public double VotesFraction => _pollViewModel.TotalVotes == 0 ? 0 : (double)VotesCount / _pollViewModel.TotalVotes;

    public bool AllowsMultipleAnswers => _pollViewModel.AllowsMultipleAnswers;

    public int Id => _option.Id;
    public string OptionText => _option.Text;
    public int Position => _option.Position;

    public void UpdateVotes(int newVotesCount)
    {
        VotesCount = newVotesCount;
        VotesPercentage = _pollViewModel.TotalVotes == 0
            ? 0
            : Math.Round((double)VotesCount / _pollViewModel.TotalVotes * 100.0, 1);
        OnPropertyChanged(nameof(VotesFraction));
    }

    public void NotifyTotalVotesChanged()
    {
        var currentVotes = VotesCount;
        VotesCount = 0;
        VotesCount = currentVotes;
    }

    public void NotifyCanVoteChanged(bool canVote) => CanVote = canVote;

    [RelayCommand]
    private void ToggleSelection() => IsSelected = !IsSelected;

    public PollOptionViewModel(PollOptionDto option, PollViewModel pollViewModel)
    {
        _option = option;
        _pollViewModel = pollViewModel;
        VotesCount = option.VotesCount;
        IsSelected = false;
        CanVote = pollViewModel.CanVote;
        VotesPercentage = pollViewModel.TotalVotes == 0
            ? 0
            : Math.Round((double)VotesCount / pollViewModel.TotalVotes * 100.0, 1);
    }
}