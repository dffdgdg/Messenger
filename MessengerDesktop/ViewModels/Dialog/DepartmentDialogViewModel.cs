using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO.Department;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

    public partial class DepartmentDialogViewModel : DialogBaseViewModel
    {
        private static readonly DepartmentDTO NoParentPlaceholder = new()
        {
            Id = -1,
            Name = "(Нет родительского отдела)"
        };

        private readonly IReadOnlyList<DepartmentDTO> _allDepartments;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DepartmentDTO> _availableParents = [];

        [ObservableProperty]
        private DepartmentDTO _selectedParent = NoParentPlaceholder;

        public int? EditId { get; }
        public bool IsNewDepartment => EditId == null;
        public int? ParentDepartmentId => SelectedParent.Id > 0 ? SelectedParent.Id : null;
        public bool CanSave => !string.IsNullOrWhiteSpace(Name);

        public Func<DepartmentDialogViewModel, Task>? SaveAction { get; set; }

        public DepartmentDialogViewModel(List<DepartmentDTO> departments, DepartmentDTO? department = null)
        {
            _allDepartments = departments ?? throw new ArgumentNullException(nameof(departments));
            EditId = department?.Id;

            Title = department == null ? "Создать отдел" : $"Редактировать: {department.Name}";
            CanCloseOnBackgroundClick = true;

            if (department != null)
                Name = department.Name;

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
    }