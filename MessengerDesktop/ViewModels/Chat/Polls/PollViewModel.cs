using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class PollViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;

    [ObservableProperty] private ObservableCollection<PollOptionViewModel> _options = [];
    [ObservableProperty] private bool _allowsMultipleAnswers;
    [ObservableProperty] private bool _canVote = true;
    [ObservableProperty] private bool _isAnonymous;
    [ObservableProperty] private int _totalVotes;
    [ObservableProperty] private bool _hasVoted;

    public int PollId { get; }
    public int UserId { get; }
    public bool HasSelection => Options.Any(o => o.IsSelected);

    public PollViewModel(PollDto poll, int userId, IApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        PollId = poll.Id;
        UserId = userId;
        AllowsMultipleAnswers = poll.AllowsMultipleAnswers;
        IsAnonymous = poll.IsAnonymous;
        TotalVotes = poll.Options.Sum(o => o.VotesCount);

        Options = new ObservableCollection<PollOptionViewModel>(
            poll.Options.Select(o => new PollOptionViewModel(o, this)));

        foreach (var opt in Options)
            opt.PropertyChanged += OnOptionPropertyChanged;

        ApplySelectedOptions(poll.SelectedOptionIds);
        CanVote = poll.CanVote;
        HasVoted = !poll.CanVote;
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PollOptionViewModel.IsSelected))
            return;

        OnPropertyChanged(nameof(HasSelection));

        if (sender is not PollOptionViewModel { IsSelected: true } changed)
            return;

        if (!AllowsMultipleAnswers)
        {
            foreach (var opt in Options.Where(o => o != changed && o.IsSelected))
                opt.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectOption(PollOptionViewModel? option)
    {
        if (option == null || !CanVote)
            return;
        option.IsSelected = !option.IsSelected;
    }

    public void ApplyDto(PollDto dto)
    {
        AllowsMultipleAnswers = dto.AllowsMultipleAnswers;
        TotalVotes = dto.Options.Sum(o => o.VotesCount);

        UpdateOptions(dto.Options);
        ApplySelectedOptions(dto.SelectedOptionIds);

        CanVote = dto.CanVote;
        HasVoted = !dto.CanVote;
    }

    private void UpdateOptions(List<PollOptionDto> optionDtos)
    {
        foreach (var optDto in optionDtos)
        {
            var vm = Options.FirstOrDefault(o => o.Id == optDto.Id);
            if (vm != null)
            {
                vm.UpdateVotes(optDto.VotesCount);
            }
            else
            {
                var newVm = new PollOptionViewModel(optDto, this);
                newVm.PropertyChanged += OnOptionPropertyChanged;
                Options.Add(newVm);
            }
        }

        var validIds = optionDtos.Select(o => o.Id).ToHashSet();
        for (int i = Options.Count - 1; i >= 0; i--)
        {
            if (!validIds.Contains(Options[i].Id))
            {
                Options[i].PropertyChanged -= OnOptionPropertyChanged;
                Options.RemoveAt(i);
            }
        }
    }

    private void ApplySelectedOptions(List<int>? selectedIds)
    {
        var selected = selectedIds ?? [];
        foreach (var opt in Options)
            opt.IsSelected = selected.Contains(opt.Id);
    }

    [RelayCommand]
    private async Task Vote()
    {
        var selectedIds = Options.Where(o => o.IsSelected).Select(o => o.Id).ToList();

        if (selectedIds.Count == 0)
        {
            ErrorMessage = "Необходимо выбрать хотя бы один вариант";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            Debug.WriteLine($"[Poll] Voting: PollId={PollId}, Options=[{string.Join(", ", selectedIds)}]");

            var voteDto = new PollVoteDto
            {
                PollId = PollId,
                UserId = UserId,
                OptionIds = selectedIds
            };

            var result = await _apiClient.PostAsync<PollVoteDto, PollDto>(ApiEndpoints.Poll.Vote, voteDto);

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
            }
            else
            {
                ErrorMessage = $"Ошибка голосования: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task CancelVote()
    {
        await SafeExecuteAsync(async () =>
        {
            Debug.WriteLine($"[Poll] Cancelling vote: PollId={PollId}");

            var voteDto = new PollVoteDto
            {
                PollId = PollId,
                UserId = UserId,
                OptionIds = []
            };

            var result = await _apiClient.PostAsync<PollVoteDto, PollDto>(ApiEndpoints.Poll.Vote, voteDto);

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
            }
            else
            {
                ErrorMessage = $"Ошибка отмены голоса: {result.Error}";
            }
        });
    }
}