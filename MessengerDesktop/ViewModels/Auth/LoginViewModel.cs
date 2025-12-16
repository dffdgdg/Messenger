using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Navigation;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;
    private readonly ISecureStorageService _secureStorage;
    private readonly INotificationService _notificationService;

    private const string RememberMeKey = "remember_me";
    private const string UsernameKey = "saved_username";

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool rememberMe;

    public LoginViewModel(
        IAuthService authService,
        INavigationService navigation,
        ISecureStorageService secureStorage,
        INotificationService notificationService)
    {
        _authService = authService;
        _navigation = navigation;
        _secureStorage = secureStorage;
        _notificationService = notificationService;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _authService.WaitForInitializationAsync();

            if (_authService.IsAuthenticated)
            {
                _navigation.NavigateToMainMenu();
                return;
            }

            await LoadSavedUsernameAsync();
        }
        catch (Exception ex)
        {
            await _notificationService.ShowErrorAsync($"Ошибка инициализации: {ex.Message}");
        }
    }

    private async Task LoadSavedUsernameAsync()
    {
        try
        {
            RememberMe = await _secureStorage.GetAsync<bool>(RememberMeKey);
            if (RememberMe)
            {
                Username = await _secureStorage.GetAsync<string>(UsernameKey) ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load username error: {ex.Message}");
        }
    }

    private async Task SaveUsernameAsync()
    {
        try
        {
            await _secureStorage.SaveAsync(RememberMeKey, RememberMe);
            if (RememberMe)
            {
                await _secureStorage.SaveAsync(UsernameKey, Username);
            }
            else
            {
                await _secureStorage.RemoveAsync(UsernameKey);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save username error: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task Login()
    {
        await SafeExecuteAsync(async () =>
        {
            if (!_authService.IsInitialized)
                await _authService.WaitForInitializationAsync();

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите логин и пароль";
                return;
            }

            var success = await _authService.LoginAsync(Username, Password);

            if (success)
            {
                await SaveUsernameAsync();
                SuccessMessage = "Успешный вход!";
                _navigation.NavigateToMainMenu();
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
        await _secureStorage.RemoveAsync(RememberMeKey);
        await _secureStorage.RemoveAsync(UsernameKey);
        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        ClearMessages();
    }
}