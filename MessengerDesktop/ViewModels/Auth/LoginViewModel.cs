using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class LoginViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        private readonly INavigationService _navigation;
        private const string CredentialsFile = "credentials.json";

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private bool rememberMe;

        public LoginViewModel(AuthService authService, INavigationService navigation)
        {
            _authService = authService;
            _navigation = navigation;
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _authService.WaitForInitializationAsync();
                await LoadSavedCredentialsAsync();
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка инициализации: {ex.Message}");
            }
        }

        private async Task LoadSavedCredentialsAsync()
        {
            try
            {
                if (File.Exists(CredentialsFile))
                {
                    var json = await File.ReadAllTextAsync(CredentialsFile);
                    var credentials = JsonSerializer.Deserialize<LoginDTO>(json);
                    if (credentials != null)
                    {
                        Username = credentials.Username;
                        Password = credentials.Password;
                        RememberMe = true;
                    }
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private async Task SaveCredentialsAsync()
        {
            try
            {
                if (RememberMe)
                {
                    var credentials = new LoginDTO { Username = Username, Password = Password };
                    var json = JsonSerializer.Serialize(credentials);
                    await File.WriteAllTextAsync(CredentialsFile, json);
                }
                else if (File.Exists(CredentialsFile))
                {
                    File.Delete(CredentialsFile);
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка сохранения данных: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task Login()
        {
            await SafeExecuteAsync(async () =>
            {
                if (!_authService.IsInitialized)
                {
                    await _authService.WaitForInitializationAsync();
                }

                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Введите логин и пароль";
                    return;
                }

                var success = await _authService.LoginAsync(Username, Password);

                if (success)
                {
                    await Task.Delay(100);

                    for (int i = 0; i < 5; i++)
                    {
                        if (_authService.UserId.HasValue) break;
                        await Task.Delay(50);
                    }

                    if (_authService.UserId.HasValue)
                    {
                        await SaveCredentialsAsync();
                        SuccessMessage = "Успешный вход!";
                        _navigation.NavigateToMainMenu();
                    }
                    else
                    {
                        ErrorMessage = "Ошибка авторизации: ID пользователя не получен";
                    }
                }
                else
                {
                    ErrorMessage = "Неверный логин или пароль";
                }
            });
        }

        [RelayCommand]
        public async Task ClearCredentials()
        {
            try
            {
                if (File.Exists(CredentialsFile))
                {
                    File.Delete(CredentialsFile);
                }
                Username = string.Empty;
                Password = string.Empty;
                RememberMe = false;
                ClearMessages();
                await NotificationService.ShowSuccess("Данные для входа очищены");
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка очистки данных: {ex.Message}");
            }
        }
    }
}