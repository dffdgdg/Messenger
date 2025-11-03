using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.ViewModels
{
    public partial class DepartmentDialogViewModel : DialogViewModelBase
    {
        public Func<DepartmentDialogViewModel, Task>? SaveAction { get; set; }
        public Action? CancelAction { get; set; }

        public int? EditId { get; set; }

        [ObservableProperty]
        private bool isNewDepartment = true;

        [ObservableProperty]
        private string name = string.Empty;

        private readonly List<DepartmentDTO> _availableDepartments;
        
        [ObservableProperty]
        private ObservableCollection<DepartmentDTO> departments;

        private DepartmentDTO? _selectedParent;
        public DepartmentDTO? SelectedParent
        {
            get => _selectedParent;
            set
            {
                if (SetProperty(ref _selectedParent, value))
                    OnPropertyChanged(nameof(ParentDepartmentId));
            }
        }

        public int? ParentDepartmentId => SelectedParent?.Id <= 0 ? null : SelectedParent?.Id;

        private readonly DepartmentDTO _noParentDepartment;

        public DepartmentDialogViewModel(List<DepartmentDTO> departments, DepartmentDTO? department = null)
        {
            _availableDepartments = departments;
            EditId = department?.Id;
            _noParentDepartment = new DepartmentDTO { Id = -1, Name = "(Нет родительского отдела)", ParentDepartmentId = null };

            if (department != null)
            {
                Name = department.Name;
                IsNewDepartment = false;
            }

            UpdateAvailableParents(department);
            
            SelectedParent = department?.ParentDepartmentId.HasValue == true
                ? Departments.FirstOrDefault(d => d.Id == department.ParentDepartmentId)
                : _noParentDepartment;
        }

        private void UpdateAvailableParents(DepartmentDTO? currentDepartment)
        {
            var availableDepts = new List<DepartmentDTO> { _noParentDepartment };

            var realDepartments = _availableDepartments.Where(d => d.Id > 0);

            if (currentDepartment != null)
            {
                var excludedIds = new HashSet<int> { currentDepartment.Id };
                excludedIds.UnionWith(GetAllChildDepartments(currentDepartment.Id));
                realDepartments = realDepartments.Where(d => !excludedIds.Contains(d.Id));
            }

            availableDepts.AddRange(realDepartments);
            Departments = new ObservableCollection<DepartmentDTO>(availableDepts);
        }

        private HashSet<int> GetAllChildDepartments(int departmentId)
        {
            var children = new HashSet<int>();
            var directChildren = _availableDepartments
                .Where(d => d.ParentDepartmentId == departmentId)
                .Select(d => d.Id);
            
            foreach (var childId in directChildren)
            {
                children.Add(childId);
                var grandChildren = GetAllChildDepartments(childId);
                children.UnionWith(grandChildren);
            }
            
            return children;
        }

        [RelayCommand]
        private async Task Save()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return;
            }

            if (SaveAction != null)
            {
                await SaveAction.Invoke(this);
            }
        }        
    }
}
