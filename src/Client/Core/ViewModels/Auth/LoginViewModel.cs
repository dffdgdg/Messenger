using Avalonia.Controls.ApplicationLifetimes;
using Core.ViewModels.Dialog;
using System.Diagnostics;

namespace Core.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthManager _authManager;
    private readonly INavigationService _navigation;
    private readonly ISecureStorageService _secureStorage;
    private readonly IDialogService _dialogService;
    private const string RememberMeKey = "remember_me";
    private const string SavedUsernameKey = "saved_username";
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(15);

    [ObservableProperty] public partial string Username { get; set; } = string.Empty;
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial bool RememberMe { get; set; }
    [ObservableProperty] public partial bool IsInitializing { get; set; } = true;
    [ObservableProperty] public partial bool CanRetryAutoLogin { get; set; }
    [ObservableProperty] public partial string ServerUrl { get; set; } = string.Empty;

    public LoginViewModel(IAuthManager authManager, INavigationService navigation,
        ISecureStorageService secureStorage, IDialogService dialogService)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _dialogService = dialogService;
        ServerUrl = AppConfig.ApiUrl;
        _ = InitializeAsync();
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
                ErrorMessage = "Не удалось автоматически восстановить сессию. Войдите вручную.";
                CanRetryAutoLogin = true;
                await LoadSavedUsernameAsync();
                _ = ObserveLateInitializationAsync(initTask);

                await SuggestManualServerAsync();
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

            await SuggestManualServerAsync();
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

    /// <summary>
    /// Если сервер не был найден через UDP, предлагаем пользователю ввести адрес вручную
    /// </summary>
    private async Task SuggestManualServerAsync()
    {
        await Task.Delay(500);

        if (AppConfig.WasDiscovered)
            return;

        if (ServerUrl.Contains("localhost") || ServerUrl == "http://localhost:5274/")
        {
            var dialog = new ConfirmDialogViewModel(
                "Сервер не найден",
                "Не удалось обнаружить сервер в локальной сети.\n\nХотите указать адрес сервера вручную?",
                "Указать адрес",
                "Пропустить")
            {
                CanCloseOnBackgroundClick = false
            };

            await _dialogService.ShowAsync(dialog);
            var shouldConfigure = await dialog.Result;

            if (shouldConfigure)
            {
                await SetServerUrlAsync();
            }
        }
    }

    private async Task ObserveLateInitializationAsync(Task initTask)
    {
        try
        {
            await initTask;
            if (!_authManager.Session.IsAuthenticated) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Debug.WriteLine("LoginVM: Поздняя автоинициализация завершилась успешно");
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
                Username = await _secureStorage.GetAsync<string>(SavedUsernameKey) ?? string.Empty;
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
            var refreshed = await _authManager.TryRefreshTokenAsync();
            if (refreshed && _authManager.Session.IsAuthenticated)
            {
                _navigation.NavigateToMainMenu();
                return;
            }
            ErrorMessage = "Не удалось восстановить сессию. Войдите заново.";
            await LoadSavedUsernameAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка подключения: {ex.Message}";
            CanRetryAutoLogin = true;
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
                    Debug.WriteLine("LoginVM: Инициализация AuthManager не завершена, продолжаем вход");
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
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetServerUrlAsync()
    {
        var dialog = new ServerUrlDialogViewModel(ServerUrl);
        await _dialogService.ShowAsync(dialog);

        if (string.IsNullOrWhiteSpace(dialog.ServerUrl))
            return;

        var newUrl = dialog.ServerUrl;

        if (string.Equals(newUrl.TrimEnd('/'), ServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return;

        AppConfig.SetApiUrl(newUrl, manual: true);
        ServerUrl = newUrl;

        var confirmDialog = new ConfirmDialogViewModel(
            "Смена сервера",
            $"Адрес сервера изменён на:\n{newUrl}\n\nДля применения изменений требуется перезапуск приложения. Перезапустить сейчас?",
            "Перезапустить",
            "Позже")
        {
            CanCloseOnBackgroundClick = false
        };

        await _dialogService.ShowAsync(confirmDialog);
        var shouldRestart = await confirmDialog.Result;

        if (shouldRestart)
            AppConfig.RestartApplication();
    }

    [RelayCommand]
    private async Task ClearCredentialsAsync()
    {
        try
        {
            await _secureStorage.RemoveAsync(RememberMeKey);
            await _secureStorage.RemoveAsync(SavedUsernameKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clear credentials error: {ex.Message}");
        }
        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        CanRetryAutoLogin = false;
        ClearMessages();
    }
}