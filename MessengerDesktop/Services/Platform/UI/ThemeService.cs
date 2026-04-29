using Avalonia.Styling;
using System;
using System.Diagnostics;
using AppTheme = MessengerShared.Enum.Theme;

namespace MessengerDesktop.Services.Infrastructure.UI;

public class ThemeService(ISettingsService settings) : IThemeService
{
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly Application _app = Application.Current ?? throw new InvalidOperationException("Application is not initialized");

    public bool IsDarkTheme => _app.RequestedThemeVariant == ThemeVariant.Dark;

    public void Toggle()
    {
        var currentTheme = GetCurrentTheme();
        var newTheme = currentTheme == AppTheme.dark ? AppTheme.light : AppTheme.dark;

        ApplyTheme(newTheme);
        SaveTheme(newTheme);

        Debug.WriteLine($"[ThemeService] Theme toggled to: {newTheme}");
    }

    public void LoadFromSettings()
    {
        try
        {
            var theme = _settings.Get<AppTheme?>("Theme") ?? AppTheme.system;
            ApplyTheme(theme);

            Debug.WriteLine($"[ThemeService] Theme loaded: {theme}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] Error loading theme: {ex.Message}");
            _app.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    public void SaveTheme(AppTheme theme)
    {
        try
        {
            _settings.Set("Theme", theme);
            Debug.WriteLine($"[ThemeService] Theme saved: {theme}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] Error saving theme: {ex.Message}");
        }
    }

    private void ApplyTheme(AppTheme theme) => _app.RequestedThemeVariant = theme switch
    {
        AppTheme.dark => ThemeVariant.Dark,
        AppTheme.light => ThemeVariant.Light,
        AppTheme.system => ThemeVariant.Default,
        _ => ThemeVariant.Default
    };

    private AppTheme GetCurrentTheme()
    {
        var requested = _app.RequestedThemeVariant;

        if (requested == ThemeVariant.Dark)
            return AppTheme.dark;

        if (requested == ThemeVariant.Light)
            return AppTheme.light;

        return AppTheme.system;
    }
}