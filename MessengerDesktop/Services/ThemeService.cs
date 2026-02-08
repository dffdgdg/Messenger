using Avalonia;
using Avalonia.Styling;
using MessengerDesktop.Services.Storage;
using System;
using System.Diagnostics;

namespace MessengerDesktop.Services
{

    public interface IThemeService
    {
        bool IsDarkTheme { get; }

        /// <summary>Переключить тему (Dark ↔ Light).</summary>
        void Toggle();

        /// <summary>Загрузить тему из настроек при старте.</summary>
        void LoadFromSettings();
    }

    public class ThemeService : IThemeService
    {
        private readonly ISettingsService _settings;
        private readonly Application _app;

        public bool IsDarkTheme => _app.RequestedThemeVariant == ThemeVariant.Dark;

        public ThemeService(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _app = Application.Current ?? throw new InvalidOperationException("Application is not initialized");
        }

        public void Toggle()
        {
            var newTheme = _app.RequestedThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            _app.RequestedThemeVariant = newTheme;
            SaveTheme(newTheme);

            Debug.WriteLine($"[ThemeService] Theme toggled to: {newTheme}");
        }

        public void LoadFromSettings()
        {
            try
            {
                var isDark = _settings.Get<bool?>("IsDarkTheme") ?? true;
                _app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

                Debug.WriteLine($"[ThemeService] Theme loaded: {(isDark ? "Dark" : "Light")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeService] Error loading theme: {ex.Message}");
                _app.RequestedThemeVariant = ThemeVariant.Dark;
            }
        }

        private void SaveTheme(ThemeVariant theme)
        {
            try
            {
                _settings.Set("IsDarkTheme", theme == ThemeVariant.Dark);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThemeService] Error saving theme: {ex.Message}");
            }
        }
    }
}