using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO;

namespace MessengerDesktop.ViewModels
{
    public partial class HierarchicalDepartmentViewModel : BaseViewModel
    {
        [ObservableProperty]
        private DepartmentDTO department;

        [ObservableProperty]
        private ObservableCollection<HierarchicalDepartmentViewModel> children = [];

        [ObservableProperty]
        private int level;

        [ObservableProperty]
        private bool isExpanded = true;

        public int Id => Department.Id;
        public string Name => Department.Name;
        public int? ParentDepartmentId => Department.ParentDepartmentId;
        public bool HasChildren => Children.Count > 0;

        public HierarchicalDepartmentViewModel(DepartmentDTO department, int level = 0)
        {
            this.department = department;
            Level = level;
        }

        [RelayCommand]
        private void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }
    }
}