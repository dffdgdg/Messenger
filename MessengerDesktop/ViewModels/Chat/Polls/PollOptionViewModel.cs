using System;

namespace MessengerDesktop.ViewModels.Chat;

public partial class PollOptionViewModel : ObservableObject
{
    private readonly PollViewModel _pollViewModel;
    private readonly PollOptionDto _option;

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial int VotesCount { get; set; }
    [ObservableProperty] public partial double VotesPercentage { get; set; }

    public int Id => _option.Id;
    public string OptionText => _option.Text;
    public int Position => _option.Position;
    public bool CanVote => _pollViewModel.CanVote;

    public void UpdateVotes(int newVotesCount)
    {
        VotesCount = newVotesCount;
        VotesPercentage = _pollViewModel.TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / _pollViewModel.TotalVotes * 100.0, 1);
    }

    [RelayCommand]
    private void ToggleSelection() => IsSelected = !IsSelected;

    public PollOptionViewModel(PollOptionDto option, PollViewModel pollViewModel)
    {
        _option = option;
        _pollViewModel = pollViewModel;
        VotesCount = option.VotesCount;
        IsSelected = false;
        VotesPercentage = pollViewModel.TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / pollViewModel.TotalVotes * 100.0, 1);
    }
}