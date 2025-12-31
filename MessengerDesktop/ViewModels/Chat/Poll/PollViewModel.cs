using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO.Chat.Poll;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class PollViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;

    [ObservableProperty]
    private string question = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PollOptionViewModel> options = [];

    [ObservableProperty]
    private bool? allowsMultipleAnswers;

    [ObservableProperty]
    private bool canVote = true;

    [ObservableProperty]
    private bool isAnonymous;

    [ObservableProperty]
    private int totalVotes;

    public int PollId { get; }
    public int UserId { get; }
    public ContextMenu PollContextMenu { get; }

    public PollViewModel(PollDTO poll, int userId, IApiClientService apiClient)
    {
        _apiClient = apiClient;

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

        if (poll.SelectedOptionIds is { Count: > 0 })
        {
            foreach (var opt in Options.Where(o => poll.SelectedOptionIds.Contains(o.Id)))
            {
                opt.IsSelected = true;
            }
        }

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
                newVm.PropertyChanged += OnOptionPropertyChanged;
                Options.Add(newVm);
            }
        }

        var validIds = dto.Options.Select(o => o.Id).ToHashSet();
        for (int i = Options.Count - 1; i >= 0; i--)
        {
            if (!validIds.Contains(Options[i].Id))
            {
                Options[i].PropertyChanged -= OnOptionPropertyChanged;
                Options.RemoveAt(i);
            }
        }

        var selected = dto.SelectedOptionIds ?? [];
        foreach (var opt in Options)
        {
            opt.IsSelected = selected.Contains(opt.Id);
        }

        CanVote = dto.CanVote;
        UpdateContextMenuState(selected.Count > 0);
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
            ErrorMessage = "���������� ������� ���� �� ���� �������";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            Debug.WriteLine($"=== SENDING VOTE === PollId: {PollId}, UserId: {UserId}, Options: [{string.Join(", ", selectedIds)}]");

            var voteDto = new PollVoteDTO
            {
                PollId = PollId,
                UserId = UserId,
                OptionIds = selectedIds
            };

            var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>("api/poll/vote", voteDto);

            if (result is { Success: true, Data: not null })
            {
                Debug.WriteLine($"Vote successful, options count: {result.Data.Options.Count}");
                ApplyDto(result.Data);
                SuccessMessage = "����� ����";
            }
            else
            {
                Debug.WriteLine($"Vote failed: {result.Error}");
                ErrorMessage = $"������ �����������: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task CancelVote() => await SafeExecuteAsync(async () =>
    {
        Debug.WriteLine($"=== CANCELLING VOTE === PollId: {PollId}, UserId: {UserId}");
        var voteDto = new PollVoteDTO
        {
            PollId = PollId,
            UserId = UserId,
            OptionIds = []
        };

        var result = await _apiClient.PostAsync<PollVoteDTO, PollDTO>("api/poll/vote", voteDto);

        if (result is { Success: true, Data: not null })
        {
            ApplyDto(result.Data);
            SuccessMessage = "����� ������";
        }
        else
        {
            ErrorMessage = $"������ ������ ������: {result.Error}";
        }
    });
}