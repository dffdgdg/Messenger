using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class PollViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string question = string.Empty;

        [ObservableProperty]
        private ObservableCollection<PollOptionViewModel> options = [];

        [ObservableProperty]
        private bool? allowsMultipleAnswers;

        public int PollId { get; }
        public int UserId { get; }

        [ObservableProperty]
        private bool canVote = true;

        [ObservableProperty]
        private bool isAnonymous;

        [ObservableProperty]
        private int totalVotes;

        private readonly HttpClient _httpClient;
        public ContextMenu PollContextMenu { get; }

        public PollViewModel(PollDTO poll, int userId, HttpClient httpClient)
        {
            PollId = poll.Id;
            UserId = userId;
            question = poll.Question;
            allowsMultipleAnswers = poll.AllowsMultipleAnswers;
            isAnonymous = poll.IsAnonymous ?? false;
            _httpClient = httpClient;
            totalVotes = poll.Options.Sum(o => o.VotesCount);

            options = new ObservableCollection<PollOptionViewModel>(
                poll.Options.Select(o => new PollOptionViewModel(o, this)));

            PollContextMenu = new ContextMenu
            {
                Items =
                {
                    new MenuItem
                    {
                        Header = "Отменить голос",
                        Command = CancelVoteCommand,
                        IsEnabled = poll.SelectedOptionIds?.Count > 0
                    }
                }
            };

            foreach (var opt in options)
                opt.PropertyChanged += Option_PropertyChanged;

            if (poll.SelectedOptionIds != null && poll.SelectedOptionIds.Count > 0)
            {
                foreach (var opt in options)
                    opt.IsSelected = poll.SelectedOptionIds.Contains(opt.Id);
            }

            CanVote = poll.CanVote;
        }

        private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PollOptionViewModel.IsSelected) && sender is PollOptionViewModel changed && changed.IsSelected)
            {
                if (AllowsMultipleAnswers != true)
                {
                    foreach (var opt in Options)
                    {
                        if (opt != changed && opt.IsSelected)
                        {
                            opt.IsSelected = false;
                        }
                    }
                }
            }
        }

        public void ApplyDto(PollDTO dto)
        {
            if (dto == null) return;
            Question = dto.Question;
            AllowsMultipleAnswers = dto.AllowsMultipleAnswers;

            TotalVotes = dto.Options.Sum(o => o.VotesCount);

            foreach (var optDto in dto.Options)
            {
                var vm = Options.FirstOrDefault(o => o.Id == optDto.Id);
                if (vm != null)
                {
                    vm.UpdateVotes(optDto.VotesCount);
                }
                else
                {
                    var newVm = new PollOptionViewModel(optDto, this);
                    newVm.PropertyChanged += Option_PropertyChanged;
                    Options.Add(newVm);
                }
            }

            var ids = dto.Options.Select(o => o.Id).ToHashSet();
            for (int i = Options.Count - 1; i >= 0; i--)
            {
                if (!ids.Contains(Options[i].Id))
                    Options.RemoveAt(i);
            }

            var selected = dto.SelectedOptionIds ?? [];
            foreach (var opt in Options)
            {
                opt.IsSelected = selected.Contains(opt.Id);
            }

            CanVote = dto.CanVote;
            if (PollContextMenu.Items.Count > 0 && PollContextMenu.Items[0] is MenuItem cancelItem)
            {
                cancelItem.IsEnabled = selected.Count > 0;
            }
        }

        [RelayCommand]
        private async Task Vote()
        {
            var selected = Options.Where(o => o.IsSelected).Select(o => o.Id).ToList();
            if (selected.Count == 0)
            {
                try { await NotificationService.ShowInfo("Выберите опцию перед голосованием"); } catch { }
                return;
            }

            try
            {
                var voteDto = new PollVoteDTO
                {
                    PollId = PollId,
                    UserId = UserId,
                    OptionIds = selected
                };

                var response = await _httpClient.PostAsJsonAsync("api/poll/vote", voteDto);
                if (response != null && response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<PollDTO>();
                    if (updated != null)
                    {
                        ApplyDto(updated);
                        try { await NotificationService.ShowSuccess("Голос учтён"); } catch { }
                    }
                }
                else
                {
                    var err = response == null ? "no response" : await response.Content.ReadAsStringAsync();
                    try { await NotificationService.ShowError($"Ошибка голосования: {err}"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try {await NotificationService.ShowError($"Ошибка голосования: {ex.Message}"); } catch { }
            }
        }

        [RelayCommand]
        private async Task CancelVote()
        {
            try
            {
                var voteDto = new PollVoteDTO
                {
                    PollId = PollId,
                    UserId = UserId,
                    OptionIds = []
                };

                var response = await _httpClient.PostAsJsonAsync("api/poll/vote", voteDto);
                if (response != null && response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<PollDTO>();
                    if (updated != null)
                    {
                        ApplyDto(updated);
                        try {await NotificationService.ShowInfo("Голос отменён"); } catch { }
                    }
                }
                else
                {
                    var err = response == null ? "no response" : await response.Content.ReadAsStringAsync();
                    try {await NotificationService.ShowError($"Ошибка отмены голоса: {err}"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try {await NotificationService.ShowError($"Ошибка отмены голоса: {ex.Message}"); } catch { }
            }
        }
    }
}
