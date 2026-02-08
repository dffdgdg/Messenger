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
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(15);

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private bool _isInitializing = true;

    public LoginViewModel(IAuthManager authManager, INavigationService navigation, ISecureStorageService secureStorage, INotificationService notificationService)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        InitializeAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.WriteLine($"Login init failed: {t.Exception?.Flatten().Message}");
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ========== Хуки из BaseViewModel ==========

    protected override void OnIsBusyUpdated(bool value) => LoginCommand.NotifyCanExecuteChanged();

    protected override void OnErrorMessageUpdated(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            SuccessMessage = null;
    }

    protected override void OnSuccessMessageUpdated(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ErrorMessage = null;
    }

    partial void OnIsInitializingChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();

    // ========== Init ==========

    private async Task InitializeAsync()
    {
        try
        {
            var initTask = _authManager.WaitForInitializationAsync();
            var completed = await Task.WhenAny(initTask, Task.Delay(InitTimeout));

            if (completed != initTask)
            {
                ErrorMessage = "Сервер не отвечает. Попробуйте позже.";
                return;
            }

            await initTask;

            if (_authManager.Session.IsAuthenticated)
            {
                _navigation.NavigateToMainMenu();
                return;
            }

            await LoadSavedUsernameAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка инициализации: {ex.Message}";
            Debug.WriteLine($"Init error: {ex}");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task LoadSavedUsernameAsync()
    {
        try
        {
            RememberMe = await _secureStorage.GetAsync<bool>(RememberMeKey);
            if (RememberMe)
                Username = await _secureStorage.GetAsync<string>(UsernameKey) ?? string.Empty;
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
                await _secureStorage.SaveAsync(UsernameKey, Username);
            else
                await _secureStorage.RemoveAsync(UsernameKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save username error: {ex.Message}");
        }
    }

    // ========== Login ==========

    private bool CanLogin() => !IsBusy && !IsInitializing;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearMessages();

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Введите логин и пароль";
            return;
        }

        IsBusy = true;
        try
        {
            if (!_authManager.IsInitialized)
            {
                var initTask = _authManager.WaitForInitializationAsync();
                var completed = await Task.WhenAny(initTask, Task.Delay(InitTimeout));

                if (completed != initTask)
                {
                    ErrorMessage = "Сервер не отвечает. Попробуйте позже.";
                    return;
                }

                await initTask;
            }

            var result = await _authManager.LoginAsync(Username, Password);

            if (result.Success)
            {
                await SaveUsernameAsync();
                _navigation.NavigateToMainMenu();
            }
            else
            {
                ErrorMessage = result.Error ?? "Неверный логин или пароль";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка входа: {ex.Message}";
            Debug.WriteLine($"Login error: {ex}");
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    // ========== Clear ==========

    [RelayCommand]
    private async Task ClearCredentialsAsync()
    {
        try
        {
            await _secureStorage.RemoveAsync(RememberMeKey);
            await _secureStorage.RemoveAsync(UsernameKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clear credentials error: {ex.Message}");
        }

        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        ClearMessages();
    }
}