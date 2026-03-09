using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class UserPickerDialogViewModel : DialogBaseViewModel
{
    private readonly List<ChatEditDialogViewModel.SelectableUserItem> _sourceItems;
    private readonly Action<List<int>>? _applySelection;
    private readonly TaskCompletionSource<UserDto?>? _singleSelectTcs;

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

    /// <summary>
    /// true = множественный выбор (чекбоксы), false = одиночный выбор (клик)
    /// </summary>
    [ObservableProperty]
    private bool _isMultiSelect;

    public int SelectedCount => Items.Count(x => x.IsSelected);

    /// <summary>
    /// Результат для single-select режима. Await после ShowAsync.
    /// </summary>
    public Task<UserDto?> SingleSelectResult =>
        _singleSelectTcs?.Task ?? Task.FromResult<UserDto?>(null);

    /// <summary>
    /// Конструктор для multi-select (используется в ChatEditDialog)
    /// </summary>
    public UserPickerDialogViewModel(
        string title,
        IEnumerable<ChatEditDialogViewModel.SelectableUserItem> items,
        bool allowEdit,
        Action<List<int>> applySelection)
    {
        Title = title;
        _sourceItems = items.Select(x => x.Clone()).ToList();
        _applySelection = applySelection;
        _singleSelectTcs = null;
        AllowEdit = allowEdit;
        IsMultiSelect = true;

        Items = new ObservableCollection<ChatEditDialogViewModel.SelectableUserItem>(_sourceItems);
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));
        }

        ApplyFilter();
    }

    /// <summary>
    /// Конструктор для single-select (замена SelectUserDialog)
    /// </summary>
    public UserPickerDialogViewModel(
        string title,
        IEnumerable<UserDto> users,
        string? emptyMessage = null)
    {
        Title = title;
        _applySelection = null;
        _singleSelectTcs = new TaskCompletionSource<UserDto?>();
        AllowEdit = true;
        IsMultiSelect = false;
        CanCloseOnBackgroundClick = true;

        if (emptyMessage != null)
            EmptyMessage = emptyMessage;

        _sourceItems = [.. users.Select(u => new ChatEditDialogViewModel.SelectableUserItem(u, false))];

        Items = new ObservableCollection<ChatEditDialogViewModel.SelectableUserItem>(_sourceItems);
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

    /// <summary>
    /// Выбор пользователя в single-select режиме
    /// </summary>
    [RelayCommand]
    private void SelectSingleUser(ChatEditDialogViewModel.SelectableUserItem item)
    {
        if (IsMultiSelect) return;

        _singleSelectTcs?.TrySetResult(item.User);
        RequestClose();
    }

    /// <summary>
    /// Сохранение в multi-select режиме
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        _applySelection?.Invoke(Items.Where(x => x.IsSelected).Select(x => x.Id).ToList());
        RequestClose();
    }

    protected override void Cancel()
    {
        _singleSelectTcs?.TrySetResult(null);
        base.Cancel();
    }

    protected override void CloseOnBackgroundClick()
    {
        if (CanCloseOnBackgroundClick)
        {
            _singleSelectTcs?.TrySetResult(null);
        }
        base.CloseOnBackgroundClick();
    }
}