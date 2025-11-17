using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ProfileViewModel : BaseViewModel
    {
        private readonly IApiClientService _apiClient;

        [ObservableProperty]
        private UserDTO? _user;

        [ObservableProperty]
        private int _userId;

        [ObservableProperty]
        private bool _isEditing;

        [ObservableProperty]
        private string _tempDisplayName = string.Empty;

        [ObservableProperty]
        private Bitmap? _avatarBitmap;

        private bool _hasUnsavedChanges;

        public string? AvatarUrl
        {
            get
            {
                if (string.IsNullOrEmpty(User?.Avatar))
                    return null;

                var avatar = User.Avatar.Trim();

                if (avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return avatar;

                var baseUrl = App.ApiUrl.TrimEnd('/');
                var avatarPath = avatar.TrimStart('/');
                return $"{baseUrl}/{avatarPath}";
            }
        }

        public ProfileViewModel(int userId, IApiClientService apiClient)
        {
            _apiClient = apiClient;
            UserId = userId;
            _ = LoadUser();
        }

        partial void OnUserChanged(UserDTO? value) => OnPropertyChanged(nameof(AvatarUrl));

        private async Task LoadUser()
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.GetAsync<UserDTO>($"api/user/{UserId}");
                if (result.Success && result.Data != null)
                {
                    User = result.Data;
                    TempDisplayName = result.Data.DisplayName ?? string.Empty;

                    await LoadAvatarAsync();
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки профиля: {result.Error}";
                }
            });
        }
        private async Task LoadAvatarAsync()
        {
            if (string.IsNullOrWhiteSpace(AvatarUrl))
            {
                AvatarBitmap = null;
                return;
            }

            try
            {
                var stream = await _apiClient.GetStreamAsync(AvatarUrl);
                if (stream == null)
                {
                    ErrorMessage = "Не удалось загрузить аватар (stream == null)";
                    return;
                }

                AvatarBitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка загрузки аватара: {ex.Message}";
                AvatarBitmap = null;
            }
        }


        [RelayCommand]
        private void StartEdit()
        {
            if (User == null) return;
            TempDisplayName = User.DisplayName ?? string.Empty;
            IsEditing = true;
            _hasUnsavedChanges = false;
            ClearMessages(); 
        }

        [RelayCommand]
        private void CancelEdit()
        {
            if (!_hasUnsavedChanges || User == null)
            {
                IsEditing = false;
                return;
            }
            TempDisplayName = User.DisplayName ?? string.Empty;
            IsEditing = false;
            _hasUnsavedChanges = false;
            ClearMessages(); 
        }

        [RelayCommand]
        private async Task SaveChanges()
        {
            if (User == null) return;

            await SafeExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(TempDisplayName))
                {
                    ErrorMessage = "Введите отображаемое имя";
                    return;
                }

                var updatedUser = new UserDTO
                {
                    Id = User.Id,
                    Username = User.Username,
                    DisplayName = TempDisplayName.Trim(),
                    Avatar = User.Avatar
                };

                var result = await _apiClient.PutAsync<UserDTO>($"api/user/{User.Id}", updatedUser);
                if (result.Success)
                {
                    User.DisplayName = TempDisplayName.Trim();
                    IsEditing = false;
                    _hasUnsavedChanges = false;
                    SuccessMessage = "Данные обновлены";
                }
                else
                    ErrorMessage = $"Ошибка обновления: {result.Error}";
            });
        }

        [RelayCommand]
        private async Task UploadAvatar()
        {
            if (User == null) return;

            await SafeExecuteAsync(async () =>
            {
                var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (window?.StorageProvider == null)
                {
                    ErrorMessage = "Ошибка доступа к файловой системе";
                    return;
                }

                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Выберите изображение",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Изображения")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"]
                        }
                    ]
                });

                if (files == null || files.Count == 0)
                    return;

                var file = files[0];

                await using var fileStream = await file.OpenReadAsync();

                if (fileStream.Length > 5 * 1024 * 1024)
                {
                    ErrorMessage = "Файл слишком большой. Максимальный размер: 5MB";
                    return;
                }

                var uploadResult = await _apiClient.UploadFileAsync<UserDTO>(
                    $"api/user/{User.Id}/avatar",
                    fileStream,
                    file.Name,
                    GetMimeType(Path.GetExtension(file.Name)));

                if (uploadResult.Success)
                {
                    if (uploadResult.Data != null && !string.IsNullOrEmpty(uploadResult.Data.Avatar))
                        User.Avatar = uploadResult.Data.Avatar;
                    else
                    {
                        var refreshed = await _apiClient.GetAsync<UserDTO>($"api/user/{User.Id}");
                        if (refreshed.Success && refreshed.Data != null) User = refreshed.Data;
                    }

                    OnPropertyChanged(nameof(AvatarUrl));
                    await LoadAvatarAsync();
                    SuccessMessage = "Аватар успешно загружен";
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки: {uploadResult.Error}";
                }
            });
        }

        private static string GetMimeType(string extension) => extension.ToLower() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

        partial void OnTempDisplayNameChanged(string value)
        {
            _hasUnsavedChanges = true;
            if (!string.IsNullOrEmpty(ErrorMessage) && !string.IsNullOrWhiteSpace(value))
                ErrorMessage = null;
        }
    }
}