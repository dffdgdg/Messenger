namespace Core.Infrastructure.Theming;

public static class AccentPaletteGenerator
{
    /// <param name="accent">Seed-цвет (HEX или выбранный)</param>
    /// <param name="isDark">Тёмная или светлая база</param>
    public static AccentPalette Generate(Color accent, bool isDark)
    {
        var (h, s, l) = ToHsl(accent);
        return isDark ? BuildDark(h, s, l) : BuildLight(h, s, l);
    }

    private static AccentPalette BuildDark(double h, double s, double l)
    {
        double aL = Clamp(l, 0.45, 0.65);
        double aS = Clamp(s, 0.55, 0.90);

        var accent = FromHsl(h, aS, aL);
        var accentHover = FromHsl(h, aS, Clamp(aL + 0.10, 0.50, 0.75));
        var accentPressed = FromHsl(h, aS, Clamp(aL - 0.08, 0.35, 0.58));
        var accentMuted = WithAlpha(accent, 0x40);

        var msgOwn = FromHsl(h, Clamp(aS * 0.70, 0.30, 0.60), Clamp(aL - 0.20, 0.18, 0.35));
        var navBg = FromHsl(h, Clamp(aS * 0.40, 0.15, 0.45), Clamp(aL - 0.25, 0.12, 0.28));
        var navFg = accent;
        var chatSelBg = FromHsl(h, Clamp(aS * 0.45, 0.18, 0.48), Clamp(aL - 0.22, 0.14, 0.30));
        var pinnedBg = FromHsl(h, Clamp(aS * 0.45, 0.18, 0.48), Clamp(aL - 0.20, 0.15, 0.28));
        var infoBg = WithAlpha(accent, 0x2D);
        var infoBorder = WithAlpha(accent, 0x80);
        var logoStart = FromHsl(h, Clamp(aS, 0.60, 0.90), Clamp(aL + 0.05, 0.50, 0.70));
        var logoEnd = FromHsl(h, Clamp(aS, 0.60, 0.90), Clamp(aL - 0.12, 0.32, 0.52));
        var glowPrimary = WithAlpha(accent, 0x14);
        var glowSecondary = WithAlpha(FromHsl(Wrap(h + 0.08), aS, aL), 0x10);

        return new AccentPalette(
            accent, accentHover, accentPressed, accentMuted,
            msgOwn,
            navBg, navFg, chatSelBg,
            pinnedBg,
            infoBg, infoBorder,
            logoStart, logoEnd,
            glowPrimary, glowSecondary
        );
    }

    private static AccentPalette BuildLight(double h, double s, double l)
    {
        double aL = Clamp(l, 0.38, 0.55);
        double aS = Clamp(s, 0.55, 0.85);

        var accent = FromHsl(h, aS, aL);
        var accentHover = FromHsl(h, aS, Clamp(aL - 0.08, 0.28, 0.48));
        var accentPressed = FromHsl(h, aS, Clamp(aL - 0.15, 0.22, 0.42));
        var accentMuted = WithAlpha(accent, 0x20);

        var msgOwn = FromHsl(h, Clamp(aS * 0.35, 0.15, 0.45), Clamp(aL + 0.35, 0.78, 0.93));
        var navBg = FromHsl(h, Clamp(aS * 0.30, 0.12, 0.40), Clamp(aL + 0.38, 0.82, 0.95));
        var navFg = accent;
        var chatSelBg = FromHsl(h, Clamp(aS * 0.32, 0.14, 0.42), Clamp(aL + 0.36, 0.80, 0.93));
        var pinnedBg = FromHsl(h, Clamp(aS * 0.28, 0.10, 0.38), Clamp(aL + 0.38, 0.83, 0.95));

        var infoBg = WithAlpha(accent, 0x15);
        var infoBorder = WithAlpha(accent, 0x60);

        var logoStart = FromHsl(h, Clamp(aS, 0.60, 0.85), Clamp(aL + 0.08, 0.48, 0.68));
        var logoEnd = FromHsl(h, Clamp(aS, 0.60, 0.85), Clamp(aL - 0.08, 0.28, 0.46));

        var glowPrimary = WithAlpha(accent, 0x08);
        var glowSecondary = WithAlpha(FromHsl(Wrap(h + 0.08), aS, aL), 0x05);

        return new AccentPalette(
            accent, accentHover, accentPressed, accentMuted,
            msgOwn,
            navBg, navFg, chatSelBg,
            pinnedBg,
            infoBg, infoBorder,
            logoStart, logoEnd,
            glowPrimary, glowSecondary
        );
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-10)
            return (0, 0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h = max switch
        {
            _ when Math.Abs(max - r) < 1e-10 => (g - b) / d + (g < b ? 6 : 0),
            _ when Math.Abs(max - g) < 1e-10 => (b - r) / d + 2,
            _ => (r - g) / d + 4,
        };

        return (h / 6.0, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = Wrap(h);
        if (s < 1e-10)
        {
            var v = (byte)Math.Round(Clamp(l, 0, 1) * 255);
            return Color.FromRgb(v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;

        return Color.FromRgb(
            ToByte(HueChan(p, q, h + 1.0 / 3)),
            ToByte(HueChan(p, q, h)),
            ToByte(HueChan(p, q, h - 1.0 / 3))
        );
    }

    private static double HueChan(double p, double q, double t)
    {
        t = Wrap(t);
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    /// <summary>Добавляет альфа-канал (0–255) к цвету без альфы</summary>
    private static Color WithAlpha(Color c, byte alpha)
        => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static double Clamp(double v, double min, double max)
        => v < min ? min : v > max ? max : v;

    private static double Wrap(double h)
    {
        while (h < 0) h += 1;
        while (h >= 1) h -= 1;
        return h;
    }

    private static byte ToByte(double v)
        => (byte)Math.Round(Clamp(v, 0, 1) * 255);
}