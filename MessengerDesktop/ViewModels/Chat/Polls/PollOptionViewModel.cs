using System;

namespace MessengerDesktop.ViewModels.Chat;

public partial class PollOptionViewModel(PollOptionDto option, PollViewModel pollViewModel) : ObservableObject
{
    [ObservableProperty] public partial bool IsSelected { get; set; } = false;
    [ObservableProperty] public partial int VotesCount { get; set; } = option.VotesCount;
    [ObservableProperty] public partial double VotesPercentage { get; set; } = pollViewModel.TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / pollViewModel.TotalVotes * 100.0, 1);

    public int Id => option.Id;
    public string OptionText => option.Text;
    public int Position => option.Position;
    public bool CanVote => pollViewModel.CanVote;

    public void UpdateVotes(int newVotesCount)
    {
        VotesCount = newVotesCount;
        VotesPercentage = pollViewModel.TotalVotes == 0 ? 0 : Math.Round((double)VotesCount / pollViewModel.TotalVotes * 100.0, 1);
    }

    [RelayCommand]
    private void ToggleSelection() => IsSelected = !IsSelected;
}