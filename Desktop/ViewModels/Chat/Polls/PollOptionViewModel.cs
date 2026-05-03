using Shared.Dto.Poll;
using System;

namespace Desktop.ViewModels.Chat;

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

    public double VotesFraction => TotalVotes == 0 ? 0 : (double)VotesCount / TotalVotes;
    private int TotalVotes => _pollViewModel.TotalVotes;
    public bool AllowsMultipleAnswers => _pollViewModel.AllowsMultipleAnswers;

    public int Id => _option.Id;
    public string OptionText => _option.Text;
    public int Position => _option.Position;
    public bool CanVote => _pollViewModel.CanVote;

    public void UpdateVotes(int newVotesCount)
    {
        VotesCount = newVotesCount;
        VotesPercentage = TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / TotalVotes * 100.0, 1);
    }

    [RelayCommand]
    private void ToggleSelection() => IsSelected = !IsSelected;

    public PollOptionViewModel(PollOptionDto option, PollViewModel pollViewModel)
    {
        _option = option;
        _pollViewModel = pollViewModel;
        VotesCount = option.VotesCount;
        IsSelected = false;
        VotesPercentage = TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / TotalVotes * 100.0, 1);
    }
}