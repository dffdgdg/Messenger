using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerShared.DTO.User;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ProfileViewModel : BaseViewModel
    {
        private readonly IApiClientService _apiClient;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AvatarUrl))]
        [NotifyPropertyChangedFor(nameof(FullName))]
        private UserDTO? _user;

        [ObservableProperty] private int _userId;

        // Режимы редактирования
        [ObservableProperty] private bool _isEditingProfile;
        [ObservableProperty] private bool _isEditingUsername;
        [ObservableProperty] private bool _isEditingPassword;

        // Поля для редактирования профиля (ФИО)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TempFullName))]
        private string _tempSurname = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TempFullName))]
        private string _tempName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TempFullName))]
        private string _tempMidname = string.Empty;

        // Поля для редактирования Username
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSaveUsername))]
        [NotifyPropertyChangedFor(nameof(UsernameValidationMessage))]
        [NotifyPropertyChangedFor(nameof(IsUsernameValid))]
        private string _tempUsername = string.Empty;

        // Поля для смены пароля
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSavePassword))]
        private string _currentPassword = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSavePassword))]
        [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
        [NotifyPropertyChangedFor(nameof(IsNewPasswordValid))]
        [NotifyPropertyChangedFor(nameof(NewPasswordValidationMessage))]
        private string _newPassword = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSavePassword))]
        [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
        [NotifyPropertyChangedFor(nameof(ShowPasswordMatchIndicator))]
        private string _confirmPassword = string.Empty;

        [ObservableProperty] private Bitmap? _avatarBitmap;

        public string? AvatarUrl => GetAbsoluteUrl(User?.Avatar);

        public string FullName => FormatFullName(User?.Surname, User?.Name, User?.Midname)
                                  ?? User?.Username ?? "Пользователь";

        public string TempFullName => FormatFullName(TempSurname, TempName, TempMidname) ?? "—";

        // Валидация Username
        public bool IsUsernameValid => string.IsNullOrEmpty(TempUsername) ||
            System.Text.RegularExpressions.Regex.IsMatch(TempUsername.Trim(), @"^[a-zA-Z0-9_]{3,30}$");

        public bool CanSaveUsername => !string.IsNullOrWhiteSpace(TempUsername) && TempUsername.Trim().Length >= 3 && IsUsernameValid;

        public string? UsernameValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(TempUsername)) return null;
                if (TempUsername.Trim().Length < 3) return "Минимум 3 символа";
                if (!IsUsernameValid) return "Только латинские буквы, цифры и _";
                return null;
            }
        }

        // Валидация пароля
        public bool IsNewPasswordValid => string.IsNullOrEmpty(NewPassword) || NewPassword.Length >= 6;

        public string? NewPasswordValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(NewPassword)) return null;
                if (NewPassword.Length < 6) return $"Ещё {6 - NewPassword.Length} символов";
                return null;
            }
        }

        public bool PasswordsMatch => NewPassword == ConfirmPassword;

        public bool ShowPasswordMatchIndicator => !string.IsNullOrEmpty(ConfirmPassword);

        public bool CanSavePassword => !string.IsNullOrWhiteSpace(CurrentPassword)
                                       && !string.IsNullOrWhiteSpace(NewPassword)
                                       && NewPassword.Length >= 6
                                       && PasswordsMatch;

        public ProfileViewModel(IApiClientService apiClient, IAuthManager authManager)
        {
            _apiClient = apiClient;
            UserId = authManager.Session.UserId ?? throw new Exception("Not auth");
            _ = LoadUser();
        }

        private static string? FormatFullName(string? surname, string? name, string? midname)
        {
            var parts = new[] { surname, name, midname };
            var filtered = parts.Where(s => !string.IsNullOrWhiteSpace(s));
            return filtered.Any() ? string.Join(" ", filtered) : null;
        }

        private async Task LoadUser() => await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.GetAsync<UserDTO>(ApiEndpoints.User.ById(UserId));
            if (result.Success)
            {
                User = result.Data;
                await LoadAvatarAsync();
            }
        });

        private async Task LoadAvatarAsync()
        {
            if (string.IsNullOrEmpty(AvatarUrl)) { AvatarBitmap = null; return; }
            try
            {
                var stream = await _apiClient.GetStreamAsync(AvatarUrl);
                if (stream != null) AvatarBitmap = new Bitmap(stream);
            }
            catch { AvatarBitmap = null; }
        }

        #region Редактирование профиля (ФИО)

        [RelayCommand]
        private void StartEditProfile()
        {
            CancelAllEditing();
            TempSurname = User?.Surname ?? "";
            TempName = User?.Name ?? "";
            TempMidname = User?.Midname ?? "";
            IsEditingProfile = true;
        }

        [RelayCommand]
        private void CancelEditProfile() => IsEditingProfile = false;

        [RelayCommand]
        private async Task SaveProfile()
        {
            if (User == null) return;

            if (string.IsNullOrWhiteSpace(TempSurname) && string.IsNullOrWhiteSpace(TempName))
            {
                ErrorMessage = "Укажите хотя бы имя или фамилию";
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                var update = new UserDTO
                {
                    Id = User.Id,
                    Username = User.Username,
                    Surname = TempSurname.Trim(),
                    Name = TempName.Trim(),
                    Midname = TempMidname.Trim(),
                    Avatar = User.Avatar,
                    Department = User.Department
                };

                var result = await _apiClient.PutAsync<UserDTO>(ApiEndpoints.User.ById(User.Id), update);

                if (result.Success)
                {
                    User.Surname = TempSurname.Trim();
                    User.Name = TempName.Trim();
                    User.Midname = TempMidname.Trim();
                    OnPropertyChanged(nameof(FullName));
                    IsEditingProfile = false;
                    SuccessMessage = "Профиль обновлён";
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            });
        }

        #endregion

        #region Редактирование Username

        [RelayCommand]
        private void StartEditUsername()
        {
            CancelAllEditing();
            TempUsername = User?.Username ?? "";
            IsEditingUsername = true;
        }

        [RelayCommand]
        private void CancelEditUsername()
        {
            IsEditingUsername = false;
            TempUsername = "";
        }

        [RelayCommand]
        private async Task SaveUsername()
        {
            if (User == null || !CanSaveUsername) return;

            var newUsername = TempUsername.Trim().ToLower();

            if (newUsername == User.Username?.ToLower())
            {
                IsEditingUsername = false;
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                var dto = new ChangeUsernameDTO { NewUsername = newUsername };
                var result = await _apiClient.PutAsync<object>(ApiEndpoints.User.Username(User.Id), dto);

                if (result.Success)
                {
                    User.Username = newUsername;
                    OnPropertyChanged(nameof(User));
                    IsEditingUsername = false;
                    SuccessMessage = "Username успешно изменён";
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            });
        }

        #endregion

        #region Смена пароля

        [RelayCommand]
        private void StartEditPassword()
        {
            CancelAllEditing();
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
            IsEditingPassword = true;
        }

        [RelayCommand]
        private void CancelEditPassword()
        {
            IsEditingPassword = false;
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
        }

        [RelayCommand]
        private async Task SavePassword()
        {
            if (User == null || !CanSavePassword) return;

            await SafeExecuteAsync(async () =>
            {
                var dto = new ChangePasswordDTO
                {
                    CurrentPassword = CurrentPassword,
                    NewPassword = NewPassword
                };

                var result = await _apiClient.PutAsync<object>(ApiEndpoints.User.Password(User.Id), dto);

                if (result.Success)
                {
                    IsEditingPassword = false;
                    CurrentPassword = "";
                    NewPassword = "";
                    ConfirmPassword = "";
                    SuccessMessage = "Пароль успешно изменён";
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            });
        }

        #endregion

        #region Аватар

        [RelayCommand]
        private async Task UploadAvatar()
        {
            var storage = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;

            if (storage == null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });

            if (files.Count == 0) return;

            await SafeExecuteAsync(async () =>
            {
                await using var stream = await files[0].OpenReadAsync();
                var result = await _apiClient.UploadFileAsync<UserDTO>(
                    ApiEndpoints.User.Avatar(User!.Id), stream, files[0].Name, "image/png");

                if (result.Success)
                {
                    User.Avatar = result.Data!.Avatar;
                    OnPropertyChanged(nameof(AvatarUrl));
                    await LoadAvatarAsync();
                    SuccessMessage = "Аватар обновлён";
                }
            });
        }

        #endregion

        #region Вспомогательные методы

        private void CancelAllEditing()
        {
            IsEditingProfile = false;
            IsEditingUsername = false;
            IsEditingPassword = false;
            ErrorMessage = null;
        }

        [RelayCommand]
        private static async Task Logout()
        {
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.DataContext is MainWindowViewModel main)
            {
                await main.Logout();
            }
        }
        [RelayCommand]
        protected void ClearError() => ErrorMessage = null;

        [RelayCommand]
        protected void ClearSuccess() => SuccessMessage = null;
        #endregion
    }
}