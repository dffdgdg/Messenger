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

namespace MessengerDesktop.ViewModels
{
    public partial class SettingsViewModel : BaseViewModel, IDisposable
    {
        private readonly IApiClientService _apiClient;
        private readonly MainMenuViewModel _mainMenuViewModel;
        private readonly Timer _autoSaveTimer;
        private bool _isSaving;
        private bool _hasPendingChanges;
        private bool _isLoaded;

        [ObservableProperty]
        private int _userId;

        [ObservableProperty]
        private Theme _selectedTheme;

        [ObservableProperty]
        private bool _notificationsEnabled = true;

        [ObservableProperty]
        private bool _canBeFoundInSearch = true;

        public Theme[] AvailableThemes { get; } = (Theme[])Enum.GetValues(typeof(Theme));

        public string CurrentThemeDisplay => SelectedTheme.ToString();

        public SettingsViewModel(MainMenuViewModel mainMenuViewModel, IApiClientService apiClient)
        {
            _mainMenuViewModel = mainMenuViewModel;
            _apiClient = apiClient;
            UserId = mainMenuViewModel.UserId;

            _autoSaveTimer = new Timer(async _ => await SaveSettings(), null, Timeout.Infinite, Timeout.Infinite);
            _selectedTheme = GetCurrentAppTheme();
            _ = LoadSettings();
        }

        private static Theme GetCurrentAppTheme()
        {
            var themeVariant = Application.Current?.RequestedThemeVariant;

            if (themeVariant == ThemeVariant.Dark)
                return Theme.dark;
            if (themeVariant == ThemeVariant.Light)
                return Theme.light;

            return Application.Current?.ActualThemeVariant == ThemeVariant.Dark
                ? Theme.dark
                : Theme.light;
        }

        private async Task LoadSettings()
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.GetAsync<UserDTO>($"api/user/{UserId}");
                if (result.Success && result.Data != null)
                {
                    _isSaving = true;

                    var serverTheme = (Theme)result.Data.Theme;
                    var serverNotifications = result.Data.NotificationsEnabled ?? true;
                    var serverCanBeFound = result.Data.CanBeFoundInSearch ?? true;

                    if (SelectedTheme != serverTheme)
                    {
                        SelectedTheme = serverTheme;
                        ApplyTheme(SelectedTheme);
                        OnPropertyChanged(nameof(SelectedTheme));
                    }

                    NotificationsEnabled = serverNotifications;
                    CanBeFoundInSearch = serverCanBeFound;

                    _isSaving = false;
                    _isLoaded = true;
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки настроек: {result.Error}";
                    _isLoaded = true;
                }
            });
        }

        private async Task SaveSettings()
        {
            if (_isSaving) return;

            _isSaving = true;
            _hasPendingChanges = false;

            try
            {
                var updatedUser = new UserDTO
                {
                    Id = UserId,
                    Theme = SelectedTheme,
                    NotificationsEnabled = NotificationsEnabled,
                    CanBeFoundInSearch = CanBeFoundInSearch
                };

                var result = await _apiClient.PutAsync<UserDTO>($"api/user/{UserId}", updatedUser);
                if (result.Success)
                {
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
            var themeVariant = theme switch
            {
                Theme.dark => ThemeVariant.Dark,
                Theme.light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };

            App.Current.ThemeVariant = themeVariant;

            OnPropertyChanged(nameof(CurrentThemeDisplay));
        }

        [RelayCommand]
        private void ToggleTheme() 
            => SelectedTheme = SelectedTheme == Theme.light ? Theme.dark : Theme.light;

        [RelayCommand]
        private async Task SaveNow() 
            => await SaveSettings();

        private void ScheduleAutoSave()
        {
            if (_isSaving) return;

            _hasPendingChanges = true;
            _autoSaveTimer.Change(1000, Timeout.Infinite);
        }

        partial void OnSelectedThemeChanged(Theme value)
        {
            if (!_isSaving && _isLoaded)
            {
                ApplyTheme(value);
                ScheduleAutoSave();
            }
        }

        partial void OnNotificationsEnabledChanged(bool value)
        {
            if (!_isSaving && _isLoaded) ScheduleAutoSave();
        }

        partial void OnCanBeFoundInSearchChanged(bool value)
        {
            if (!_isSaving && _isLoaded) ScheduleAutoSave();
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            SelectedTheme = Theme.light;
            NotificationsEnabled = true;
            CanBeFoundInSearch = true;
            SuccessMessage = "Настройки сброшены к значениям по умолчанию";
        }

        public void Dispose()
        {
            _autoSaveTimer?.Dispose();

            if (_hasPendingChanges && !_isSaving)
                _ = SaveSettings();
        }
    }
}