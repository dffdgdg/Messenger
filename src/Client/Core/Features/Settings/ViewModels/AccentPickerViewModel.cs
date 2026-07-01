using Core.Infrastructure.Theming;
using Core.Services.Platform.Abstractions;
using Core.Services.Platform.UI;

namespace Core.Features.Settings.ViewModels;

public partial class AccentPickerViewModel : BaseViewModel
{
    private readonly IThemeService _themeService;
    private bool _updatingHex;

    public sealed partial class PresetItem(Color color) : ObservableObject
    {
        public Color Color { get; } = color;
        [ObservableProperty] public partial bool IsSelected { get; set; }
    }

    public sealed class ShadeItem(Color color)
    {
        public Color Color { get; } = color;
    }

    public IReadOnlyList<PresetItem> Presets { get; }

    [ObservableProperty] public partial bool UseSystemAccent { get; set; }
    [ObservableProperty] public partial Color CustomColor { get; set; } = ThemeService.PresetAccents[0];
    [ObservableProperty] public partial string CustomColorHex { get; set; } = "#3390EC";
    [ObservableProperty] public partial bool IsColorPickerOpen { get; set; }
    [ObservableProperty] public partial bool HasCustomAccent { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ShadeItem> CustomColorShades { get; set; } = [];

    public AccentPickerViewModel(IThemeService themeService)
    {
        _themeService = themeService;

        Presets = [.. ThemeService.PresetAccents.Select(c => new PresetItem(c))];
        UseSystemAccent = themeService.UseSystemAccent;
        SyncSelection(themeService.CurrentAccent);
    }


    [RelayCommand]
    private void SelectPreset(PresetItem item)
    {
        foreach (var p in Presets) p.IsSelected = false;
        item.IsSelected = true;
        HasCustomAccent = false;
        IsColorPickerOpen = false;

        SetAccentAndSync(item.Color, useSystem: false);
    }

    [RelayCommand]
    private void OpenColorPicker()
    {
        IsColorPickerOpen = !IsColorPickerOpen;
        if (IsColorPickerOpen)
            RebuildShades(CustomColor);
    }

    [RelayCommand]
    private void ApplyCustomColor()
    {
        foreach (var p in Presets) p.IsSelected = false;
        HasCustomAccent = true;
        IsColorPickerOpen = false;

        SetAccentAndSync(CustomColor, useSystem: false);
    }

    [RelayCommand]
    private void SelectShade(Color color)
    {
        CustomColor = color;
        CustomColorHex = ColorToHex(color);
        RebuildShades(color);
    }

    [RelayCommand]
    private async Task ToggleSystemAccentAsync()
    {
        UseSystemAccent = !UseSystemAccent;

        if (!UseSystemAccent)
        {
            SyncSelection(_themeService.CurrentAccent);
        }
        else
        {
            foreach (var p in Presets) p.IsSelected = false;
            HasCustomAccent = false;
        }

        await _themeService.SetUseSystemAccentAsync(UseSystemAccent);

        if (UseSystemAccent)
        {
            CustomColorHex = ColorToHex(_themeService.CurrentAccent);
        }
    }

    partial void OnCustomColorChanged(Color value)
    {
        if (_updatingHex) return;
        _updatingHex = true;
        CustomColorHex = ColorToHex(value);
        _updatingHex = false;

        RebuildShades(value);
    }

    partial void OnCustomColorHexChanged(string value)
    {
        if (_updatingHex) return;

        var hex = value.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;

        if (Color.TryParse(hex, out var color))
        {
            _updatingHex = true;
            CustomColor = color;
            _updatingHex = false;
            RebuildShades(color);
        }
    }
    private void SetAccentAndSync(Color color, bool useSystem)
    {
        _themeService.SetAccent(color);
        CustomColor = color;
        CustomColorHex = ColorToHex(color);
        UseSystemAccent = useSystem;
    }

    private void SyncSelection(Color current)
    {
        var matched = false;
        foreach (var p in Presets)
        {
            p.IsSelected = p.Color == current;
            if (p.IsSelected) matched = true;
        }

        HasCustomAccent = !matched && !UseSystemAccent;

        if (HasCustomAccent || !matched)
        {
            CustomColor = current;
            CustomColorHex = ColorToHex(current);
        }
    }

    partial void OnUseSystemAccentChanged(bool value) => _ = HandleSystemAccentToggleAsync(value);

    private async Task HandleSystemAccentToggleAsync(bool use)
    {
        if (use)
        {
            foreach (var p in Presets) p.IsSelected = false;
            HasCustomAccent = false;
        }
        else
        {
            SyncSelection(_themeService.CurrentAccent);
        }

        await _themeService.SetUseSystemAccentAsync(use);

        if (use)
            CustomColorHex = ColorToHex(_themeService.CurrentAccent);
    }
    /// <summary>
    /// Строит 7 оттенков: -30%, -20%, -10%, base, +10%, +20%, +30% по L
    /// </summary>
    private void RebuildShades(Color baseColor)
    {
        var (h, s, l) = AccentPaletteGenerator.ToHsl(baseColor);

        var offsets = new[] { -0.25, -0.15, -0.07, 0.0, 0.07, 0.15, 0.25 };

        CustomColorShades = [.. offsets.Select(offset =>
        {
            var nl = Math.Clamp(l + offset, 0.05, 0.95);
            return new ShadeItem(AccentPaletteGenerator.FromHsl(h, s, nl));
        })];
    }

    private static string ColorToHex(Color c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}