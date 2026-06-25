using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Abstractions;
using Core.ViewModels.Dialog;
using Shared.Contracts.Department;
using Shared.Contracts.User;

namespace Core.ViewModels;

public partial class DepartmentsTabViewModel(IApiClientService apiClient, IDialogService dialogService, ISessionStore session) : BaseViewModel
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private readonly ISessionStore _session = session ?? throw new ArgumentNullException(nameof(session));
    private const int AdminDepartmentId = 1;

    [ObservableProperty] public partial ObservableCollection<DepartmentDto> Departments { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<UserDto> Users { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<HierarchicalDepartmentViewModel> HierarchicalDepartments { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDepartments))]
    public partial string SearchQuery { get; set; } = string.Empty;
    public IEnumerable<HierarchicalDepartmentViewModel> FilteredDepartments => ApplyFilter();

    public async Task LoadAsync() => await SafeExecuteAsync(async () =>
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
        var departmentDialog = new DepartmentHeadDialogViewModel([.. Departments], Users, _dialogService)
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
        if (!item.CanEdit)
        {
            ErrorMessage = "Недостаточно прав для редактирования этого отдела";
            return;
        }

        var oldHeadId = item.Department.Head;

        await _dialogService.ShowAsync(new DepartmentHeadDialogViewModel([.. Departments.Where(d => d.Id != item.Id)], Users, _dialogService, item.Department, item.HasChildren)
        {
            SaveAction = async dialogVm =>
            {
                if (!_session.IsHead && dialogVm.HeadId != oldHeadId)
                {
                    throw new InvalidOperationException("Только глава отдела администраторов может назначать руководителей отделов");
                }

                var dto = new DepartmentDto
                {
                    Id = item.Id,
                    Name = dialogVm.Name,
                    ParentDepartmentId = dialogVm.ParentDepartmentId,
                    Head = dialogVm.HeadId,
                };

                var result = await _apiClient.PutAsync<DepartmentDto>(ApiEndpoints.Departments.ById(item.Id), dto);

                if (result.Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Отдел обновлён";
                }
                else
                {
                    throw new InvalidOperationException(result.Error ?? "Ошибка обновления отдела");
                }
            },

            DeleteAction = async dialogVm =>
            {
                if (!dialogVm.EditId.HasValue)
                    throw new InvalidOperationException("Идентификатор отдела не задан");

                var result = await _apiClient.DeleteAsync(ApiEndpoints.Departments.ById(dialogVm.EditId.Value));

                if (result.Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Отдел успешно удалён";
                }
                else
                {
                    throw new InvalidOperationException(result.Error ?? "Ошибка удаления отдела");
                }
            },
        });
    }

    [RelayCommand]
    private async Task Delete(HierarchicalDepartmentViewModel item)
    {
        if (!item.CanEdit)
        {
            ErrorMessage = "Недостаточно прав для удаления этого отдела";
            return;
        }

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

        var departmentName = item.Name ?? item.Department.Name;
        var confirmDialog = new ConfirmDialogViewModel("Удаление отдела",
            $"Вы уверены, что хотите удалить отдел «{departmentName}»?\n\nЭто действие нельзя отменить.", "Удалить", "Отмена")
        {
            ConfirmationPrompt = "Для удаления введите название отдела точно как указано:",
            ConfirmationTargetText = departmentName
        };

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
        var vm = new HierarchicalDepartmentViewModel(dept, level)
        {
            CanEdit = _session.IsHead || dept.Id != AdminDepartmentId
        };

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
            IsExpanded = true,
            CanEdit = item.CanEdit
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