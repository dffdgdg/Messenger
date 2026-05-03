using Avalonia.Styling;
using System;
using AppTheme = Shared.Enum.Theme;

namespace Desktop.Views.Controls;

public partial class ThemeSelectorControl : UserControl
{
    public static readonly DirectProperty<ThemeSelectorControl, bool> IsSystemDarkNowProperty = AvaloniaProperty.RegisterDirect<ThemeSelectorControl, bool>(nameof(IsSystemDarkNow), o => o.IsSystemDarkNow);
    public static readonly StyledProperty<AppTheme> SelectedThemeProperty = AvaloniaProperty.Register<ThemeSelectorControl, AppTheme>(nameof(SelectedTheme));
    public static readonly StyledProperty<bool> IsLightSelectedProperty = AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsLightSelected));
    public static readonly StyledProperty<bool> IsDarkSelectedProperty = AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsDarkSelected));
    public static readonly StyledProperty<bool> IsSystemSelectedProperty = AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsSystemSelected));

    private bool _isSystemDarkNow;
    private bool _isUpdatingState;

    public bool IsSystemDarkNow
    {
        get => _isSystemDarkNow;
        private set => SetAndRaise(IsSystemDarkNowProperty, ref _isSystemDarkNow, value);
    }

    public AppTheme SelectedTheme { get => GetValue(SelectedThemeProperty); set => SetValue(SelectedThemeProperty, value); }
    public bool IsLightSelected { get => GetValue(IsLightSelectedProperty); set => SetValue(IsLightSelectedProperty, value); }
    public bool IsDarkSelected { get => GetValue(IsDarkSelectedProperty); set => SetValue(IsDarkSelectedProperty, value); }
    public bool IsSystemSelected { get => GetValue(IsSystemSelectedProperty); set => SetValue(IsSystemSelectedProperty, value); }

    public ThemeSelectorControl() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnSystemThemeChanged;
            UpdateSystemThemeInfo();
        }

        Dispatcher.UIThread.Post(() =>
        {
            var theme = SelectedTheme;
            _isUpdatingState = true;
            IsLightSelected = theme == AppTheme.light;
            IsDarkSelected = theme == AppTheme.dark;
            IsSystemSelected = theme == AppTheme.system;
            _isUpdatingState = false;
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Application.Current?.ActualThemeVariantChanged -= OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e) => UpdateSystemThemeInfo();

    private void UpdateSystemThemeInfo() => IsSystemDarkNow = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isUpdatingState) return;

        try
        {
            _isUpdatingState = true;

            if (change.Property == SelectedThemeProperty)
            {
                var theme = (AppTheme)change.NewValue!;
                IsLightSelected = theme == AppTheme.light;
                IsDarkSelected = theme == AppTheme.dark;
                IsSystemSelected = theme == AppTheme.system;
            }
            else if (change.Property == IsLightSelectedProperty && change.NewValue is true)
            {
                SelectedTheme = AppTheme.light;
            }
            else if (change.Property == IsDarkSelectedProperty && change.NewValue is true)
            {
                SelectedTheme = AppTheme.dark;
            }
            else if (change.Property == IsSystemSelectedProperty && change.NewValue is true)
            {
                SelectedTheme = AppTheme.system;
            }
        }
        finally
        {
            _isUpdatingState = false;
        }
    }
}