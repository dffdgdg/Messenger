using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Admin;
using MessengerShared.Dto.Department;
using MessengerShared.Dto.User;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class AdminViewModel : BaseViewModel, IRefreshable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTab))]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public UsersTabViewModel UsersTab { get; }
    public DepartmentsTabViewModel DepartmentsTab { get; }

    public BaseViewModel CurrentTab => SelectedTabIndex == 0 ? UsersTab : DepartmentsTab;

    public IEnumerable<DepartmentGroup> FilteredGroupedUsers => UsersTab.FilteredGroups;

    public IEnumerable<HierarchicalDepartmentViewModel> FilteredHierarchicalDepartments => DepartmentsTab.FilteredDepartments;

    public AdminViewModel(UsersTabViewModel usersTab, DepartmentsTabViewModel departmentsTab)
    {
        UsersTab = usersTab;
        DepartmentsTab = departmentsTab;

        UsersTab.PropertyChanged += (_, e) =>
        {
            PropagateMessages(e.PropertyName, UsersTab);

            if (e.PropertyName == nameof(UsersTab.FilteredGroups) ||
                e.PropertyName == nameof(UsersTab.GroupedUsers))
            {
                OnPropertyChanged(nameof(FilteredGroupedUsers));
            }
        };

        DepartmentsTab.PropertyChanged += (_, e) =>
        {
            PropagateMessages(e.PropertyName, DepartmentsTab);

            if (e.PropertyName == nameof(DepartmentsTab.FilteredDepartments) ||
                e.PropertyName == nameof(DepartmentsTab.HierarchicalDepartments))
            {
                OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
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
    }

    partial void OnSearchQueryChanged(string value)
    {
        UsersTab.SearchQuery = value;
        DepartmentsTab.SearchQuery = value;

        OnPropertyChanged(nameof(FilteredGroupedUsers));
        OnPropertyChanged(nameof(FilteredHierarchicalDepartments));
    }

    partial void OnSelectedTabIndexChanged(int value) => ClearMessages();

    [RelayCommand]
    private async Task OpenEditUserDialog(UserDto user) => await UsersTab.EditCommand.ExecuteAsync(user);

    [RelayCommand]
    private async Task ToggleBan(UserDto user) => await UsersTab.ToggleBanCommand.ExecuteAsync(user);

    [RelayCommand]
    private async Task OpenEditDepartment(DepartmentDto department)
    {
        var hierarchicalItem = FindDepartmentItem(
            DepartmentsTab.HierarchicalDepartments, department.Id);

        if (hierarchicalItem != null)
        {
            await DepartmentsTab.EditCommand.ExecuteAsync(hierarchicalItem);
        }
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

            SuccessMessage = "Данные обновлены";
        });
    }

    [RelayCommand]
    private async Task Create()
    {
        if (SelectedTabIndex == 0) await UsersTab.CreateCommand.ExecuteAsync(null);
        else await DepartmentsTab.CreateCommand.ExecuteAsync(null);
    }

    private static HierarchicalDepartmentViewModel? FindDepartmentItem(IEnumerable<HierarchicalDepartmentViewModel> items, int departmentId)
    {
        foreach (var item in items)
        {
            if (item.Id == departmentId) return item;

            var found = FindDepartmentItem(item.Children, departmentId);
            if (found != null) return found;
        }
        return null;
    }

    private void PropagateMessages(string? propertyName, BaseViewModel source)
    {
        if (propertyName == nameof(ErrorMessage) && !string.IsNullOrEmpty(source.ErrorMessage)) ErrorMessage = source.ErrorMessage;
        else if (propertyName == nameof(SuccessMessage) && !string.IsNullOrEmpty(source.SuccessMessage)) SuccessMessage = source.SuccessMessage;
    }
}