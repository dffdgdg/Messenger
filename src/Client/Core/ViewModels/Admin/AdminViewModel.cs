using CommunityToolkit.Mvvm.ComponentModel;
using Core.ViewModels.Admin;
using Core.ViewModels.Shared;
using Shared.Dto.Department;

namespace Core.ViewModels;

public partial class AdminViewModel : BaseViewModel, IRefreshable
{
    private readonly INotificationService _notificationService;
    private string? _lastShownError;
    private string? _lastShownSuccess;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTab))]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUsers))]
    [NotifyPropertyChangedFor(nameof(HasDepartments))]
    public partial string SearchQuery { get; set; } = string.Empty;

    public UsersTabViewModel UsersTab { get; }
    public DepartmentsTabViewModel DepartmentsTab { get; }

    public BaseViewModel CurrentTab => SelectedTabIndex == 0 ? UsersTab : DepartmentsTab;

    public IEnumerable<DepartmentGroup> FilteredGroupedUsers => UsersTab.FilteredGroups;

    public IEnumerable<HierarchicalDepartmentViewModel> FilteredHierarchicalDepartments =>
        DepartmentsTab.FilteredDepartments;

    public bool HasUsers => FilteredGroupedUsers?.Any(g => g.Users.Count > 0) == true;
    public bool HasDepartments => FilteredHierarchicalDepartments?.Any() == true;

    public AdminViewModel(
        UsersTabViewModel usersTab,
        DepartmentsTabViewModel departmentsTab,
        INotificationService notificationService,
        IGlobalHubConnection globalHub)
    {
        UsersTab = usersTab;
        DepartmentsTab = departmentsTab;
        _notificationService = notificationService;

        globalHub.UserPermissionsChanged += OnUserPermissionsChanged;

        UsersTab.PropertyChanged += (_, e) =>
        {
            PropagateMessages(e.PropertyName, UsersTab);

            if (e.PropertyName is nameof(UsersTab.FilteredGroups) or nameof(UsersTab.GroupedUsers))
            {
                OnPropertyChanged(nameof(FilteredGroupedUsers));
                OnPropertyChanged(nameof(HasUsers));
            }
        };

        DepartmentsTab.PropertyChanged += (_, e) =>
        {
            PropagateMessages(e.PropertyName, DepartmentsTab);

            if (e.PropertyName is nameof(DepartmentsTab.FilteredDepartments) or nameof(DepartmentsTab.HierarchicalDepartments))
            {
                OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
                OnPropertyChanged(nameof(HasDepartments));
            }
        };

        _ = InitializeAsync();
    }

    private void OnUserPermissionsChanged(UserPermissionsChangedDto dto)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (RefreshCommand.CanExecute(null))
                RefreshCommand.Execute(null);
        });
    }

    private async Task InitializeAsync()
    {
        await Task.WhenAll(UsersTab.LoadAsync(), DepartmentsTab.LoadAsync());

        UsersTab.SetDepartments(DepartmentsTab.Departments);
        DepartmentsTab.SetUsers(UsersTab.Users);

        OnPropertyChanged(nameof(FilteredGroupedUsers));
        OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(HasDepartments));
    }

    partial void OnSearchQueryChanged(string value)
    {
        UsersTab.SearchQuery = value;
        DepartmentsTab.SearchQuery = value;

        OnPropertyChanged(nameof(FilteredGroupedUsers));
        OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(HasDepartments));
    }

    partial void OnSelectedTabIndexChanged(int value) => ClearMessages();

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    private async Task OpenEditUserDialog(UserDto user) =>
        await UsersTab.EditCommand.ExecuteAsync(user);

    [RelayCommand]
    private async Task ToggleBan(UserDto user) =>
        await UsersTab.ToggleBanCommand.ExecuteAsync(user);

    [RelayCommand]
    private async Task OpenEditDepartment(DepartmentDto department)
    {
        var item = FindDepartmentItem(
            DepartmentsTab.HierarchicalDepartments, department.Id);

        if (item is not null)
            await DepartmentsTab.EditCommand.ExecuteAsync(item);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await SafeExecuteAsync(async () =>
        {
            await Task.WhenAll(UsersTab.LoadAsync(), DepartmentsTab.LoadAsync());

            UsersTab.SetDepartments(DepartmentsTab.Departments);
            DepartmentsTab.SetUsers(UsersTab.Users);

            OnPropertyChanged(nameof(FilteredGroupedUsers));
            OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
            OnPropertyChanged(nameof(HasUsers));
            OnPropertyChanged(nameof(HasDepartments));

            await _notificationService.ShowSuccessAsync("Данные обновлены");
        });
    }

    [RelayCommand]
    private async Task Create()
    {
        if (SelectedTabIndex == 0)
            await UsersTab.CreateCommand.ExecuteAsync(null);
        else
            await DepartmentsTab.CreateCommand.ExecuteAsync(null);
    }

    private static HierarchicalDepartmentViewModel? FindDepartmentItem(
        IEnumerable<HierarchicalDepartmentViewModel> items, int departmentId)
    {
        foreach (var item in items)
        {
            if (item.Id == departmentId)
                return item;

            var found = FindDepartmentItem(item.Children, departmentId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void PropagateMessages(string? propertyName, BaseViewModel source)
    {
        if (propertyName == nameof(ErrorMessage) && !string.IsNullOrEmpty(source.ErrorMessage))
        {
            if (!string.Equals(_lastShownError, source.ErrorMessage, StringComparison.Ordinal))
            {
                _lastShownError = source.ErrorMessage;
                _ = _notificationService.ShowErrorAsync(source.ErrorMessage);
            }
            ErrorMessage = null;
        }
        else if (propertyName == nameof(SuccessMessage) && !string.IsNullOrEmpty(source.SuccessMessage))
        {
            if (!string.Equals(_lastShownSuccess, source.SuccessMessage, StringComparison.Ordinal))
            {
                _lastShownSuccess = source.SuccessMessage;
                _ = _notificationService.ShowSuccessAsync(source.SuccessMessage);
            }
            SuccessMessage = null;
        }
    }
}