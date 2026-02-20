using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Cache;
using MessengerDesktop.Services.Storage;
using MessengerShared.DTO.User;
using System;
using System.Threading;
using System.Threading.Tasks;

using AppTheme = MessengerShared.Enum.Theme;

namespace MessengerDesktop.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;
    private readonly ICacheMaintenanceService _cacheMaintenanceService;
    private readonly ISettingsService _settingsService;
    private readonly Timer _autoSaveTimer;
    private readonly int _userId;
    private bool _isSaving;
    private bool _hasPendingChanges;
    private bool _isLoaded;

    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _canBeFoundInSearch = true;

    public SettingsViewModel(
        MainMenuViewModel mainMenuViewModel,
        IApiClientService apiClient,
        ICacheMaintenanceService cacheMaintenanceService,
        ISettingsService settingsService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cacheMaintenanceService = cacheMaintenanceService ?? throw new ArgumentNullException(nameof(cacheMaintenanceService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _userId = mainMenuViewModel?.UserId ?? throw new ArgumentNullException(nameof(mainMenuViewModel));

        _autoSaveTimer = new Timer(async _ => await SaveSettingsAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _selectedTheme = GetCurrentAppTheme();

        _ = LoadSettingsAsync();
    }

    private static AppTheme GetCurrentAppTheme()
    {
        var app = Application.Current;
        var themeVariant = app?.RequestedThemeVariant;

        if (themeVariant == null || themeVariant == ThemeVariant.Default)
            return AppTheme.system;

        return themeVariant == ThemeVariant.Dark ? AppTheme.dark : AppTheme.light;
    }

    private async Task LoadSettingsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.GetAsync<UserDTO>(ApiEndpoints.User.ById(_userId));
            if (!result.Success || result.Data == null)
            {
                ErrorMessage = $"Ошибка загрузки настроек: {result.Error}";
                _isLoaded = true;
                return;
            }

            _isSaving = true;
            try
            {
                var data = result.Data;
                var serverTheme = (AppTheme)data.Theme!;

                if (SelectedTheme != serverTheme)
                {
                    SelectedTheme = serverTheme;
                    ApplyTheme(serverTheme);
                }

                NotificationsEnabled = data.NotificationsEnabled ?? true;
                _settingsService.NotificationsEnabled = NotificationsEnabled;
            }
            finally
            {
                _isSaving = false;
                _isLoaded = true;
            }
        });
    }

    private async Task SaveSettingsAsync()
    {
        if (_isSaving) return;

        _isSaving = true;
        _hasPendingChanges = false;

        try
        {
            var result = await _apiClient.PutAsync<UserDTO>(ApiEndpoints.User.ById(_userId), new UserDTO
            {
                Id = _userId,
                Theme = SelectedTheme,
                NotificationsEnabled = NotificationsEnabled,
            });

            if (result.Success)
            {
                _settingsService.NotificationsEnabled = NotificationsEnabled;
                SuccessMessage = "Настройки сохранены";
            }
            else
            {
                ErrorMessage = $"Ошибка сохранения: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.dark => ThemeVariant.Dark,
            AppTheme.light => ThemeVariant.Light,
            AppTheme.system => ThemeVariant.Default,
            _ => ThemeVariant.Default
        };
    }

    private void ScheduleAutoSave()
    {
        if (_isSaving || !_isLoaded) return;
        _hasPendingChanges = true;
        _autoSaveTimer.Change(800, Timeout.Infinite);
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_isSaving || !_isLoaded) return;
        ApplyTheme(value);
        ScheduleAutoSave();
    }

    partial void OnNotificationsEnabledChanged(bool value) => ScheduleAutoSave();
    partial void OnCanBeFoundInSearchChanged(bool value) => ScheduleAutoSave();

    [RelayCommand]
    private async Task SaveNow() => await SaveSettingsAsync();

    [RelayCommand]
    private void ResetToDefaults()
    {
        SelectedTheme = AppTheme.light;
        NotificationsEnabled = true;
        CanBeFoundInSearch = true;
        SuccessMessage = "Настройки сброшены";
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            await _cacheMaintenanceService.ClearAllDataAsync();
            SuccessMessage = "Кэш успешно очищен";
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoSaveTimer.Dispose();
            if (_hasPendingChanges && !_isSaving)
                _ = SaveSettingsAsync();
        }
        base.Dispose(disposing);
    }
}