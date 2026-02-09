using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO.Chat.Poll;
using System;

namespace MessengerDesktop.ViewModels.Chat
{
    public partial class PollOptionViewModel : ObservableObject
    {
        private readonly PollViewModel _pollViewModel;
        private readonly PollOptionDTO _option;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private int _votesCount;

        [ObservableProperty]
        private double _votesPercentage;

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

        public PollOptionViewModel(PollOptionDTO option, PollViewModel pollViewModel)
        {
            _option = option;
            _pollViewModel = pollViewModel;
            _votesCount = option.VotesCount;
            _isSelected = false;
            VotesPercentage = pollViewModel.TotalVotes == 0 ? 0 : Math.Round((double)_votesCount / pollViewModel.TotalVotes * 100.0, 1);
        }
    }
}