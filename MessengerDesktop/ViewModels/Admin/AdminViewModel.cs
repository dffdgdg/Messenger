using MessengerDesktop.ViewModels.Admin;
using MessengerShared.Dto.Department;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class AdminViewModel : BaseViewModel, IRefreshable
{
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

    // Есть ли вообще хоть один пользователь в отфильтрованных группах
    public bool HasUsers => FilteredGroupedUsers?.Any(g => g.Users.Count > 0) == true;

    // Есть ли хоть один отдел после фильтрации
    public bool HasDepartments => FilteredHierarchicalDepartments?.Any() == true;

    public AdminViewModel(
        UsersTabViewModel usersTab,
        DepartmentsTabViewModel departmentsTab)
    {
        UsersTab = usersTab;
        DepartmentsTab = departmentsTab;

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

    // Сброс поиска — используется кнопкой в пустом состоянии
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

            SuccessMessage = "Данные обновлены";
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
            ErrorMessage = source.ErrorMessage;

        else if (propertyName == nameof(SuccessMessage) && !string.IsNullOrEmpty(source.SuccessMessage))
            SuccessMessage = source.SuccessMessage;
    }
}