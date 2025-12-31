using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Storage;
using MessengerShared.DTO;
using MessengerShared.Enum;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly Timer _autoSaveTimer;
    private readonly int _userId;
    private bool _isSaving;
    private bool _hasPendingChanges;
    private bool _isLoaded;

    [ObservableProperty] private Theme _selectedTheme;
    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _canBeFoundInSearch = true;

    public Theme[] AvailableThemes { get; } = Enum.GetValues<Theme>();
    public string CurrentThemeDisplay => SelectedTheme.ToString();

    public SettingsViewModel(
        MainMenuViewModel mainMenuViewModel,
        IApiClientService apiClient,
        ISettingsService settingsService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _userId = mainMenuViewModel?.UserId ?? throw new ArgumentNullException(nameof(mainMenuViewModel));

        _autoSaveTimer = new Timer(async _ => await SaveSettingsAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _selectedTheme = GetCurrentAppTheme();

        _ = LoadSettingsAsync();
    }

    private static Theme GetCurrentAppTheme()
    {
        var app = Application.Current;
        var themeVariant = app?.RequestedThemeVariant ?? app?.ActualThemeVariant;
        return themeVariant == ThemeVariant.Dark ? Theme.dark : Theme.light;
    }

    private async Task LoadSettingsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.GetAsync<UserDTO>($"api/user/{_userId}");
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
                var serverTheme = (Theme)data.Theme!;

                if (SelectedTheme != serverTheme)
                {
                    SelectedTheme = serverTheme;
                    ApplyTheme(serverTheme);
                }

                NotificationsEnabled = data.NotificationsEnabled ?? true;

                _settingsService.NotificationsEnabled = NotificationsEnabled;
                _settingsService.CanBeFoundInSearch = CanBeFoundInSearch;
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
            var result = await _apiClient.PutAsync<UserDTO>($"api/user/{_userId}", new UserDTO
            {
                Id = _userId,
                Theme = SelectedTheme,
                NotificationsEnabled = NotificationsEnabled,
            });

            if (result.Success)
            {
                _settingsService.NotificationsEnabled = NotificationsEnabled;
                _settingsService.CanBeFoundInSearch = CanBeFoundInSearch;

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

    private void ApplyTheme(Theme theme)
    {
        App.Current.ThemeVariant = theme switch
        {
            Theme.dark => ThemeVariant.Dark,
            Theme.light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
        OnPropertyChanged(nameof(CurrentThemeDisplay));
    }

    private void ScheduleAutoSave()
    {
        if (_isSaving || !_isLoaded) return;
        _hasPendingChanges = true;
        _autoSaveTimer.Change(1000, Timeout.Infinite);
    }

    partial void OnSelectedThemeChanged(Theme value)
    {
        if (_isSaving || !_isLoaded) return;
        ApplyTheme(value);
        ScheduleAutoSave();
    }

    partial void OnNotificationsEnabledChanged(bool value) => ScheduleAutoSave();
    partial void OnCanBeFoundInSearchChanged(bool value) => ScheduleAutoSave();

    [RelayCommand]
    private void ToggleTheme() => SelectedTheme = SelectedTheme == Theme.light ? Theme.dark : Theme.light;

    [RelayCommand]
    private async Task SaveNow() => await SaveSettingsAsync();

    [RelayCommand]
    private void ResetToDefaults()
    {
        SelectedTheme = Theme.light;
        NotificationsEnabled = true;
        CanBeFoundInSearch = true;
        SuccessMessage = "Настройки сброшены";
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