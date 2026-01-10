using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO.Department;
using MessengerShared.DTO.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class DepartmentHeadDialogViewModel : DialogBaseViewModel
{
    private static readonly DepartmentDTO NoParentPlaceholder = new()
    {
        Id = -1,
        Name = "(Нет родительского отдела)"
    };

    private readonly IReadOnlyList<DepartmentDTO> _allDepartments;
    private readonly ObservableCollection<UserDTO> _allUsers;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DepartmentDTO> _availableParents = [];

    [ObservableProperty]
    private DepartmentDTO _selectedParent = NoParentPlaceholder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHead))]
    [NotifyPropertyChangedFor(nameof(HeadDisplayText))]
    private UserDTO? _selectedHead;

    public int? EditId { get; }
    public bool IsNewDepartment => EditId == null;
    public int? ParentDepartmentId => SelectedParent.Id > 0 ? SelectedParent.Id : null;
    public int? HeadId => SelectedHead?.Id;
    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public bool HasHead => SelectedHead != null;
    public string HeadDisplayText => SelectedHead?.DisplayName ?? "Не назначен";

    // Свойства для удаления
    public int UserCount { get; }
    public bool HasChildren { get; }
    public bool CanDelete => !IsNewDepartment && !HasChildren && UserCount == 0;

    public string DeleteTooltip
    {
        get
        {
            if (IsNewDepartment) return string.Empty;
            if (HasChildren) return "Сначала удалите дочерние отделы";
            if (UserCount > 0) return $"Сначала переведите сотрудников ({UserCount} чел.)";
            return "Удалить отдел";
        }
    }

    public Func<DepartmentHeadDialogViewModel, Task>? SaveAction { get; set; }
    public Func<DepartmentHeadDialogViewModel, Task>? DeleteAction { get; set; }

    public DepartmentHeadDialogViewModel(List<DepartmentDTO> departments,ObservableCollection<UserDTO> users,
        IDialogService dialogService,DepartmentDTO? department = null, bool hasChildren = false)
    {
        _allDepartments = departments ?? throw new ArgumentNullException(nameof(departments));
        _allUsers = users ?? throw new ArgumentNullException(nameof(users));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        EditId = department?.Id;
        Title = department == null ? "Создать отдел" : $"Редактировать: {department.Name}";
        CanCloseOnBackgroundClick = true;

        // Информация для удаления
        HasChildren = hasChildren;
        UserCount = department?.UserCount ?? 0;

        if (department != null)
        {
            Name = department.Name;

            if (department.Head.HasValue)
            {
                SelectedHead = _allUsers.FirstOrDefault(u => u.Id == department.Head.Value);
            }
        }

        BuildAvailableParents(department);
    }

    private void BuildAvailableParents(DepartmentDTO? current)
    {
        var excludedIds = current != null ? GetDescendantIds(current.Id).Append(current.Id).ToHashSet() : [];

        var parents = _allDepartments.Where(d => d.Id > 0 && !excludedIds.Contains(d.Id)).OrderBy(d => d.Name).Prepend(NoParentPlaceholder);

        AvailableParents = new ObservableCollection<DepartmentDTO>(parents);

        SelectedParent = current?.ParentDepartmentId is int parentId
            ? AvailableParents.FirstOrDefault(d => d.Id == parentId) ?? NoParentPlaceholder
            : NoParentPlaceholder;
    }

    private HashSet<int> GetDescendantIds(int rootId)
    {
        var result = new HashSet<int>();
        var queue = new Queue<int>([rootId]);

        while (queue.TryDequeue(out var currentId))
        {
            foreach (var child in _allDepartments.Where(d => d.ParentDepartmentId == currentId))
            {
                if (result.Add(child.Id))
                    queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    partial void OnNameChanged(string value)
    {
        if (CanSave) ErrorMessage = null;
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SelectHead()
    {
        var availableUsers = new ObservableCollection<UserDTO>(_allUsers.Where(u => !u.IsBanned));

        var selectDialog = new SelectUserDialogViewModel(availableUsers, "Выбрать руководителя");

        await _dialogService.ShowAsync(selectDialog);
        var selectedUser = await selectDialog.Result;

        if (selectedUser != null)
        {
            SelectedHead = selectedUser;
        }
    }

    [RelayCommand]
    private void ClearHead() => SelectedHead = null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Введите название отдела";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            if (SaveAction != null)
                await SaveAction(this);

            SuccessMessage = IsNewDepartment ? "Отдел создан" : "Отдел обновлён";
            RequestClose();
        });
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (!CanDelete)
        {
            ErrorMessage = DeleteTooltip;
            return;
        }

        var confirmDialog = new ConfirmDialogViewModel("Удаление отдела",
            $"Вы уверены, что хотите удалить отдел «{Name}»?\n\nЭто действие нельзя отменить.","Удалить","Отмена");

        await _dialogService.ShowAsync(confirmDialog);
        var confirmed = await confirmDialog.Result;

        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            if (DeleteAction != null)
                await DeleteAction(this);

            RequestClose();
        });
    }
}