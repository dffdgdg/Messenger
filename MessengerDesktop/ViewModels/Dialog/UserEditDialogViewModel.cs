using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.ViewModels
{
    public partial class UserEditDialogViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string displayName = string.Empty;

        [ObservableProperty]
        private DepartmentDTO? selectedDepartment;

        [ObservableProperty]
        private List<DepartmentDTO> availableDepartments = [];

        [ObservableProperty]
        private bool isNewUser;

        public Action<UserDTO>? SaveAction { get; set; }
        public Action? CancelAction { get; set; }

        public UserEditDialogViewModel(UserDTO? user, List<DepartmentDTO> departments)
        {
            IsNewUser = user == null;
            availableDepartments = departments;

            if (user != null)
            {
                Username = user.Username;
                DisplayName = user.DisplayName ?? string.Empty;
                SelectedDepartment = departments.Find(d => d.Id == user.DepartmentId);
            }
        }

        [RelayCommand]
        private void Save()
        {
            var user = new UserDTO
            {
                Username = Username,
                DisplayName = DisplayName,
                DepartmentId = SelectedDepartment?.Id,
                Department = SelectedDepartment?.Name
            };

            SaveAction?.Invoke(user);
        }

        [RelayCommand]
        private void Cancel() => CancelAction?.Invoke();
    }
}
