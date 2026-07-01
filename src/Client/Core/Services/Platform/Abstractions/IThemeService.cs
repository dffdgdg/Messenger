using AppTheme = Shared.Enum.Theme;

namespace Core.Services.Platform.Abstractions;

public interface IThemeService
{
    Color CurrentAccent { get; }
    bool UseSystemAccent { get; }
    bool IsDarkTheme { get; }
    bool IsColoredTheme { get; }

    void Initialize(ResourceDictionary lightDict, ResourceDictionary darkDict);
    void LoadFromSettings();
    void SaveTheme(AppTheme theme);
    void Toggle();
    void SwitchTheme(AppTheme theme);
    void SetAccent(Color accent, bool saveToSettings = true);
    void ApplyColoredTheme(Color accent);
    Task SetUseSystemAccentAsync(bool use);
}