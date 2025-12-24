using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Navigation;
using MessengerDesktop.Services.UI;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthManager _authManager;
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

    public LoginViewModel(IAuthManager authManager,INavigationService navigation,ISecureStorageService secureStorage,INotificationService notificationService)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _navigation = navigation;
        _secureStorage = secureStorage;
        _notificationService = notificationService;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _authManager.WaitForInitializationAsync();

            if (_authManager.Session.IsAuthenticated)
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
            Debug.WriteLine($"Load username error: {ex.Message}");
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
            Debug.WriteLine($"Save username error: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task Login()
    {
        await SafeExecuteAsync(async () =>
        {
            if (!_authManager.IsInitialized)
                await _authManager.WaitForInitializationAsync();

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите логин и пароль";
                return;
            }

            var result = await _authManager.LoginAsync(Username, Password);

            if (result.Success)
            {
                await SaveUsernameAsync();
                SuccessMessage = "Успешный вход!";
                _navigation.NavigateToMainMenu();
            }
            else
            {
                ErrorMessage = result.Error ?? "Неверный логин или пароль";
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