using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    /// <summary>
    /// ViewModel диалога редактирования пользователя.
    /// Используется в админ-панели для создания/редактирования пользователей.
    /// </summary>
    public partial class UserEditDialogViewModel : DialogBaseViewModel
    {
        private readonly UserDTO? _originalUser;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private int? _departmentId;

        [ObservableProperty]
        private ObservableCollection<DepartmentDTO> _departments = [];

        /// <summary>Callback для сохранения пользователя</summary>
        public Func<UserDTO, Task>? SaveAction { get; set; }

        public bool IsNewUser => _originalUser == null;

        public UserEditDialogViewModel(UserDTO? user, ObservableCollection<DepartmentDTO> departments)
        {
            _originalUser = user;
            Departments = departments;
            Title = user == null ? "Создать пользователя" : $"Редактировать: {user.DisplayName ?? user.Username}";
            CanCloseOnBackgroundClick = true;

            if (user != null)
            {
                Username = user.Username ?? string.Empty;
                DisplayName = user.DisplayName ?? string.Empty;
                DepartmentId = user.DepartmentId;
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "Введите имя пользователя";
                return;
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                ErrorMessage = "Введите отображаемое имя";
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                var user = new UserDTO
                {
                    Id = _originalUser?.Id ?? 0,
                    Username = Username,
                    DisplayName = DisplayName,
                    DepartmentId = DepartmentId
                };

                if (SaveAction != null)
                {
                    await SaveAction(user);
                    SuccessMessage = IsNewUser ? "Пользователь создан" : "Пользователь обновлен";
                    RequestClose();
                }
            });
        }

        partial void OnDisplayNameChanged(string value)
        {
            if (!string.IsNullOrEmpty(ErrorMessage) && !string.IsNullOrWhiteSpace(value))
            {
                ErrorMessage = null;
            }
        }
    }
}