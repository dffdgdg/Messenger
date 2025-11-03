using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient = new() { BaseAddress = new System.Uri(App.ApiUrl) };
        private readonly MainMenuViewModel _mainMenuViewModel;
        private readonly Timer _autoSaveTimer;
        private bool _isSaving;
        private bool _hasPendingChanges;

        [ObservableProperty]
        private int userId;

        [ObservableProperty]
        private Theme selectedTheme = Theme.light;

        [ObservableProperty]
        private bool notificationsEnabled = true;

        [ObservableProperty]
        private bool canBeFoundInSearch = true;

        public Theme[] AvailableThemes { get; } = (Theme[])Enum.GetValues(typeof(Theme));

        // Свойство для отображения текущей темы в UI (если нужно)
        public string CurrentThemeDisplay => SelectedTheme.ToString();

        public SettingsViewModel(MainMenuViewModel mainMenuViewModel)
        {
            _mainMenuViewModel = mainMenuViewModel;
            UserId = mainMenuViewModel.UserId;

            _autoSaveTimer = new Timer(async _ => await SaveSettings(), null, Timeout.Infinite, Timeout.Infinite);

            _ = LoadSettings();
        }

        private async Task LoadSettings()
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<UserDTO>($"api/user/{UserId}");
                if (result != null)
                {
                    _isSaving = true;

                    SelectedTheme = result.Theme ?? Theme.light;
                    NotificationsEnabled = result.NotificationsEnabled ?? true;
                    CanBeFoundInSearch = result.CanBeFoundInSearch ?? true;

                    ApplyTheme(SelectedTheme);

                    _isSaving = false;
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка загрузки настроек: {ex.Message}");
            }
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

                var response = await _httpClient.PutAsJsonAsync($"api/user/{UserId}", updatedUser);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    await NotificationService.ShowError($"Ошибка сохранения: {error}");
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка: {ex.Message}");
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void ApplyTheme(Theme theme)
        {
            Application.Current!.RequestedThemeVariant = theme switch
            {
                Theme.dark => ThemeVariant.Dark,
                Theme.light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };

            // Обновляем свойство для привязок в UI
            OnPropertyChanged(nameof(CurrentThemeDisplay));
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            // Переключаем между светлой и темной темой
            SelectedTheme = SelectedTheme == Theme.light ? Theme.dark : Theme.light;

            // Тема применится автоматически через OnSelectedThemeChanged
        }

        private void ScheduleAutoSave()
        {
            if (_isSaving) return;

            _hasPendingChanges = true;
            _autoSaveTimer.Change(500, Timeout.Infinite);
        }

        partial void OnSelectedThemeChanged(Theme value)
        {
            if (!_isSaving)
            {
                // Применяем тему сразу при изменении
                ApplyTheme(value);
                ScheduleAutoSave();
            }
        }

        partial void OnNotificationsEnabledChanged(bool value)
        {
            if (!_isSaving)
            {
                ScheduleAutoSave();
            }
        }

        partial void OnCanBeFoundInSearchChanged(bool value)
        {
            if (!_isSaving)
            {
                ScheduleAutoSave();
            }
        }

        public void Dispose()
        {
            _autoSaveTimer?.Dispose();

            if (_hasPendingChanges && !_isSaving)
            {
                _ = SaveSettings();
            }
        }
    }
}