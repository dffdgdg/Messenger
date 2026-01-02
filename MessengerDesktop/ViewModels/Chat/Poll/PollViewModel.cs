using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO.Chat.Poll;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class PollViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;

    [ObservableProperty] private string _question = string.Empty;
    [ObservableProperty] private ObservableCollection<PollOptionViewModel> _options = [];
    [ObservableProperty] private bool? _allowsMultipleAnswers;
    [ObservableProperty] private bool _canVote = true;
    [ObservableProperty] private bool _isAnonymous;
    [ObservableProperty] private int _totalVotes;

    public int PollId { get; }
    public int UserId { get; }
    public ContextMenu PollContextMenu { get; }

    public PollViewModel(PollDTO poll, int userId, IApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        PollId = poll.Id;
        UserId = userId;
        Question = poll.Question;
        AllowsMultipleAnswers = poll.AllowsMultipleAnswers;
        IsAnonymous = poll.IsAnonymous ?? false;
        TotalVotes = poll.Options.Sum(o => o.VotesCount);

        Options = new ObservableCollection<PollOptionViewModel>(
            poll.Options.Select(o => new PollOptionViewModel(o, this)));

        foreach (var opt in Options)
        {
            opt.PropertyChanged += OnOptionPropertyChanged;
        }

        ApplySelectedOptions(poll.SelectedOptionIds);
        CanVote = poll.CanVote;

        PollContextMenu = CreateContextMenu(poll.SelectedOptionIds?.Count > 0);
    }

    private ContextMenu CreateContextMenu(bool hasVotes) => new()
    {
        Items =
        {
            new MenuItem
            {
                Header = "Отменить голос",
                Command = CancelVoteCommand,
                IsEnabled = hasVotes
            }
        }
    };

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PollOptionViewModel.IsSelected))
            return;

        if (sender is not PollOptionViewModel { IsSelected: true } changed)
            return;

        if (AllowsMultipleAnswers != true)
        {
            foreach (var opt in Options.Where(o => o != changed && o.IsSelected))
            {
                opt.IsSelected = false;
            }
        }
    }

    public void ApplyDto(PollDTO dto)
    {
        Question = dto.Question;
        AllowsMultipleAnswers = dto.AllowsMultipleAnswers;
        TotalVotes = dto.Options.Sum(o => o.VotesCount);

        UpdateOptions(dto.Options);
        ApplySelectedOptions(dto.SelectedOptionIds);

        CanVote = dto.CanVote;
        UpdateContextMenuState(dto.SelectedOptionIds?.Count > 0);
    }

    private void UpdateOptions(List<PollOptionDTO> optionDtos)
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
        {
            opt.IsSelected = selected.Contains(opt.Id);
        }
    }

    private void UpdateContextMenuState(bool hasVotes)
    {
        if (PollContextMenu.Items.Count > 0 && PollContextMenu.Items[0] is MenuItem cancelItem)
        {
            cancelItem.IsEnabled = hasVotes;
        }
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

            var voteDto = new PollVoteDTO
            {
                PollId = PollId,
                UserId = UserId,
                OptionIds = selectedIds
            };

            var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>(ApiEndpoints.Poll.Vote, voteDto);

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
                SuccessMessage = "Голос учтён";
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

            var voteDto = new PollVoteDTO
            {
                PollId = PollId,
                UserId = UserId,
                OptionIds = []
            };

            var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>(ApiEndpoints.Poll.Vote, voteDto);

            if (result is { Success: true, Data: not null })
            {
                ApplyDto(result.Data);
                SuccessMessage = "Голос отменён";
            }
            else
            {
                ErrorMessage = $"Ошибка отмены голоса: {result.Error}";
            }
        });
    }
}