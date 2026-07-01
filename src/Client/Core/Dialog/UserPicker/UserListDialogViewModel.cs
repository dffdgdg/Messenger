using Core.Dialog.Shared;

namespace Core.Dialog.UserPicker;

public partial class UserListDialogViewModel : DialogBaseViewModel
{
    private readonly List<UserListItemViewModel> _allItems;
    private readonly Func<IEnumerable<UserListItemViewModel>, IEnumerable<UserListItemViewModel>> _reViewSelector;
    private readonly Action<List<int>> _applySelection;
    public override bool IsFullscreenInCompactMode => true;
    [ObservableProperty] public partial ObservableCollection<UserListItemViewModel> Items { get; set; }
    [ObservableProperty] public partial ObservableCollection<UserListItemViewModel> FilteredItems { get; set; }
    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty] public partial bool AllowEdit { get; set; }

    [ObservableProperty] public partial bool IsEditMode { get; set; }

    [ObservableProperty] public partial string EmptyMessage { get; set; }

    [ObservableProperty] public partial string EditButtonText { get; set; }

    public bool ShowEditButton => AllowEdit && !IsEditMode;
    public bool ShowSaveButton => IsEditMode;
    public int SelectedCount => Items.Count(x => x.IsSelected);

    public UserListDialogViewModel(string title, IEnumerable<UserListItemViewModel> allItems, bool allowEdit,
        Func<IEnumerable<UserListItemViewModel>, IEnumerable<UserListItemViewModel>> reViewSelector, Action<List<int>> applySelection,
        string editButtonText, string emptyMessage)
    {
        Items = [];
        FilteredItems = [];

        Title = title;
        _allItems = [.. allItems.Select(x => x.Clone())];
        _reViewSelector = reViewSelector;
        _applySelection = applySelection;
        AllowEdit = allowEdit;
        EditButtonText = editButtonText;
        EmptyMessage = emptyMessage;
        CanCloseOnBackgroundClick = true;

        LoadReViewItems();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnIsEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditButton));
        OnPropertyChanged(nameof(ShowSaveButton));
    }

    private void ReplaceItems(IEnumerable<UserListItemViewModel> items)
    {
        Items = new ObservableCollection<UserListItemViewModel>(items);
        foreach (var item in Items)
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));

        OnPropertyChanged(nameof(SelectedCount));
        ApplyFilter();
    }

    private void LoadReViewItems()
    {
        IsEditMode = false;
        ReplaceItems(_reViewSelector(_allItems).Select(x => x.Clone()));
    }

    private void LoadEditItems()
    {
        IsEditMode = true;
        ReplaceItems(_allItems.Select(x => x.Clone()));
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredItems = new ObservableCollection<UserListItemViewModel>(Items);
            return;
        }

        var filtered = Items.Where(u => u.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || u.Username.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        FilteredItems = new ObservableCollection<UserListItemViewModel>(filtered);
    }

    [RelayCommand]
    private void BeginEdit() => LoadEditItems();

    [RelayCommand]
    private void Save()
    {
        if (!IsEditMode)
            return;

        _applySelection([.. Items.Where(x => x.IsSelected).Select(x => x.Id)]);
        RequestClose();
    }

    protected override Task Cancel()
    {
        if (IsEditMode)
        {
            LoadReViewItems();
            SearchQuery = string.Empty;
            return Task.CompletedTask;
        }

        return base.Cancel();
    }
}