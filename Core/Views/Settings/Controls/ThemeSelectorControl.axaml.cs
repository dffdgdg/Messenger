using Core.ViewModels;
using AppTheme = Shared.Enum.Theme;

namespace Core.Views.Controls;

public partial class ThemeSelectorControl : UserControl
{
    public static readonly StyledProperty<AppTheme> SelectedThemeProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, AppTheme>(nameof(SelectedTheme));

    public static readonly StyledProperty<Color> AccentColorProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, Color>(nameof(AccentColor), Color.Parse("#3390EC"));

    public static readonly StyledProperty<AccentPickerViewModel?> AccentPickerProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, AccentPickerViewModel?>(nameof(AccentPicker));

    public static readonly StyledProperty<bool> IsLightSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsLightSelected));
    public static readonly StyledProperty<bool> IsDarkSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsDarkSelected));
    public static readonly StyledProperty<bool> IsSystemSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsSystemSelected));
    public static readonly StyledProperty<bool> IsColoredSelectedProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, bool>(nameof(IsColoredSelected));

    public static readonly StyledProperty<Color> PreviewAccentColorProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, Color>(nameof(PreviewAccentColor), Color.Parse("#3390EC"));

    public static readonly StyledProperty<Color> ColoredBgColorProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, Color>(nameof(ColoredBgColor), Color.Parse("#0D1829"));

    public static readonly StyledProperty<Color> ColoredBg2ColorProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, Color>(nameof(ColoredBg2Color), Color.Parse("#162235"));

    public static readonly StyledProperty<Color> ColoredSidebarBgProperty =
        AvaloniaProperty.Register<ThemeSelectorControl, Color>(nameof(ColoredSidebarBg), Color.Parse("#0F1C2E"));

    private bool _isUpdatingState;

    public AppTheme SelectedTheme
    {
        get => GetValue(SelectedThemeProperty);
        set => SetValue(SelectedThemeProperty, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public AccentPickerViewModel? AccentPicker
    {
        get => GetValue(AccentPickerProperty);
        set => SetValue(AccentPickerProperty, value);
    }

    public bool IsLightSelected
    {
        get => GetValue(IsLightSelectedProperty);
        set => SetValue(IsLightSelectedProperty, value);
    }

    public bool IsDarkSelected
    {
        get => GetValue(IsDarkSelectedProperty);
        set => SetValue(IsDarkSelectedProperty, value);
    }

    public bool IsSystemSelected
    {
        get => GetValue(IsSystemSelectedProperty);
        set => SetValue(IsSystemSelectedProperty, value);
    }

    public bool IsColoredSelected
    {
        get => GetValue(IsColoredSelectedProperty);
        set => SetValue(IsColoredSelectedProperty, value);
    }

    public Color PreviewAccentColor
    {
        get => GetValue(PreviewAccentColorProperty);
        set => SetValue(PreviewAccentColorProperty, value);
    }

    public Color ColoredBgColor
    {
        get => GetValue(ColoredBgColorProperty);
        set => SetValue(ColoredBgColorProperty, value);
    }

    public Color ColoredBg2Color
    {
        get => GetValue(ColoredBg2ColorProperty);
        set => SetValue(ColoredBg2ColorProperty, value);
    }

    public Color ColoredSidebarBg
    {
        get => GetValue(ColoredSidebarBgProperty);
        set => SetValue(ColoredSidebarBgProperty, value);
    }

    public ThemeSelectorControl()
    {
        InitializeComponent();

        Application.Current?.ActualThemeVariantChanged += OnSystemThemeChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() =>
        {
            SyncCheckboxesFromTheme(SelectedTheme);
            UpdatePreviewColors(AccentColor);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Application.Current?.ActualThemeVariantChanged -= OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e) { }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isUpdatingState) return;

        _isUpdatingState = true;
        try
        {
            if (change.Property == SelectedThemeProperty)
            {
                SyncCheckboxesFromTheme((AppTheme)change.NewValue!);
            }
            else if (change.Property == AccentColorProperty)
            {
                UpdatePreviewColors((Color)change.NewValue!);
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
            else if (change.Property == IsColoredSelectedProperty && change.NewValue is true)
            {
                SelectedTheme = AppTheme.colored;
            }
        }
        finally
        {
            _isUpdatingState = false;
        }
    }

    private void SyncCheckboxesFromTheme(AppTheme theme)
    {
        IsLightSelected = theme == AppTheme.light;
        IsDarkSelected = theme == AppTheme.dark;
        IsSystemSelected = theme == AppTheme.system;
        IsColoredSelected = theme == AppTheme.colored;
    }

    private void UpdatePreviewColors(Color accent)
    {
        PreviewAccentColor = accent;

        var (h, s, _) = ToHsl(accent);
        double bgS = Math.Min(s * 0.65, 0.55);
        bgS = Math.Max(bgS, 0.20);

        ColoredBgColor = FromHsl(h, bgS, 0.065);
        ColoredBg2Color = FromHsl(h, bgS, 0.105);
        ColoredSidebarBg = FromHsl(h, bgS, 0.09);
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-10) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h = Math.Abs(max - r) < 1e-10
            ? (g - b) / d + (g < b ? 6 : 0)
            : Math.Abs(max - g) < 1e-10
                ? (b - r) / d + 2
                : (r - g) / d + 4;
        return (h / 6.0, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        while (h < 0) h += 1;
        while (h >= 1) h -= 1;
        if (s < 1e-10)
        {
            var v = (byte)Math.Round(Math.Clamp(l, 0, 1) * 255);
            return Color.FromRgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromRgb(Chan(p, q, h + 1.0 / 3), Chan(p, q, h), Chan(p, q, h - 1.0 / 3));
    }

    private static byte Chan(double p, double q, double t)
    {
        while (t < 0) t += 1;
        while (t >= 1) t -= 1;
        double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
            : t < 0.5 ? q
            : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
            : p;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }
}