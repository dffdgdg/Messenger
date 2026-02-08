using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using System;

using AppTheme = MessengerShared.Enum.Theme;

namespace MessengerDesktop.Controls;

public partial class ThemeSelectorControl : UserControl
{
    public static readonly DirectProperty<ThemeSelectorControl, bool> IsSystemDarkNowProperty =
        AvaloniaProperty.RegisterDirect<ThemeSelectorControl, bool>
        (nameof(IsSystemDarkNow), o => o.IsSystemDarkNow);

    private bool _isSystemDarkNow;
    public bool IsSystemDarkNow
    {
        get => _isSystemDarkNow;
        private set => SetAndRaise(IsSystemDarkNowProperty, ref _isSystemDarkNow, value);
    }

    public static readonly StyledProperty<AppTheme> SelectedThemeProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, AppTheme>(nameof(SelectedTheme));

    public static readonly StyledProperty<bool> IsLightSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsLightSelected));

    public static readonly StyledProperty<bool> IsDarkSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsDarkSelected));

    public static readonly StyledProperty<bool> IsSystemSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsSystemSelected));

    public AppTheme SelectedTheme { get => GetValue(SelectedThemeProperty); set => SetValue(SelectedThemeProperty, value); }
    public bool IsLightSelected { get => GetValue(IsLightSelectedProperty); set => SetValue(IsLightSelectedProperty, value); }
    public bool IsDarkSelected { get => GetValue(IsDarkSelectedProperty); set => SetValue(IsDarkSelectedProperty, value); }
    public bool IsSystemSelected { get => GetValue(IsSystemSelectedProperty); set => SetValue(IsSystemSelectedProperty, value); }

    public ThemeSelectorControl()
    {
        InitializeComponent();
        SelectedThemeProperty.Changed.AddClassHandler<ThemeSelectorControl>((x, e) => x.OnSelectedThemeChanged(e));

        // Подписываемся на изменения темы в системе через Application
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) => UpdateSystemThemeInfo();
            UpdateSystemThemeInfo();
        }
    }

    private void UpdateSystemThemeInfo() => IsSystemDarkNow = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    private void OnSelectedThemeChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is AppTheme theme)
        {
            IsLightSelected = theme == AppTheme.light;
            IsDarkSelected = theme == AppTheme.dark;
            IsSystemSelected = theme == AppTheme.system;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Когда пользователь кликает на RadioButton, обновляем основной Enum
        if (change.Property == IsLightSelectedProperty && (bool)change.NewValue!) SelectedTheme = AppTheme.light;
        else if (change.Property == IsDarkSelectedProperty && (bool)change.NewValue!) SelectedTheme = AppTheme.dark;
        else if (change.Property == IsSystemSelectedProperty && (bool)change.NewValue!) SelectedTheme = AppTheme.system;
    }
}