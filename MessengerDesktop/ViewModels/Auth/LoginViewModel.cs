using MessengerDesktop.Services.Network;
using MessengerDesktop.Services.Storage;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthManager _authManager;
    private readonly INavigationService _navigation;
    private readonly ISecureStorageService _secureStorage;
    private readonly IServerDiscoveryService _discoveryService;
    private readonly ISettingsService _settings;

    private const string RememberMeKey = "remember_me";
    private const string SavedUsernameKey = "saved_username";
    private const string ServerUrlKey = "server_url";
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(15);

    [ObservableProperty] public partial string Username { get; set; } = string.Empty;
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial bool RememberMe { get; set; }
    [ObservableProperty] public partial bool IsInitializing { get; set; } = true;
    [ObservableProperty] public partial bool CanRetryAutoLogin { get; set; }
    [ObservableProperty] public partial bool IsDiscovering { get; set; }
    [ObservableProperty] public partial string DiscoveryStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string ServerUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowManualUrlInput { get; set; }

    public LoginViewModel(IAuthManager authManager, INavigationService navigation, ISecureStorageService secureStorage,
        IServerDiscoveryService discoveryService, ISettingsService settings)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _discoveryService = discoveryService;
        _settings = settings;

        ServerUrl = _settings.Get<string>(ServerUrlKey) ?? App.ApiUrl;

        _ = InitializeAsync();
    }
    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverServerAsync()
    {
        IsDiscovering = true;
        ShowManualUrlInput = false;
        DiscoveryStatus = "Поиск сервера в сети...";
        DiscoverServerCommand.NotifyCanExecuteChanged();

        try
        {
            var found = await _discoveryService.DiscoverAsync(timeoutMs: 3000);

            if (found is not null)
            {
                ServerUrl = found;
                _settings.Set(ServerUrlKey, found);
                DiscoveryStatus = $"Найден: {found}";
            }
            else
            {
                DiscoveryStatus = "Сервер не найден. Введите адрес вручную.";
                ShowManualUrlInput = true;
            }
        }
        finally
        {
            IsDiscovering = false;
            DiscoverServerCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDiscover() => !IsDiscovering && !IsBusy;

    [RelayCommand]
    private void ApplyManualUrl()
    {
        var url = ServerUrl.Trim();
        if (string.IsNullOrEmpty(url)) return;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        if (!url.EndsWith('/'))
            url += "/";

        ServerUrl = url;
        _settings.Set(ServerUrlKey, url);
        ShowManualUrlInput = false;
        DiscoveryStatus = $"Адрес сохранён: {url}";
    }
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

    partial void OnIsInitializingChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        RetryAutoLoginCommand.NotifyCanExecuteChanged();
    }


    private async Task InitializeAsync()
    {
        try
        {
            var initTask = _authManager.WaitForInitializationAsync();
            var completed = await Task.WhenAny(initTask, Task.Delay(InitTimeout));

            if (completed != initTask)
            {
                ErrorMessage = "Не удалось автоматически восстановить сессию. Войдите вручную или попробуйте снова.";
                CanRetryAutoLogin = true;
                await LoadSavedUsernameAsync();
                _ = ObserveLateInitializationAsync(initTask);
                return;
            }

            await initTask;

            if (_authManager.HasValidSession())
            {
                Debug.WriteLine("LoginVM: Сессия восстановлена, переход в MainMenu");
                _navigation.NavigateToMainMenu();
                return;
            }

            Debug.WriteLine("LoginVM: Сессия не восстановлена, показываем форму логина");
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
    private async Task ObserveLateInitializationAsync(Task initTask)
    {
        try
        {
            await initTask;

            if (!_authManager.Session.IsAuthenticated)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Debug.WriteLine("LoginVM: Поздняя автоинициализация завершилась успешно, переход в MainMenu");
                _navigation.NavigateToMainMenu();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoginVM: Ошибка поздней автоинициализации: {ex.Message}");
        }
    }

    private async Task LoadSavedUsernameAsync()
    {
        try
        {
            RememberMe = await _secureStorage.GetAsync<bool>(RememberMeKey);
            if (RememberMe)
            {
                Username = await _secureStorage.GetAsync<string>(SavedUsernameKey) ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Load saved username error: {ex.Message}");
        }
    }

    private bool CanRetryAutoLoginExecute() => CanRetryAutoLogin && !IsBusy && !IsInitializing;

    [RelayCommand(CanExecute = nameof(CanRetryAutoLoginExecute))]
    private async Task RetryAutoLoginAsync()
    {
        ClearMessages();
        CanRetryAutoLogin = false;
        IsBusy = true;

        try
        {
            Debug.WriteLine("LoginVM: Повторная попытка auto-login...");

            var refreshed = await _authManager.TryRefreshTokenAsync();

            if (refreshed && _authManager.Session.IsAuthenticated)
            {
                Debug.WriteLine("LoginVM: Retry auto-login успешен, переход в MainMenu");
                _navigation.NavigateToMainMenu();
                return;
            }

            Debug.WriteLine("LoginVM: Retry auto-login неудачен");
            ErrorMessage = "Не удалось восстановить сессию. Войдите заново.";
            await LoadSavedUsernameAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка подключения: {ex.Message}";
            CanRetryAutoLogin = true;
            Debug.WriteLine($"Retry auto-login error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() => !IsBusy && !IsInitializing;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearMessages();
        CanRetryAutoLogin = false;

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
                    Debug.WriteLine("LoginVM: Инициализация AuthManager не завершилась вовремя, продолжаем ручной вход");
                }
                else
                {
                    await initTask;
                }
            }

            var result = await _authManager.LoginAsync(Username, Password, RememberMe);

            if (result.Success)
            {
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


    //[RelayCommand]
    //private async Task ClearCredentialsAsync()
    //{
    //    try
    //    {
    //        await _secureStorage.RemoveAsync(RememberMeKey);
    //        await _secureStorage.RemoveAsync(SavedUsernameKey);
    //    }
    //    catch (Exception ex)
    //    {
    //        Debug.WriteLine($"Clear credentials error: {ex.Message}");
    //    }

    //    Username = string.Empty;
    //    Password = string.Empty;
    //    RememberMe = false;
    //    CanRetryAutoLogin = false;
    //    ClearMessages();
    //}

    [RelayCommand]
    private void ToggleManualInput()
    {
        ShowManualUrlInput = !ShowManualUrlInput;
        if (ShowManualUrlInput)
        {
            DiscoveryStatus = "Введите адрес сервера вручную";
        }
        else
        {
            DiscoveryStatus = string.Empty;
        }
    }
    [RelayCommand]
    private async Task ClearCredentialsAsync()
    {
        try
        {
            // ВРЕМЕННО: полная очистка всех токенов и сессионных данных
            await _secureStorage.RemoveAsync("auth_token");
            await _secureStorage.RemoveAsync("auth_refresh_token");
            await _secureStorage.RemoveAsync("user_id");
            await _secureStorage.RemoveAsync("user_role");
            await _secureStorage.RemoveAsync("cached_user_id");
            await _secureStorage.RemoveAsync(RememberMeKey);
            await _secureStorage.RemoveAsync(SavedUsernameKey);

            Debug.WriteLine("=== ВСЕ ТОКЕНЫ ОЧИЩЕНЫ ===");
            ErrorMessage = "Данные очищены. Войдите заново.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clear error: {ex.Message}");
        }

        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        CanRetryAutoLogin = false;
        ClearMessages();
    }
}