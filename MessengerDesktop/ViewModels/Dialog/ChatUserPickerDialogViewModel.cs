using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class ChatUserPickerDialogViewModel : DialogBaseViewModel
{
    private readonly List<ChatEditDialogViewModel.SelectableUserItem> _sourceItems;
    private readonly Action<List<int>> _applySelection;

    [ObservableProperty]
    private ObservableCollection<ChatEditDialogViewModel.SelectableUserItem> _items = [];

    [ObservableProperty]
    private ObservableCollection<ChatEditDialogViewModel.SelectableUserItem> _filteredItems = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _allowEdit;

    [ObservableProperty]
    private string _emptyMessage = "Пользователи не найдены";

    public int SelectedCount => Items.Count(x => x.IsSelected);

    public ChatUserPickerDialogViewModel(
        string title,
        IEnumerable<ChatEditDialogViewModel.SelectableUserItem> items,
        bool allowEdit,
        Action<List<int>> applySelection)
    {
        Title = title;
        _sourceItems = items.Select(x => x.Clone()).ToList();
        _applySelection = applySelection;
        AllowEdit = allowEdit;

        Items = new ObservableCollection<ChatEditDialogViewModel.SelectableUserItem>(_sourceItems);
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));
        }

        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredItems = new ObservableCollection<ChatEditDialogViewModel.SelectableUserItem>(Items);
            return;
        }

        var filtered = Items.Where(u =>
            u.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
            u.Username.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        FilteredItems = new ObservableCollection<ChatEditDialogViewModel.SelectableUserItem>(filtered);
    }

    [RelayCommand]
    private void Save()
    {
        _applySelection(Items.Where(x => x.IsSelected).Select(x => x.Id).ToList());
        RequestClose();
    }

    [RelayCommand]
    private void Cancel() => RequestClose();
}
