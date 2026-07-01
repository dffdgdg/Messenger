using Avalonia.Styling;
using Core.Infrastructure.Theming;
using Core.Services.Platform.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AppTheme = Shared.Enum.Theme;
using AvaloniaApp = Avalonia.Application;

namespace Core.Services.Platform.UI;

public partial class ThemeService(ISettingsService settings) : IThemeService
{
    private const string AccentColorKey = "AccentColor";
    private const string UseSystemAccentKey = "UseSystemAccent";
    private const string ThemeKey = "Theme";

    public bool IsColoredTheme { get; private set; }

    public static readonly Color[] PresetAccents =
    [
        Color.Parse("#3390EC"),
        Color.Parse("#45B97C"),
        Color.Parse("#E2527A"),
        Color.Parse("#E8892B"),
        Color.Parse("#9C5FB5"),
        Color.Parse("#E05454"),
        Color.Parse("#C49B2F"),
    ];

    private static readonly string[] ColoredOnlyKeys =
    [
        "TopBg", "BaseBg",
        "PrimaryBG", "SecondaryBG", "ThirdBG", "FourthBG",
        "ChatHeaderBg", "ChatComposerBg", "ChatComposerBorder",
        "ChatMessagesBgTop", "ChatMessagesBgBottom",
        "ChatGlowPrimary", "ChatGlowSecondary",
        "TextPrimary", "TextSecondary", "TextMuted",
        "Icon",
        "BorderDefault", "BorderHover",
        "ControlBg", "ControlBorder",
        "AvatarPlaceholderBG", "HoverOverlay",
        "MessageOtherBackground",
        "CardBgBrush", "CardHoverBrush", "ItemBgBrush", "HierarchyLineBrush",
        "ChatItemHoverBg",
        "OnlineIndicatorBorder",
        "LoginBgStart", "LoginBgMiddle", "LoginBgEnd",
        "LoginGlowPrimary", "LoginGlowSecondary",
        "LoginCardBg", "LoginCardBorder",
    ];

    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly AvaloniaApp _app = AvaloniaApp.Current
               ?? throw new InvalidOperationException("Application is not initialized");

    private ResourceDictionary? _lightDict;
    private ResourceDictionary? _darkDict;

    public Color CurrentAccent { get; private set; } = PresetAccents[0];
    public bool UseSystemAccent { get; private set; }
    public bool IsDarkTheme => _app.RequestedThemeVariant == ThemeVariant.Dark || ActualIsDark();

    public void Toggle()
    {
        var newTheme = IsDarkTheme ? AppTheme.light : AppTheme.dark;
        SaveTheme(newTheme);
        SwitchTheme(newTheme);
    }

    public void LoadFromSettings()
    {
        try
        {
            if (_lightDict == null || _darkDict == null)
            {
                Debug.WriteLine("[ThemeService] WARNING: dictionaries not initialized!");
            }

            var theme = _settings.Get<AppTheme?>(ThemeKey) ?? AppTheme.system;

            SwitchTheme(theme);

            UseSystemAccent = _settings.Get<bool>(UseSystemAccentKey);

            if (UseSystemAccent)
            {
                var sysColor = GetSystemAccentColorSync();
                if (sysColor.HasValue)
                    CurrentAccent = sysColor.Value;
            }
            else
            {
                var hex = _settings.Get<string?>(AccentColorKey);
                if (hex != null && Color.TryParse(hex, out var saved))
                    CurrentAccent = saved;
            }

            if (theme != AppTheme.colored && (UseSystemAccent || _settings.Get<string?>(AccentColorKey) != null))
            {
                ApplyAccentInternal(CurrentAccent, ActualIsDark());
            }

            Debug.WriteLine($"[ThemeService] Loaded. Theme={theme}, Accent={CurrentAccent}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] LoadFromSettings error: {ex.Message}");
        }
    }

    public void SaveTheme(AppTheme theme)
    {
        try { _settings.Set(ThemeKey, theme); }
        catch (Exception ex) { Debug.WriteLine($"[ThemeService] SaveTheme error: {ex.Message}"); }
    }

    public void SetAccent(Color accent, bool saveToSettings = true)
    {
        UseSystemAccent = false;
        CurrentAccent = accent;

        if (IsColoredTheme)
        {
            var useDarkBase = ColoredPaletteGenerator.ShouldUseDarkBase(accent);
            _app.RequestedThemeVariant = useDarkBase ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplyColoredPalette(accent);
        }
        else
        {
            ApplyAccentInternal(accent, ActualIsDark());
        }

        if (!saveToSettings) return;
        _settings.Set(AccentColorKey, accent.ToString());
        _settings.Set(UseSystemAccentKey, false);
    }

    public void ApplyColoredTheme(Color accent)
    {
        IsColoredTheme = true;
        CurrentAccent = accent;

        var useDarkBase = ColoredPaletteGenerator.ShouldUseDarkBase(accent);
        _app.RequestedThemeVariant = useDarkBase ? ThemeVariant.Dark : ThemeVariant.Light;

        ApplyColoredPalette(accent);
        _settings.Set(AccentColorKey, accent.ToString());
    }

    public async Task SetUseSystemAccentAsync(bool use)
    {
        UseSystemAccent = use;
        _settings.Set(UseSystemAccentKey, use);

        if (use)
        {
            var sysColor = await GetSystemAccentColorAsync();
            if (sysColor.HasValue)
            {
                CurrentAccent = sysColor.Value;
                if (IsColoredTheme)
                    ApplyColoredPalette(CurrentAccent);
                else
                    ApplyAccentInternal(CurrentAccent, ActualIsDark());
            }
        }
        else
        {
            var hex = _settings.Get<string?>(AccentColorKey);
            var color = (hex != null && Color.TryParse(hex, out var c)) ? c : PresetAccents[0];
            SetAccent(color);
        }
    }

    public void SwitchTheme(AppTheme theme)
    {
        _ = IsColoredTheme;
        IsColoredTheme = theme == AppTheme.colored;

        if (theme != AppTheme.colored)
        {
            _app.RequestedThemeVariant = theme switch
            {
                AppTheme.light => ThemeVariant.Light,
                AppTheme.system => ThemeVariant.Default,
                _ => ThemeVariant.Dark,
            };
        }

        var isDark = theme switch
        {
            AppTheme.light => false,
            AppTheme.dark => true,
            AppTheme.system => _app.ActualThemeVariant == ThemeVariant.Dark,
            AppTheme.colored => ColoredPaletteGenerator.ShouldUseDarkBase(CurrentAccent),
            _ => true
        };

        if (!IsColoredTheme && _lightDict != null && _darkDict != null)
            RestoreFromDictionary(isDark);

        if (!IsColoredTheme)
            ApplyAccentInternal(CurrentAccent, isDark);
    }

    private void ApplyAccentInternal(Color accent, bool isDark)
    {
        var palette = AccentPaletteGenerator.Generate(accent, isDark);
        PatchResources(palette);
    }

    private void PatchResources(AccentPalette p)
    {
        var res = _app.Resources;

        Patch(res, "Accent", p.Accent);
        Patch(res, "AccentHover", p.AccentHover);
        Patch(res, "AccentPressed", p.AccentPressed);
        Patch(res, "AccentMuted", p.AccentMuted);
        Patch(res, "MessageOwnBackground", p.MessageOwn);
        Patch(res, "NavButtonSelectedBg", p.NavSelectedBg);
        Patch(res, "NavButtonSelectedFg", p.NavSelectedFg);
        Patch(res, "ChatItemSelectedBg", p.ChatItemSelectedBg);
        Patch(res, "PinnedBannerBg", p.PinnedBannerBg);
        Patch(res, "InfoBgBrush", p.InfoBg);
        Patch(res, "InfoBorderBrush", p.InfoBorder);
        Patch(res, "ChatGlowPrimary", p.ChatGlowPrimary);
        Patch(res, "ChatGlowSecondary", p.ChatGlowSecondary);
        PatchColor(res, "LoginLogoStart", p.LoginLogoStart);
        PatchColor(res, "LoginLogoEnd", p.LoginLogoEnd);
    }

    private void ApplyColoredPalette(Color accent)
    {
        var p = ColoredPaletteGenerator.Generate(accent);
        var res = _app.Resources;

        PatchColor(res, "TopBg", p.PrimaryBg);
        PatchColor(res, "BaseBg", p.SecondaryBg);
        Patch(res, "PrimaryBG", p.PrimaryBg);
        Patch(res, "SecondaryBG", p.SecondaryBg);
        Patch(res, "ThirdBG", p.ThirdBg);
        Patch(res, "FourthBG", p.FourthBg);
        Patch(res, "ChatHeaderBg", p.ChatHeaderBg);
        Patch(res, "ChatComposerBg", p.ChatComposerBg);
        Patch(res, "ChatComposerBorder", p.ChatComposerBorder);
        PatchColor(res, "ChatMessagesBgTop", p.ChatMsgBgTop);
        PatchColor(res, "ChatMessagesBgBottom", p.ChatMsgBgBottom);
        Patch(res, "ChatGlowPrimary", p.ChatGlowPrimary);
        Patch(res, "ChatGlowSecondary", p.ChatGlowSecondary);
        Patch(res, "Accent", p.Accent);
        Patch(res, "AccentHover", p.AccentHover);
        Patch(res, "AccentPressed", p.AccentPressed);
        Patch(res, "AccentMuted", p.AccentMuted);
        Patch(res, "TextPrimary", p.TextPrimary);
        Patch(res, "TextSecondary", p.TextSecondary);
        Patch(res, "TextMuted", p.TextMuted);
        Patch(res, "Icon", p.Icon);
        Patch(res, "BorderDefault", p.BorderDefault);
        Patch(res, "BorderHover", p.BorderHover);
        Patch(res, "ControlBg", p.ControlBg);
        Patch(res, "ControlBorder", p.ControlBorder);
        Patch(res, "AvatarPlaceholderBG", p.AvatarPlaceholder);
        Patch(res, "HoverOverlay", p.HoverOverlay);
        Patch(res, "MessageOwnBackground", p.MessageOwn);
        Patch(res, "MessageOtherBackground", p.MessageOther);
        Patch(res, "NavButtonSelectedBg", p.NavSelectedBg);
        Patch(res, "NavButtonSelectedFg", p.NavSelectedFg);
        Patch(res, "ChatItemSelectedBg", p.ChatItemSelectedBg);
        Patch(res, "ChatItemHoverBg", p.ChatItemHoverBg);
        Patch(res, "CardBgBrush", p.CardBg);
        Patch(res, "CardHoverBrush", p.CardHover);
        Patch(res, "ItemBgBrush", p.ItemBg);
        Patch(res, "HierarchyLineBrush", p.HierarchyLine);
        Patch(res, "PinnedBannerBg", p.PinnedBannerBg);
        Patch(res, "OnlineIndicatorBorder", p.OnlineIndicatorBorder);
        Patch(res, "InfoBgBrush", p.InfoBg);
        Patch(res, "InfoBorderBrush", p.InfoBorder);
        PatchColor(res, "LoginBgStart", p.LoginBgStart);
        PatchColor(res, "LoginBgMiddle", p.LoginBgMiddle);
        PatchColor(res, "LoginBgEnd", p.LoginBgEnd);
        Patch(res, "LoginGlowPrimary", p.LoginGlowPrimary);
        Patch(res, "LoginGlowSecondary", p.LoginGlowSecondary);
        PatchColor(res, "LoginLogoStart", p.LoginLogoStart);
        PatchColor(res, "LoginLogoEnd", p.LoginLogoEnd);
        Patch(res, "LoginCardBg", p.LoginCardBg);
        Patch(res, "LoginCardBorder", p.LoginCardBorder);
    }

    public void Initialize(ResourceDictionary lightDict, ResourceDictionary darkDict)
    {
        _lightDict = lightDict;
        _darkDict = darkDict;
        Debug.WriteLine($"[ThemeService] Initialized: light={lightDict.Count}, dark={darkDict.Count}");
    }

    /// <summary>
    /// Восстанавливает ColoredOnlyKeys из нужного словаря.
    /// Вызывается когда уходим с colored темы.
    /// </summary>
    private void RestoreFromDictionary(bool isDark)
    {
        var source = isDark ? _darkDict : _lightDict;
        if (source == null)
        {
            Debug.WriteLine("[ThemeService] RESTORE FAILED: dictionary is null");
            return;
        }

        Debug.WriteLine($"[ThemeService] RESTORE START: isDark={isDark}, keys={source.Count}");

        var res = _app.Resources;
        var restored = 0;
        var missed = 0;

        foreach (var key in ColoredOnlyKeys)
        {
            if (!source.TryGetResource(key, null, out var value) || value == null)
            {
                Debug.WriteLine($"[ThemeService] RESTORE MISS: key='{key}' not found in source dict");
                missed++;
                continue;
            }

            switch (value)
            {
                case SolidColorBrush sourceBrush:
                    if (res.TryGetValue(key, out var existing) && existing is SolidColorBrush target)
                    {
                        Debug.WriteLine($"[ThemeService] RESTORE BRUSH: {key} → {sourceBrush.Color}");
                        target.Color = sourceBrush.Color;
                    }
                    else
                    {
                        Debug.WriteLine($"[ThemeService] RESTORE NEW BRUSH: {key} → {sourceBrush.Color}");
                        res[key] = new SolidColorBrush(sourceBrush.Color);
                    }
                    restored++;
                    break;

                case Color color:
                    Debug.WriteLine($"[ThemeService] RESTORE COLOR: {key} → {color}");
                    res[key] = color;
                    restored++;
                    break;

                default:
                    Debug.WriteLine($"[ThemeService] RESTORE UNKNOWN TYPE: {key} = {value?.GetType().Name}");
                    break;
            }
        }

        Debug.WriteLine($"[ThemeService] RESTORE DONE: restored={restored}, missed={missed}");
    }

    private static void Patch(IResourceDictionary res, string key, Color color)
    {
        if (res.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
            brush.Color = color;
        else
            res[key] = new SolidColorBrush(color);
    }

    private static void PatchColor(IResourceDictionary res, string key, Color color)
        => res[key] = color;

    private static Color? GetSystemAccentColorSync()
    {
        try
        {
            return GetSystemAccentColorAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] GetSystemAccentColorSync failed: {ex.Message}");
            return null;
        }
    }

    public static async Task<Color?> GetSystemAccentColorAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsAccent();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return await GetLinuxAccentAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] System accent error: {ex.Message}");
        }
        return null;
    }

    private static Color? GetWindowsAccent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");

            if (key?.GetValue("AccentColorMenu") is int raw)
            {
                byte b = (byte)((raw >> 16) & 0xFF);
                byte g = (byte)((raw >> 8) & 0xFF);
                byte r = (byte)(raw & 0xFF);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] GetWindowsAccent failed: {ex.Message}");
        }
        return null;
    }

    [GeneratedRegex(@"[\d.]+(?:e[+-]?\d+)?")]
    private static partial Regex LinuxAccentNumberPattern();

    private static async Task<Color?> GetLinuxAccentAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gdbus",
                    Arguments = "call --session --dest org.freedesktop.portal.Desktop " +
                                "--object-path /org/freedesktop/portal/desktop " +
                                "--method org.freedesktop.portal.Settings.Read " +
                                "org.freedesktop.appearance accent-color",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var matches = LinuxAccentNumberPattern().Matches(output);

            if (matches.Count >= 3
                && double.TryParse(matches[0].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var r)
                && double.TryParse(matches[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var g)
                && double.TryParse(matches[2].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var b))
            {
                return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] GetLinuxAccentAsync failed: {ex.Message}");
        }
        return null;
    }

    private bool ActualIsDark()
    {
        var variant = _app.RequestedThemeVariant;
        if (variant == ThemeVariant.Dark) return true;
        if (variant == ThemeVariant.Light) return false;
        return _app.ActualThemeVariant == ThemeVariant.Dark;
    }
}