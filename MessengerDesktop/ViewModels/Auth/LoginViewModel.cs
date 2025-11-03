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
    public partial class LoginViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private readonly INavigationService _navigation;
        private const string CredentialsFile = "credentials.json";

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string? errorMessage;

        [ObservableProperty]
        private bool rememberMe;

        public LoginViewModel(HttpClient httpClient, AuthService authService, INavigationService navigation)
        {
            _httpClient = httpClient;
            _authService = authService;
            _navigation = navigation;
            LoadSavedCredentials();
        }

        private void LoadSavedCredentials()
        {
            try
            {
                if (File.Exists(CredentialsFile))
                {
                    var json = File.ReadAllText(CredentialsFile);
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
                NotificationService.ShowError($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private void SaveCredentials()
        {
            if (RememberMe)
            {
                try
                {
                    var credentials = new LoginDTO { Username = Username, Password = Password };
                    var json = JsonSerializer.Serialize(credentials);
                    File.WriteAllText(CredentialsFile, json);
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError($"Ошибка сохранения данных: {ex.Message}");
                }
            }
            else if (File.Exists(CredentialsFile))
            {
                try
                {
                    File.Delete(CredentialsFile);
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError($"Ошибка удаления данных: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public async Task Login()
        {
            try
            {
                ErrorMessage = null;

                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Введите логин и пароль";
                    return;
                }

                var success = await _authService.LoginAsync(Username, Password);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"Login успешен: UserId={_authService.UserId}");

                    SaveCredentials();

                    if (_authService.UserId.HasValue)
                    {
                        NotificationService.ShowSuccess($"Переход в MainMenu с UserId={_authService.UserId.Value}");
                        _navigation.NavigateToMainMenu(_authService.UserId.Value);
                    }
                    else
                    {
                        NotificationService.ShowError("ОШИБКА: UserId is null после успешного логина!");
                        ErrorMessage = "Ошибка авторизации: ID пользователя не получен";
                    }
                }
                else ErrorMessage = "Неверный логин или пароль";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка входа: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Ошибка в Login: {ex.Message}");
            }
        }
    }
}