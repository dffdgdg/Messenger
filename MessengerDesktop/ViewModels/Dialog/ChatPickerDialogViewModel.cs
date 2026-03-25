using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class ChatPickerDialogViewModel : DialogBaseViewModel
{
    private readonly TaskCompletionSource<ChatDto?> _singleSelectTcs = new();

    [ObservableProperty]public partial ObservableCollection<ChatDto> Items { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<ChatDto> FilteredItems { get; set; } = [];
    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial string EmptyMessage { get; set; } = "Чаты не найдены";

    public Task<ChatDto?> SingleSelectResult => _singleSelectTcs.Task;

    public ChatPickerDialogViewModel(string title, IEnumerable<ChatDto> chats, string? emptyMessage = null)
    {
        Title = title;
        CanCloseOnBackgroundClick = true;

        if (!string.IsNullOrWhiteSpace(emptyMessage))
            EmptyMessage = emptyMessage;

        Items = new ObservableCollection<ChatDto>(chats);
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredItems = new ObservableCollection<ChatDto>(Items);
            return;
        }

        var query = SearchQuery.Trim();
        var filtered = Items.Where(c =>
            (!string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(c.LastMessagePreview) && c.LastMessagePreview.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(c.LastMessageSenderName) && c.LastMessageSenderName.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredItems = new ObservableCollection<ChatDto>(filtered);
    }

    [RelayCommand]
    private void SelectChat(ChatDto chat)
    {
        _singleSelectTcs.TrySetResult(chat);
        RequestClose();
    }

    protected override void Cancel()
    {
        _singleSelectTcs.TrySetResult(null);
        base.Cancel();
    }

    protected override void CloseOnBackgroundClick()
    {
        if (CanCloseOnBackgroundClick)
            _singleSelectTcs.TrySetResult(null);

        base.CloseOnBackgroundClick();
    }
}