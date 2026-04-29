using AppTheme = MessengerShared.Enum.Theme;

namespace MessengerDesktop.Services.Abstractions;

public interface IThemeService
{
    bool IsDarkTheme { get; }
    void Toggle();
    void LoadFromSettings();
    void SaveTheme(AppTheme theme);
}