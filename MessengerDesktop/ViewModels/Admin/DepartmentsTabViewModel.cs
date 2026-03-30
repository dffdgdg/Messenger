using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.Dto.Department;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class DepartmentsTabViewModel(IApiClientService apiClient, IDialogService dialogService) : BaseViewModel
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

    [ObservableProperty] public partial ObservableCollection<DepartmentDto> Departments { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<UserDto> Users { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<HierarchicalDepartmentViewModel> HierarchicalDepartments { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDepartments))]
    public partial string SearchQuery { get; set; } = string.Empty;
    public IEnumerable<HierarchicalDepartmentViewModel> FilteredDepartments => ApplyFilter();

    public async Task LoadAsync() =>
        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.GetAsync<List<DepartmentDto>>(ApiEndpoints.Departments.GetAll);

            if (result is { Success: true, Data: not null })
            {
                Departments = new ObservableCollection<DepartmentDto>(result.Data);
                BuildHierarchy();
            }
            else
            {
                ErrorMessage = $"Ошибка загрузки отделов: {result.Error}";
            }
        });

    public void SetUsers(IEnumerable<UserDto> users) => Users = new ObservableCollection<UserDto>(users);

    [RelayCommand]
    private async Task Create()
    {
        var departmentDialog = new DepartmentHeadDialogViewModel([.. Departments],Users,_dialogService)
        {
            SaveAction = async dialogVm =>
            {
                var result = await _apiClient.PostAsync<DepartmentDto>(ApiEndpoints.Departments.Create, new DepartmentDto
                {
                    Name = dialogVm.Name,
                    ParentDepartmentId = dialogVm.ParentDepartmentId,
                    Head = dialogVm.HeadId
                });

                if (result.Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Отдел создан";
                }
                else
                {
                    throw new InvalidOperationException(result.Error ?? "Ошибка создания отдела");
                }
            }
        };

        await _dialogService.ShowAsync(departmentDialog);
    }

    [RelayCommand]
    private async Task Edit(HierarchicalDepartmentViewModel item)
    {
        await _dialogService.ShowAsync(new DepartmentHeadDialogViewModel([.. Departments.Where(d => d.Id != item.Id)],
            Users, _dialogService, item.Department, item.HasChildren)
        {
            SaveAction = async dialogVm =>
            {
                var dto = new DepartmentDto
                {
                    Id = item.Id,
                    Name = dialogVm.Name,
                    ParentDepartmentId = dialogVm.ParentDepartmentId,
                    Head = dialogVm.HeadId
                };

                if ((await _apiClient.PutAsync<DepartmentDto>(ApiEndpoints.Departments.ById(item.Id), dto)).Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Отдел обновлён";
                }
                else
                {
                    throw new InvalidOperationException((await _apiClient.PutAsync<DepartmentDto>(ApiEndpoints.Departments.ById(item.Id), dto)).Error ?? "Ошибка обновления отдела");
                }
            },
            DeleteAction = async dialogVm =>
            {
                if (!dialogVm.EditId.HasValue)
                    throw new InvalidOperationException("Идентификатор отдела не задан.");

                if ((await _apiClient.DeleteAsync(ApiEndpoints.Departments.ById(dialogVm.EditId.Value))).Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Отдел успешно удалён";
                }
                else
                {
                    throw new InvalidOperationException((await _apiClient.DeleteAsync(ApiEndpoints.Departments.ById(dialogVm.EditId.Value))).Error ?? "Ошибка удаления отдела");
                }
            }
        });
    }

    [RelayCommand]
    private async Task Delete(HierarchicalDepartmentViewModel item)
    {
        if (item.HasChildren)
        {
            ErrorMessage = "Невозможно удалить отдел с подразделениями. Сначала удалите дочерние отделы.";
            return;
        }

        if (item.Department.UserCount > 0)
        {
            ErrorMessage = $"Невозможно удалить отдел с сотрудниками ({item.Department.UserCount} чел.).";
            return;
        }

        var confirmDialog = new ConfirmDialogViewModel("Удаление отдела",$"Вы уверены, что хотите удалить отдел «{item.Name}»?\n\nЭто действие нельзя отменить.","Удалить","Отмена");

        await _dialogService.ShowAsync(confirmDialog);
        var confirmed = await confirmDialog.Result;

        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.DeleteAsync(ApiEndpoints.Departments.ById(item.Id));

            if (result.Success)
            {
                await LoadAsync();
                SuccessMessage = "Отдел успешно удалён";
            }
            else
            {
                ErrorMessage = $"Ошибка удаления: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private void ExpandAll() => SetExpandedState(HierarchicalDepartments, true);

    [RelayCommand]
    private void CollapseAll() => SetExpandedState(HierarchicalDepartments, false);

    private void BuildHierarchy()
    {
        var roots = Departments.Where(d => !d.ParentDepartmentId.HasValue).Select(d => CreateHierarchicalItem(d, 0)).OrderBy(d => d.Name);

        HierarchicalDepartments = new ObservableCollection<HierarchicalDepartmentViewModel>(roots);
        OnPropertyChanged(nameof(FilteredDepartments));
    }

    private HierarchicalDepartmentViewModel CreateHierarchicalItem(DepartmentDto dept, int level)
    {
        var vm = new HierarchicalDepartmentViewModel(dept, level);

        foreach (var child in Departments.Where(d => d.ParentDepartmentId == dept.Id).Select(d => CreateHierarchicalItem(d, level + 1)).OrderBy(d => d.Name))
        {
            vm.Children.Add(child);
        }

        return vm;
    }

    private IEnumerable<HierarchicalDepartmentViewModel> ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return HierarchicalDepartments;

        var query = SearchQuery.ToLowerInvariant();
        var results = new List<HierarchicalDepartmentViewModel>();

        foreach (var dept in HierarchicalDepartments)
        {
            var filtered = FilterHierarchy(dept, query);
            if (filtered is not null)
                results.Add(filtered);
        }

        return results;
    }

    private static HierarchicalDepartmentViewModel? FilterHierarchy(HierarchicalDepartmentViewModel item, string query)
    {
        var nameMatches = item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        var headMatches = item.HeadName?.Contains(query, StringComparison.OrdinalIgnoreCase) is true;

        var filteredChildren = item.Children.Select(c => FilterHierarchy(c, query)).Where(c => c is not null).ToList();

        if (!nameMatches && !headMatches && filteredChildren.Count == 0)
            return null;

        var clone = new HierarchicalDepartmentViewModel(item.Department, item.Level)
        {
            IsExpanded = true
        };

        foreach (var child in filteredChildren)
            clone.Children.Add(child!);

        return clone;
    }

    private static void SetExpandedState(IEnumerable<HierarchicalDepartmentViewModel> items, bool expanded)
    {
        foreach (var item in items)
        {
            item.IsExpanded = expanded;
            SetExpandedState(item.Children, expanded);
        }
    }
}