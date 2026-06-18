using CommunityToolkit.Mvvm.ComponentModel;
using Core.Services.Abstractions;
using System.ComponentModel;

namespace Core.ViewModels.Chat;

public partial class PollViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;

    public event Action<PollDto>? ServerStateApplied;
    public string TotalVotesFormatted => Pluralize(TotalVotes);

    [ObservableProperty] public partial ObservableCollection<PollOptionViewModel> Options { get; set; } = [];
    [ObservableProperty] public partial bool AllowsMultipleAnswers { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultsButton))]
    public partial bool IsAnonymous { get; set; }
    [ObservableProperty] public partial int TotalVotes { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultsButton))]
    public partial bool HasVoted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultsButton))]
    public partial bool CanVote { get; set; } = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultsButton))]
    public partial bool IsClosed { get; set; }
    public event Action<PollViewModel>? ShowResultsRequested;
    [ObservableProperty]
    public partial bool CanClose { get; set; }

    public int PollId { get; }
    public int UserId { get; }
    public bool HasSelection => Options.Any(o => o.IsSelected);
    public bool ShowResultsButton => !IsAnonymous && (HasVoted || IsClosed);
    public PollDto? CurrentPollDto { get; private set; }
    private static string Pluralize(int count)
    {
        var abs = Math.Abs(count) % 100;
        var n1 = abs % 10;
        if (abs > 10 && abs < 20) return $"{count} голосов";
        if (n1 > 1 && n1 < 5) return $"{count} голоса";
        if (n1 == 1) return $"{count} голос";
        return $"{count} голосов";
    }
    public PollViewModel(PollDto poll, int userId, IApiClientService apiClient, int? pollOwnerId = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        PollId = poll.Id;
        UserId = userId;
        AllowsMultipleAnswers = poll.AllowsMultipleAnswers;
        IsAnonymous = poll.IsAnonymous;
        IsClosed = ComputeIsClosed(poll.ClosesAt);
        CanClose = !IsClosed && userId == pollOwnerId;

        var options = poll.Options ?? [];
        var selectedOptionIds = poll.SelectedOptionIds ?? [];

        TotalVotes = options.Sum(o => o.VotesCount);
        CanVote = poll.CanVote && !IsClosed;
        HasVoted = selectedOptionIds.Count > 0;
        CurrentPollDto = poll;

        Options = new ObservableCollection<PollOptionViewModel>(
            options.Select(o => new PollOptionViewModel(o, this)));

        foreach (var opt in Options)
            opt.PropertyChanged += OnOptionPropertyChanged;

        ApplySelectedOptions(selectedOptionIds);
    }

    [RelayCommand]
    private void ShowResults() => ShowResultsRequested?.Invoke(this);
    private static bool ComputeIsClosed(DateTime? closesAt)
        => closesAt <= DateTime.UtcNow;

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PollOptionViewModel.IsSelected))
            return;

        OnPropertyChanged(nameof(HasSelection));

        if (sender is not PollOptionViewModel { IsSelected: true } changed)
            return;

        if (!AllowsMultipleAnswers)
        {
            foreach (PollOptionViewModel? opt in Options.Where(o => o != changed && o.IsSelected))
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

    [RelayCommand]
    private async Task ClosePoll()
    {
        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.PostAsync<object, PollDto>(
                ApiEndpoints.Polls.Close(PollId), new { });

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
                ServerStateApplied?.Invoke(result.Data);
            }
            else
            {
                ErrorMessage = result?.Error ?? "Ошибка завершения опроса";
            }
        });
    }
    partial void OnCanVoteChanged(bool value)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[PollVM] pollId={PollId} CanVote={value} IsAnonymous={IsAnonymous} ShowResultsButton={ShowResultsButton}");
    }

    public void ApplyDto(PollDto dto)
    {
        AllowsMultipleAnswers = dto.AllowsMultipleAnswers;

        var options = dto.Options ?? [];
        TotalVotes = options.Sum(o => o.VotesCount);
        OnPropertyChanged(nameof(TotalVotesFormatted));

        UpdateOptions(options);
        var selectedOptionIds = dto.SelectedOptionIds ?? [];
        ApplySelectedOptions(selectedOptionIds);

        IsClosed = ComputeIsClosed(dto.ClosesAt);
        CanVote = dto.CanVote && !IsClosed;
        HasVoted = selectedOptionIds.Count > 0;
        CurrentPollDto = dto;

        foreach (var opt in Options)
        {
            opt.NotifyTotalVotesChanged();
            opt.NotifyCanVoteChanged(CanVote);
        }
    }

    private void UpdateOptions(List<PollOptionDto> optionDtos)
    {
        optionDtos ??= [];

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
        if (selectedIds.Count == 0) { ErrorMessage = "Необходимо выбрать хотя бы один вариант"; return; }

        await SafeExecuteAsync(async () =>
        {
            var voteDto = new PollVoteDto { PollId = PollId, UserId = UserId, OptionIds = selectedIds };
            var result = await _apiClient.PostAsync<PollVoteDto, PollDto>(ApiEndpoints.Polls.Vote, voteDto);

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
                ServerStateApplied?.Invoke(result.Data);
            }
            else
            {
                ErrorMessage = $"Ошибка голосования: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task CancelVote() => await SafeExecuteAsync(async () =>
    {
        var voteDto = new PollVoteDto
        {
            PollId = PollId,
            UserId = UserId,
            OptionIds = []
        };

        var result = await _apiClient.PostAsync<PollVoteDto, PollDto>(ApiEndpoints.Polls.Vote, voteDto);

        if (result is { Success: true, Data: not null })
        {
            ApplyDto(result.Data);
            ServerStateApplied?.Invoke(result.Data);
        }
        else
        {
            ErrorMessage = $"Ошибка отмены голоса: {result.Error}";
        }
    });
}