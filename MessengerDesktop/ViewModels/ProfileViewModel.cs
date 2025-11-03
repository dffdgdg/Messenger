using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ProfileViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;

        [ObservableProperty]
        private UserDTO? user;

        [ObservableProperty]
        private int userId;

        [ObservableProperty]
        private bool isEditing;

        [ObservableProperty]
        private string tempDisplayName = string.Empty;

        [ObservableProperty]
        private Bitmap? avatarBitmap;

        private bool hasUnsavedChanges;

        public string? AvatarUrl => User?.Avatar == null ? null : $"https://localhost:7190{User.Avatar}";

        public ProfileViewModel(int userId)
        {
            // Set up HttpClient with timeout and SSL handling
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(App.ApiUrl),
                Timeout = TimeSpan.FromMinutes(5) // Longer timeout for uploads
            };

            this.userId = userId;
            _ = LoadUser();
        }

        partial void OnUserChanged(UserDTO? value)
        {
            OnPropertyChanged(nameof(AvatarUrl));
            _ = LoadAvatarBitmap();
        }

        private async Task LoadUser()
        {
            var result = await _httpClient.GetFromJsonAsync<UserDTO>($"api/user/{this.UserId}");
            if (result != null)
            {
                User = result;
                TempDisplayName = result.DisplayName ?? string.Empty;
            }
        }

        private async Task LoadAvatarBitmap()
        {
            if (User?.Avatar == null)
            {
                AvatarBitmap = null;
                return;
            }
            try
            {
                var url = $"https://localhost:7190{User.Avatar}";

                // Сначала пробуем загрузить без токена для проверки защиты
                using (var testClient = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                }))
                {
                    var testResponse = await testClient.GetAsync(url);
                    if (testResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        NotificationService.ShowWarning("Проверка защиты: доступ без токена запрещен (это правильно)");
                    }
                    else
                    {
                        NotificationService.ShowError("Внимание: файл доступен без авторизации!");
                    }
                }

                // Теперь пробуем загрузить с токеном
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var token = NotificationService.GetAuthToken();
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    AvatarBitmap = new Bitmap(stream);
                    NotificationService.ShowSuccess("Аватар успешно загружен с токеном");
                }
                else
                {
                    AvatarBitmap = null;
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        NotificationService.ShowError("Нет доступа к файлу: отсутствует или недействительный токен");
                    }
                    else
                    {
                        NotificationService.ShowError($"Ошибка загрузки аватара: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                AvatarBitmap = null;
                NotificationService.ShowError($"Ошибка загрузки аватара: {ex.Message}");
            }
        }

        [RelayCommand]
        private void StartEdit()
        {
            if (User == null) return;
            TempDisplayName = User.DisplayName ?? string.Empty;
            IsEditing = true;
            hasUnsavedChanges = false;
        }

        [RelayCommand]
        private void CancelEdit()
        {
            if (!hasUnsavedChanges || User == null) 
            {
                IsEditing = false;
                return;
            }
            TempDisplayName = User.DisplayName ?? string.Empty;
            IsEditing = false;
            hasUnsavedChanges = false;
        }

        [RelayCommand]
        private async Task SaveChanges()
        {
            if (User == null) return;
            try
            {
                var updatedUser = new UserDTO
                {
                    Id = User.Id,
                    Username = User.Username,
                    DisplayName = TempDisplayName,
                    Avatar = User.Avatar
                };
                var response = await _httpClient.PutAsJsonAsync($"api/user/{User.Id}", updatedUser);
                if (response.IsSuccessStatusCode)
                {
                    User.DisplayName = TempDisplayName;
                    IsEditing = false;
                    hasUnsavedChanges = false;
                    await NotificationService.ShowSuccess("Изменения сохранены");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    await NotificationService.ShowError($"Ошибка сохранения: {error}");
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task UploadAvatar()
        {
            if (User == null) return;

            try
            {
                var dlg = new OpenFileDialog
                {
                    AllowMultiple = false,
                    Title = "Выберите изображение"
                };
                dlg.Filters.Add(new FileDialogFilter { Name = "Images", Extensions = { "png", "jpg", "jpeg", "gif" } });

                var window = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                var result = await dlg.ShowAsync(window);
                if (result == null || result.Length == 0) return;

                var filePath = result[0];
                var fileInfo = new FileInfo(filePath);

                if (fileInfo.Length > 5 * 1024 * 1024)
                {
                    await NotificationService.ShowError("Файл слишком большой. Максимальный размер: 5MB");
                    return;
                }

                // Use using statements for proper disposal
                using var fileStream = File.OpenRead(filePath);
                using var streamContent = new StreamContent(fileStream);
                using var content = new MultipartFormDataContent();

                // Add file content
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeType(Path.GetExtension(filePath)));
                content.Add(streamContent, "file", Path.GetFileName(filePath));

                // Upload with timeout handling
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(2));
                var response = await _httpClient.PostAsync($"api/user/{User.Id}/avatar", content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var userInfo = await _httpClient.GetFromJsonAsync<UserDTO>($"api/user/{User.Id}");
                    if (userInfo != null)
                    {
                        User.Avatar = userInfo.Avatar;
                        await LoadAvatarBitmap();
                        await NotificationService.ShowSuccess("Аватар успешно загружен");
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    await NotificationService.ShowError($"Ошибка загрузки: {error}");
                }
            }
            catch (OperationCanceledException)
            {
                await NotificationService.ShowError("Превышено время ожидания загрузки");
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка загрузки: {ex.Message}");
            }
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
            hasUnsavedChanges = true;
        }
    }
}
