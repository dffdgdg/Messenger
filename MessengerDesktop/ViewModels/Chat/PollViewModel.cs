using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class PollViewModel : BaseViewModel
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

        private readonly IApiClientService _apiClient;
        public ContextMenu PollContextMenu { get; }

        public PollViewModel(PollDTO poll, int userId, IApiClientService apiClient)
        {
            PollId = poll.Id;
            UserId = userId;
            question = poll.Question;
            allowsMultipleAnswers = poll.AllowsMultipleAnswers;
            isAnonymous = poll.IsAnonymous ?? false;
            _apiClient = apiClient;
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
                ErrorMessage = "Пожалуйста выберите вариант ответа";
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"=== SENDING VOTE ===");
                System.Diagnostics.Debug.WriteLine($"PollId: {PollId}, UserId: {UserId}");
                System.Diagnostics.Debug.WriteLine($"Selected options: {string.Join(", ", selected)}");

                var voteDto = new PollVoteDTO
                {
                    PollId = PollId,
                    UserId = UserId,
                    OptionIds = selected
                };

                var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>("api/poll/vote", voteDto);

                if (result.Success && result.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Vote successful, received updated poll with {result.Data.Options.Count} options");
                    ApplyDto(result.Data);
                    SuccessMessage = "Голос учтен";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Vote failed: {result.Error}");
                    ErrorMessage = $"Ошибка голосования: {result.Error}";
                }
            });
        }

        [RelayCommand]
        private async Task CancelVote()
        {
            await SafeExecuteAsync(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"=== CANCELLING VOTE ===");
                System.Diagnostics.Debug.WriteLine($"PollId: {PollId}, UserId: {UserId}");

                var voteDto = new PollVoteDTO
                {
                    PollId = PollId,
                    UserId = UserId,
                    OptionIds = []
                };

                var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>("api/poll/vote", voteDto);

                if (result.Success && result.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine("Vote cancelled successfully");
                    ApplyDto(result.Data);
                    SuccessMessage = "Голос отменен";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Cancel vote failed: {result.Error}");
                    ErrorMessage = $"Ошибка отмены голоса: {result.Error}";
                }
            });
        }
    }
}