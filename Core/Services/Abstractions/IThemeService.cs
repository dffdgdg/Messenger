using AppTheme = Shared.Enum.Theme;

namespace Core.Services.Abstractions;

public interface IThemeService
{
    bool IsDarkTheme { get; }
    void Toggle();
    void LoadFromSettings();
    void SaveTheme(AppTheme theme);
}